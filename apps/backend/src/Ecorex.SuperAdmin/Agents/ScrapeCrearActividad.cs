using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Paso "Crear actividad" del dron (server-side, no va al navegador). Por cada registro que el flujo tiene
/// en el contexto (p.ej. el pedido que acaba de leer el bucle de Pendientes) crea UNA tarea del concepto
/// configurado y le deja el formulario del concepto ya diligenciado: campos de cabecera y una grilla
/// (GridDetail) con los items. NO DUPLICA: la tarea creada se anota en una columna de la fila del
/// Contenedor que tiene la clave (p.ej. Cotizaciones.PETOF -> Cotizaciones.TAREA); si la fila ya tiene
/// tarea, el paso la omite. El historico no lleva este paso, asi que solo llega al Contenedor.
///
/// No se envuelve en una transaccion propia porque ITaskItemService.CreateAsync abre la suya (EF no anida).
/// En su lugar la columna de la tarea se RESERVA ("CREANDO...") antes de crear: si algo se cae a mitad, la
/// fila queda marcada y la siguiente corrida NO crea otra tarea (se reporta para revision manual).
/// La config vive en ScrapeStep.MappingJson (mismo patron que los demas pasos de servidor).
/// </summary>
public static class ScrapeCrearActividad
{
    public const string Reserva = "CREANDO...";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Regex Placeholder = new(@"\{\{[^{}]*\}\}|@@[^@]*@@", RegexOptions.Compiled);

    /// <summary>Config del paso, serializada en MappingJson (camelCase).</summary>
    public sealed record Config
    {
        /// <summary>Concepto (subcategoria): define tablero/columna y el formulario de la tarea.</summary>
        public Guid? SubcategoriaId { get; init; }
        /// <summary>Plantilla del titulo con {{ruta}} (ej. "Pedido INCAUCA {{DETALLE.cabecera.petof}}").</summary>
        public string? Titulo { get; init; }
        /// <summary>Plantilla de la descripcion (opcional).</summary>
        public string? Descripcion { get; init; }
        /// <summary>Ruta (o plantilla) de la clave que identifica el registro (ej. DETALLE.cabecera.petof).</summary>
        public string? Clave { get; init; }
        /// <summary>Contenedor donde vive la fila del registro (ej. Drones Ingenios / Cotizaciones).</summary>
        public Guid? ContenedorId { get; init; }
        /// <summary>Columna del contenedor con la clave (ej. PETOF).</summary>
        public string? ColumnaClave { get; init; }
        /// <summary>Columna del contenedor donde se anota el numero de la tarea creada (ej. TAREA).</summary>
        public string? ColumnaTarea { get; init; }
        /// <summary>TenantUser al que se asigna (opcional; sin el, la tarea nace sin responsable).</summary>
        public Guid? ResponsableId { get; init; }
        /// <summary>Plantillas del solicitante (opcionales).</summary>
        public string? Solicitante { get; init; }
        public string? DocumentoSolicitante { get; init; }
        /// <summary>Campos de cabecera: codigo del campo del formulario -> ruta o plantilla.</summary>
        public Dictionary<string, string>? Campos { get; init; }
        /// <summary>Codigo del campo GridDetail que recibe los items (opcional).</summary>
        public string? GrillaCampo { get; init; }
        /// <summary>Ruta del arreglo de items (ej. DETALLE.items).</summary>
        public string? ItemsRuta { get; init; }
        /// <summary>Columnas de la grilla: id de columna -> propiedad del item.</summary>
        public Dictionary<string, string>? GrillaColumnas { get; init; }
        /// <summary>Guarda opcional: si la ruta resuelve a este valor (sin distinguir mayusculas), no se crea.</summary>
        public string? SaltarSiRuta { get; init; }
        public string? SaltarSiValor { get; init; }
        /// <summary>Ruta o plantilla de la FECHA DE VENCIMIENTO de la tarea (ej. DETALLE.cabecera.plazoOferta,
        /// "17/11/2026"). Se toma como fin del dia en la zona del tenant. Vacia o ilegible = sin vencimiento.</summary>
        public string? Vencimiento { get; init; }
        /// <summary>Valores FIJOS de cabecera (codigo del campo -> literal), para lo que el registro no trae
        /// (ej. moneda "PESO COLOMBIANO", vigencia "{hoy+30}"). Acepta los tokens {hoy}/{hoy+N}. No pisa un
        /// campo que ya lleno <see cref="Campos"/>.</summary>
        public Dictionary<string, string>? CamposFijos { get; init; }
        /// <summary>Prellenado de la grilla con la ULTIMA respuesta conocida del mismo codigo (opcional).</summary>
        public PrellenadoConfig? Prellenado { get; init; }
    }

