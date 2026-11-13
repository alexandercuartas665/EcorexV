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
    }

    public sealed record Result(bool Ok, bool Created, string Detail, string? TaskNumber);

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
        var tasks = sp.GetRequiredService<ITaskItemService>();
        var req = new CreateTaskItemRequest(
            Title: titulo,
            ActivityTypeId: null,
            Description: NullIfEmpty(Resolve(ctx, cfg.Descripcion)),
            AssigneeTenantUserId: cfg.ResponsableId,
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
            try { nota = await FillFormAsync(sp, db, fid, numero, cfg, ctx, ct); }
            catch (Exception ex) { nota = " Formulario sin llenar: " + ex.Message; }
        }
        else { nota = " El concepto no tiene formulario."; }

        return new Result(true, true, $"{clave}: actividad {numero} creada.{nota}", numero);

        static Result Fail(string msg) => new(false, false, msg, null);
    }

    private static async Task<string> FillFormAsync(IServiceProvider sp, IApplicationDbContext db, Guid formId,
        string numero, Config cfg, ScrapeRunContext ctx, CancellationToken ct)
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

        if (!string.IsNullOrWhiteSpace(cfg.GrillaCampo))
        {
            if (!tipoPorCodigo.TryGetValue(cfg.GrillaCampo!, out var tg) || tg != FormControlType.GridDetail)
            {
                faltan.Add(cfg.GrillaCampo!);
            }
            else
            {
                var filas = BuildGridRows(ctx, cfg);
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
        return $" Formulario con {data.Count} campo(s)"
            + (faltan.Count > 0 ? $"; no existen en el formulario: {string.Join(", ", faltan)}" : "") + ".";
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