    /// <summary>
    /// Sugerencias para las celdas VACIAS de la grilla tomadas de un Contenedor historico (ej. Drones Ingenios /
    /// Items, con lo que ya se cotizo): por cada fila se busca en el contenedor la fila con la misma clave
    /// (ej. CODIGO) y se elige la mas reciente segun <see cref="OrdenColumna"/>. Nunca pisa lo que ya trae la fila.
    /// </summary>
    public sealed record PrellenadoConfig
    {
        /// <summary>Contenedor historico.</summary>
        public Guid? ContenedorId { get; init; }
        /// <summary>Columna de la GRILLA con la clave de busqueda (ej. codigo).</summary>
        public string? ClaveGrilla { get; init; }
        /// <summary>Columna del CONTENEDOR con esa clave (ej. CODIGO).</summary>
        public string? ColumnaClave { get; init; }
        /// <summary>Columna del contenedor que ordena: gana el mayor (numerico si se puede; ej. PETOF).</summary>
        public string? OrdenColumna { get; init; }
        /// <summary>Columna del contenedor que identifica el registro (ej. PETOF): se excluyen las filas del
        /// MISMO registro que se esta creando, para no sugerirse a si mismo.</summary>
        public string? ExcluirColumna { get; init; }
        /// <summary>Solo cuentan las filas con valor en esta columna (ej. VLR_UNI: que si se cotizaron).</summary>
        public string? RequiereColumna { get; init; }
        /// <summary>Columnas a sugerir: id de columna de la grilla -> columna del contenedor.</summary>
        public Dictionary<string, string>? Columnas { get; init; }
    }

    public sealed record Result(bool Ok, bool Created, string Detail, string? TaskNumber);

    /// <summary>Nombre por defecto de la variable con el numero de la tarea (si el paso no define OutputVar).</summary>
    public const string DefaultOutputVar = "TAREA";

    /// <summary>
    /// Variables que el paso deja en el contexto para los pasos siguientes (p.ej. Notificar): el numero de la
    /// tarea (creada o ya existente) en <c>{{TAREA}}</c> y si se creo en ESTA corrida en <c>{{TAREA_NUEVA}}</c>
    /// ("1"/"0"). El nombre base sale del OutputVar del paso si lo tiene. Sin tarea -> vacio y "0".
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> OutputVars(ScrapeStep step, Result r)
    {
        var name = string.IsNullOrWhiteSpace(step.OutputVar) ? DefaultOutputVar : step.OutputVar.Trim();
        return [(name, r.TaskNumber ?? string.Empty), (name + "_NUEVA", r.Created ? "1" : "0")];
    }

    public static Config? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try { return JsonSerializer.Deserialize<Config>(json, Web); }
        catch (JsonException) { return null; }
    }

    public static string Serialize(Config cfg) => JsonSerializer.Serialize(cfg, Web);

    /// <summary>
    /// Resuelve una expresion contra el contexto: si trae {{ }} / @@ @@ es una PLANTILLA (lo no resuelto se
    /// quita); si no, es una RUTA (ej. DETALLE.cabecera.petof) y se resuelve directo. Sin valor -> "".
    /// </summary>
    public static string Resolve(ScrapeRunContext ctx, string? expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) { return string.Empty; }
        if (expr.Contains("{{") || expr.Contains("@@"))
        {
            var s = ctx.Substitute(expr) ?? string.Empty;
            return Placeholder.Replace(s, string.Empty).Trim();
        }
        return ctx.TryResolve(expr.Trim(), out var v) ? (v ?? string.Empty).Trim() : string.Empty;
    }

    public static async Task<Result> RunAsync(IServiceProvider sp, IApplicationDbContext db, Guid tenantId,
        ScrapeStep step, ScrapeRunContext ctx, string actorName, CancellationToken ct)
    {
        var cfg = Parse(step.MappingJson);
        if (cfg is null) { return Fail("El paso 'Crear actividad' no tiene configuracion."); }
        if (cfg.SubcategoriaId is not Guid subId) { return Fail("Configura el concepto (sub-categoria) de la actividad."); }

        // Guarda: p.ej. no crear si el registro ya viene "Concluida".
        if (!string.IsNullOrWhiteSpace(cfg.SaltarSiRuta))
        {
            var actual = Resolve(ctx, cfg.SaltarSiRuta);
            if (string.Equals(actual, (cfg.SaltarSiValor ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return new Result(true, false, $"Omitido: {cfg.SaltarSiRuta} = '{actual}'.", null);
            }
        }

        var clave = Resolve(ctx, cfg.Clave);
        if (string.IsNullOrWhiteSpace(cfg.Clave) || clave.Length == 0)
        {
            return Fail("La clave del registro esta vacia: configura 'Clave' (ej. DETALLE.cabecera.petof) y corre antes el paso que la lee.");
        }

        var concepto = await db.ActividadSubcategorias.AsNoTracking()
            .Where(s => s.Id == subId).Select(s => new { s.Nombre, s.FormDefinitionId, s.TituloAuto })
            .FirstOrDefaultAsync(ct);
        if (concepto is null) { return Fail("El concepto configurado no existe en este tenant."); }

        var titulo = Resolve(ctx, cfg.Titulo);
        if (titulo.Length == 0 && string.IsNullOrWhiteSpace(concepto.TituloAuto)) { titulo = $"{concepto.Nombre} {clave}".Trim(); }
        if (titulo.Length > 200) { titulo = titulo[..200]; }

        // ---- Anti-duplicado ----
        DataContainerCell? marca = null;
        if (cfg.ContenedorId is Guid cid && !string.IsNullOrWhiteSpace(cfg.ColumnaClave) && !string.IsNullOrWhiteSpace(cfg.ColumnaTarea))
        {
            var cols = await db.DataContainerColumns.AsNoTracking()
                .Where(c => c.ContainerId == cid && (c.Name == cfg.ColumnaClave || c.Name == cfg.ColumnaTarea))
                .Select(c => new { c.Id, c.Name }).ToListAsync(ct);
            var keyCol = cols.FirstOrDefault(c => string.Equals(c.Name, cfg.ColumnaClave, StringComparison.Ordinal))?.Id;
            var tareaCol = cols.FirstOrDefault(c => string.Equals(c.Name, cfg.ColumnaTarea, StringComparison.Ordinal))?.Id;
            if (keyCol is null) { return Fail($"El contenedor no tiene la columna clave '{cfg.ColumnaClave}'."); }
            if (tareaCol is null) { return Fail($"El contenedor no tiene la columna '{cfg.ColumnaTarea}' para anotar la tarea. Creala (texto)."); }

            var rowId = await db.DataContainerCells.AsNoTracking()
                .Where(c => c.ColumnId == keyCol && c.Value == clave && c.Row!.ContainerId == cid)
                .Select(c => (Guid?)c.RowId).FirstOrDefaultAsync(ct);
            if (rowId is not Guid rid)
            {
                return Fail($"No existe la fila {cfg.ColumnaClave}={clave} en el contenedor. El paso 'Crear actividad' va DESPUES del paso que ingiere el registro.");
            }

            marca = await db.DataContainerCells.FirstOrDefaultAsync(c => c.RowId == rid && c.ColumnId == tareaCol, ct);
            var previa = marca?.Value?.Trim();
            if (!string.IsNullOrEmpty(previa))
            {
                return previa == Reserva
                    ? new Result(true, false, $"{clave}: quedo una creacion a medias ({cfg.ColumnaTarea}='{Reserva}'); revisar a mano, no se crea otra.", null)
                    : new Result(true, false, $"{clave}: ya tiene la actividad {previa}.", previa);
            }

            // Reserva ANTES de crear: si el proceso se cae a mitad, la siguiente corrida no duplica.
            if (marca is null)
            {
                marca = new DataContainerCell { TenantId = tenantId, RowId = rid, ColumnId = tareaCol.Value, Value = Reserva };
                db.DataContainerCells.Add(marca);
            }
            else { marca.Value = Reserva; }
            await db.SaveChangesAsync(ct);
        }
        else
        {
            // Sin contenedor configurado: anti-duplicado por titulo dentro del concepto (menos robusto).
            var existente = await db.TaskItems.AsNoTracking()
                .Where(t => t.SubcategoriaId == subId && t.Title == titulo && !t.IsArchived)
                .Select(t => t.Number).FirstOrDefaultAsync(ct);
            if (existente is not null) { return new Result(true, false, $"{clave}: ya existe la actividad {existente}.", existente); }
        }

        // ---- Crear la tarea ----
        var zona = await TenantZoneAsync(db, tenantId, ct);
        var tasks = sp.GetRequiredService<ITaskItemService>();
        var req = new CreateTaskItemRequest(
            Title: titulo,
            ActivityTypeId: null,
            Description: NullIfEmpty(Resolve(ctx, cfg.Descripcion)),
            AssigneeTenantUserId: cfg.ResponsableId,
            DueDate: EndOfDay(Resolve(ctx, cfg.Vencimiento), zona),
            RequesterName: NullIfEmpty(Resolve(ctx, cfg.Solicitante)),
            RequesterDocument: NullIfEmpty(Resolve(ctx, cfg.DocumentoSolicitante)),
            SubcategoriaId: subId);
        var created = await tasks.CreateAsync(req, Guid.Empty, actorName, ct);
        if (!created.IsOk || created.Value is null)
        {
            if (marca is not null) { marca.Value = string.Empty; await db.SaveChangesAsync(ct); }
            return Fail($"{clave}: no se pudo crear la actividad: {created.Error}");
        }
        var numero = created.Value.Item.Number;
        if (marca is not null) { marca.Value = numero; await db.SaveChangesAsync(ct); }

        // ---- Formulario del concepto, diligenciado ----
        var nota = string.Empty;
        if (concepto.FormDefinitionId is Guid fid)
        {
            try { nota = await FillFormAsync(sp, db, fid, numero, clave, cfg, ctx, NowIn(zona), ct); }
            catch (Exception ex) { nota = " Formulario sin llenar: " + ex.Message; }
        }
        else { nota = " El concepto no tiene formulario."; }

        return new Result(true, true, $"{clave}: actividad {numero} creada.{nota}", numero);

        static Result Fail(string msg) => new(false, false, msg, null);
    }

    private static async Task<string> FillFormAsync(IServiceProvider sp, IApplicationDbContext db, Guid formId,
        string numero, string clave, Config cfg, ScrapeRunContext ctx, DateTimeOffset ahoraLocal, CancellationToken ct)
    {
        var tipos = await db.FormQuestions.AsNoTracking()
            .Where(q => q.DefinitionId == formId)
            .Select(q => new { q.FieldCode, q.ControlType })
            .ToListAsync(ct);
        var tipoPorCodigo = tipos.GroupBy(t => t.FieldCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().ControlType, StringComparer.OrdinalIgnoreCase);

        var data = new Dictionary<string, FormFieldValue>(StringComparer.OrdinalIgnoreCase);
        var faltan = new List<string>();
        foreach (var (campo, expr) in cfg.Campos ?? new())
        {
            if (!tipoPorCodigo.TryGetValue(campo, out var tipo)) { faltan.Add(campo); continue; }
            var valor = Resolve(ctx, expr);
            if (valor.Length == 0) { continue; } // no pisar con vacio
            data[campo] = new FormFieldValue(Coerce(tipo, valor), tipo.ToString());
        }

        // Valores fijos: solo donde el registro no trajo nada (no pisan lo leido del portal).
        foreach (var (campo, literal) in cfg.CamposFijos ?? new())
        {
            if (data.ContainsKey(campo)) { continue; }
            if (!tipoPorCodigo.TryGetValue(campo, out var tipo)) { faltan.Add(campo); continue; }
            var valor = Ecorex.SuperAdmin.Components.Shared.Forms.FormSystemTokens.Resolve(literal ?? string.Empty, null, ahoraLocal).Trim();
            if (valor.Length == 0) { continue; }
            data[campo] = new FormFieldValue(Coerce(tipo, valor), tipo.ToString());
        }

        var sugeridas = 0;
        if (!string.IsNullOrWhiteSpace(cfg.GrillaCampo))
        {
            if (!tipoPorCodigo.TryGetValue(cfg.GrillaCampo!, out var tg) || tg != FormControlType.GridDetail)
            {
                faltan.Add(cfg.GrillaCampo!);
            }
            else
            {
                var filas = BuildGridRows(ctx, cfg);
                if (filas.Count > 0 && cfg.Prellenado is { } pre)
                {
                    sugeridas = await PrefillAsync(db, pre, filas, clave, ct);
                }
                if (filas.Count > 0)
                {
                    data[cfg.GrillaCampo!] = new FormFieldValue(JsonSerializer.Serialize(filas, Web), FormControlType.GridDetail.ToString());
                }
            }
        }

        if (data.Count == 0) { return " Formulario sin datos que llenar."; }

        var forms = sp.GetRequiredService<IFormResponseService>();
        var draft = await forms.GetOrCreateDraftAsync(formId, numero, ct);
        if (!draft.IsOk || draft.Value is null) { return " Formulario sin llenar: " + (draft.Error ?? "no se pudo abrir el borrador") + "."; }
        var saved = await forms.SaveAsync(draft.Value.Id, data, submit: false, cancellationToken: ct);
        if (!saved.IsOk) { return " Formulario sin llenar: " + (saved.Error ?? "error al guardar") + "."; }
        // Formulario PRINCIPAL de la tarea: la UI ya lo trata asi cuando es el unico, pero el resolvedor de tokens de
        // los avisos ({form.petof}, paso Notificar / reglas de nodo) solo lee el marcado como activo.
        await forms.SetActiveTaskFormAsync(draft.Value.Id, ct);
        return $" Formulario con {data.Count} campo(s)"
            + (sugeridas > 0 ? $"; {sugeridas} item(s) prellenado(s) con la ultima cotizacion" : "")
            + (faltan.Count > 0 ? $"; no existen en el formulario: {string.Join(", ", faltan)}" : "") + ".";
    }

    // ---------------- Prellenado desde el historico ----------------

    /// <summary>Llena las celdas VACIAS de <paramref name="filas"/> con la fila mas reciente del contenedor
    /// historico que comparte la clave. Devuelve cuantas filas recibieron al menos una sugerencia.</summary>
    private static async Task<int> PrefillAsync(IApplicationDbContext db, PrellenadoConfig pre,
        List<Dictionary<string, string?>> filas, string claveRegistro, CancellationToken ct)
    {
        if (pre.ContenedorId is not Guid cid || string.IsNullOrWhiteSpace(pre.ClaveGrilla)
            || string.IsNullOrWhiteSpace(pre.ColumnaClave) || pre.Columnas is not { Count: > 0 })
        {
            return 0;
        }

        var claves = filas.Select(f => f.GetValueOrDefault(pre.ClaveGrilla!)?.Trim())
            .Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).Distinct(StringComparer.Ordinal).ToList();
        if (claves.Count == 0) { return 0; }

        var nombres = new HashSet<string>(StringComparer.Ordinal) { pre.ColumnaClave! };
        foreach (var n in new[] { pre.OrdenColumna, pre.ExcluirColumna, pre.RequiereColumna }.Concat(pre.Columnas.Values))
        {
            if (!string.IsNullOrWhiteSpace(n)) { nombres.Add(n.Trim()); }
        }
        var cols = await db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == cid && nombres.Contains(c.Name))
            .Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var keyCol = cols.FirstOrDefault(c => c.Name == pre.ColumnaClave)?.Id;
        if (keyCol is null) { return 0; }
        var nombrePorId = cols.ToDictionary(c => c.Id, c => c.Name);
        var colIds = cols.Select(c => c.Id).ToList();

        var rowIds = await db.DataContainerCells.AsNoTracking()
            .Where(c => c.ColumnId == keyCol && c.Value != null && claves.Contains(c.Value) && c.Row!.ContainerId == cid)
            .Select(c => c.RowId).Distinct().ToListAsync(ct);
        if (rowIds.Count == 0) { return 0; }

        var celdas = await db.DataContainerCells.AsNoTracking()
            .Where(c => rowIds.Contains(c.RowId) && colIds.Contains(c.ColumnId))
            .Select(c => new { c.RowId, c.ColumnId, c.Value }).ToListAsync(ct);
        var historico = celdas.GroupBy(c => c.RowId)
            .Select(g => (IReadOnlyDictionary<string, string?>)g.ToDictionary(c => nombrePorId[c.ColumnId], c => c.Value, StringComparer.Ordinal))
            .ToList();

        return ApplyPrefill(filas, historico, pre, claveRegistro);
    }

    /// <summary>
    /// Parte pura del prellenado (testeable): por cada fila de la grilla elige, entre las filas historicas con la
    /// misma clave (sin las del mismo registro y con valor en RequiereColumna), la de mayor OrdenColumna, y copia
    /// sus columnas mapeadas SOLO a celdas vacias. Devuelve cuantas filas recibieron alguna sugerencia.
    /// </summary>
    public static int ApplyPrefill(List<Dictionary<string, string?>> filas,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> historico, PrellenadoConfig pre, string? claveRegistro)
    {
        if (string.IsNullOrWhiteSpace(pre.ClaveGrilla) || string.IsNullOrWhiteSpace(pre.ColumnaClave)
            || pre.Columnas is not { Count: > 0 })
        {
            return 0;
        }

        var porClave = historico
            .Where(h => string.IsNullOrWhiteSpace(pre.ExcluirColumna)
                || !string.Equals(h.GetValueOrDefault(pre.ExcluirColumna!)?.Trim(), claveRegistro?.Trim(), StringComparison.Ordinal))
            .Where(h => string.IsNullOrWhiteSpace(pre.RequiereColumna)
                || !string.IsNullOrWhiteSpace(h.GetValueOrDefault(pre.RequiereColumna!)))
            .GroupBy(h => h.GetValueOrDefault(pre.ColumnaClave!)?.Trim() ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.GetValueOrDefault(pre.OrdenColumna ?? string.Empty), OrdenComparer.Instance).First(),
                StringComparer.Ordinal);

        var tocadas = 0;
        foreach (var fila in filas)
        {
            var k = fila.GetValueOrDefault(pre.ClaveGrilla!)?.Trim();
            if (string.IsNullOrEmpty(k) || !porClave.TryGetValue(k, out var src)) { continue; }
            var algo = false;
            foreach (var (colGrilla, colCont) in pre.Columnas)
            {
                if (string.IsNullOrWhiteSpace(colGrilla) || string.IsNullOrWhiteSpace(colCont)) { continue; }
                if (!string.IsNullOrWhiteSpace(fila.GetValueOrDefault(colGrilla))) { continue; }
                var v = src.GetValueOrDefault(colCont.Trim());
                if (string.IsNullOrWhiteSpace(v)) { continue; }
                fila[colGrilla] = v.Trim();
                algo = true;
            }
            if (algo) { tocadas++; }
        }
        return tocadas;
    }

    /// <summary>Orden "natural" para elegir la mas reciente: numerico si ambos son numeros, si no ordinal.</summary>
    private sealed class OrdenComparer : IComparer<string?>
    {
        public static readonly OrdenComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (decimal.TryParse(x?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var a)
                && decimal.TryParse(y?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var b))
            {
                return a.CompareTo(b);
            }
            return string.CompareOrdinal(x ?? string.Empty, y ?? string.Empty);
        }
    }

    // ---------------- Fechas (zona del tenant, regla 9) ----------------

    private static async Task<TimeZoneInfo> TenantZoneAsync(IApplicationDbContext db, Guid tenantId, CancellationToken ct)
    {
        var tzId = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.TimeZoneId).FirstOrDefaultAsync(ct);
        return Ecorex.Application.Scheduling.ScheduledJobRecurrence.ResolveTimeZone(tzId);
    }

    private static DateTimeOffset NowIn(TimeZoneInfo zona) => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zona);

    /// <summary>Fecha (dd/MM/yyyy, yyyy-MM-dd...) -> 23:59 de ese dia en la zona del tenant. Ilegible -> null.</summary>
    public static DateTimeOffset? EndOfDay(string? texto, TimeZoneInfo zona)
    {
        if (string.IsNullOrWhiteSpace(texto)) { return null; }
        if (!DateTime.TryParseExact(texto.Trim(), Fechas, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) { return null; }
        var local = DateTime.SpecifyKind(d.Date.AddHours(23).AddMinutes(59), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zona.GetUtcOffset(local));
    }

    /// <summary>Filas de la grilla: por cada objeto del arreglo de items, id de columna -> propiedad.</summary>
    public static List<Dictionary<string, string?>> BuildGridRows(ScrapeRunContext ctx, Config cfg)
    {
        var filas = new List<Dictionary<string, string?>>();
        if (string.IsNullOrWhiteSpace(cfg.ItemsRuta) || cfg.GrillaColumnas is not { Count: > 0 } cols) { return filas; }
        if (!ctx.TryResolve(cfg.ItemsRuta!.Trim(), out var raw) || string.IsNullOrWhiteSpace(raw)) { return filas; }
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return filas; }
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) { continue; }
                var fila = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var (colId, prop) in cols)
                {
                    if (string.IsNullOrWhiteSpace(colId) || string.IsNullOrWhiteSpace(prop)) { continue; }
                    fila[colId] = item.TryGetProperty(prop.Trim(), out var v) ? AsText(v) : null;
                }
                filas.Add(fila);
            }
        }
        catch (JsonException) { /* sin items legibles */ }
        return filas;
    }

    private static string? AsText(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => v.GetRawText(),
    };

    private static readonly string[] Fechas = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-ddTHH:mm:ss"];

    /// <summary>Normaliza el texto al formato interno del control (Date yyyy-MM-dd, Number invariante, Toggle true/false).</summary>
    public static string Coerce(FormControlType tipo, string valor)
    {
        switch (tipo)
        {
            case FormControlType.Date:
                return DateTime.TryParseExact(valor.Trim(), Fechas, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : valor;
            case FormControlType.Number:
                {
                    var t = valor.Trim().Replace("$", "").Replace(" ", "");
                    if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && !(t.Contains(',') && t.Contains('.')))
                    {
                        return n.ToString(CultureInfo.InvariantCulture);
                    }
                    return decimal.TryParse(t, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out var n2)
                        ? n2.ToString(CultureInfo.InvariantCulture) : valor;
                }
            case FormControlType.Toggle:
                {
                    var t = valor.Trim().ToLowerInvariant();
                    return t is "1" or "true" or "si" or "x" or "yes" ? "true" : "false";
                }
            default:
                return valor;
        }
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
