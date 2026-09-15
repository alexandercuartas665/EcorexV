using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Scheduling;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Implementacion de <see cref="IDirectorioModularFichaService"/> (Capa 8, 2do motor de contactos).
/// Lee las secciones/campos "mod_" por composicion de la categoria y crea el tercero con
/// DirectoryEngine=Modular. Aislamiento por el filtro global; TenantId estampado al crear.
/// </summary>
public sealed class DirectorioModularFichaService : IDirectorioModularFichaService
{
    private readonly IApplicationDbContext _app;
    private readonly IDirectorioModularDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISequenceService _sequences;
    private readonly TimeProvider _clock;

    // Consecutivo por tenant del Directorio (regla 2.1, "Datos Automaticos de Sistema"): TER-000001,
    // TER-000002, ... Emitido por ISequenceService (CAS atomico, ADR-0013), sin choque cross-tenant.
    public const string SequenceCode = "TER";
    public const string SequencePrefix = "TER-";
    public const int SequencePadding = 6;

    // Campos de sistema de la seccion publica, estampados al crear e inmutables en edicion (regla 2.1).
    private static readonly string[] SistemaCampos = { "codigo", "fecha_creacion", "usuario_creador" };

    public DirectorioModularFichaService(IApplicationDbContext app, IDirectorioModularDbContext db,
        ITenantContext tenant, ISequenceService sequences, TimeProvider clock)
    {
        _app = app;
        _db = db;
        _tenant = tenant;
        _sequences = sequences;
        _clock = clock;
    }

    public async Task<ModularFichaDto?> GetFichaAsync(string categoriaKey, CancellationToken cancellationToken = default)
    {
        var key = (categoriaKey ?? string.Empty).Trim();
        var cat = await _db.DirectorioCategorias.AsNoTracking().FirstOrDefaultAsync(c => c.CategoriaKey == key, cancellationToken);
        if (cat is null) { return null; }

        // Secciones que arma la categoria, en orden de composicion.
        var comp = await _db.DirectorioCategoriaSecciones.AsNoTracking()
            .Where(s => s.CategoriaKey == key)
            .OrderBy(s => s.Orden)
            .Select(s => s.FichaKey)
            .ToListAsync(cancellationToken);
        if (comp.Count == 0) { return new ModularFichaDto(cat.CategoriaKey, cat.Title, Array.Empty<ModularSeccionDto>()); }

        var secciones = await _app.TerceroFichaDefinitions.AsNoTracking()
            .Where(f => comp.Contains(f.FichaKey))
            .ToListAsync(cancellationToken);
        var campos = await _app.TerceroFieldDefinitions.AsNoTracking()
            .Where(f => comp.Contains(f.FichaKey))
            .OrderBy(f => f.SortOrder)
            .ToListAsync(cancellationToken);

        var result = new List<ModularSeccionDto>(comp.Count);
        foreach (var fk in comp) // respeta el orden de composicion
        {
            var sec = secciones.FirstOrDefault(s => s.FichaKey == fk);
            if (sec is null) { continue; }
            var flds = campos.Where(c => c.FichaKey == fk)
                .Select(c => new ModularCampoDto(c.FieldKey, c.Label, c.FieldType, c.Column, c.Options, c.RequeridoEn, c.ReadOnly, c.Description))
                .ToList();
            result.Add(new ModularSeccionDto(sec.FichaKey, sec.Title, sec.Icono, sec.Color, sec.Description, sec.AplicaA, flds));
        }

        return new ModularFichaDto(cat.CategoriaKey, cat.Title, result);
    }

    /// <summary>Catalogo de areas del motor (v1 fijo, como el prototipo). En una ola posterior saldra de
    /// las Dependencias/Cargos del organigrama del tenant.</summary>
    private static readonly ModularAreaDto[] Areas =
    {
        new("admin", "Administracion", "fa-shield-halved"),
        new("comercial", "Comercial", "fa-handshake"),
        new("contabilidad", "Contabilidad", "fa-calculator"),
        new("logistica", "Logistica", "fa-boxes-stacked"),
    };

    private static string AnchoDe(int column) => column switch { <= 1 => "pequena", 2 => "media", _ => "completa" };

    public async Task<ModularEstructuraDto> GetEstructuraAsync(CancellationToken cancellationToken = default)
    {
        var prefix = DirectorioModularDefaults.SeccionPrefix;

        var secciones = await _app.TerceroFichaDefinitions.AsNoTracking()
            .Where(f => f.FichaKey.StartsWith(prefix))
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Title)
            .ToListAsync(cancellationToken);
        var campos = await _app.TerceroFieldDefinitions.AsNoTracking()
            .Where(f => f.FichaKey.StartsWith(prefix))
            .OrderBy(f => f.SortOrder)
            .ToListAsync(cancellationToken);

        // Que categoria usa que seccion (para el bloque "Usada por las categorias").
        var comp = await _db.DirectorioCategoriaSecciones.AsNoTracking()
            .Select(s => new { s.CategoriaKey, s.FichaKey })
            .ToListAsync(cancellationToken);
        var cats = await _db.DirectorioCategorias.AsNoTracking()
            .Select(c => new { c.CategoriaKey, c.Title, c.Color })
            .ToListAsync(cancellationToken);
        var catByKey = cats.ToDictionary(c => c.CategoriaKey, c => c);

        var list = new List<ModularSeccionConfigDto>(secciones.Count);
        foreach (var sec in secciones)
        {
            var flds = campos.Where(c => c.FichaKey == sec.FichaKey)
                .Select(c => new ModularCampoConfigDto(
                    c.Id, c.FieldKey, c.Label, c.FieldType, AnchoDe(c.Column),
                    c.Options, c.RequeridoEn, c.ReadOnly, c.IsSystem, c.Description, c.SortOrder,
                    c.NotasDesarrollador))
                .ToList();
            var usada = comp.Where(x => x.FichaKey == sec.FichaKey)
                .Select(x => catByKey.TryGetValue(x.CategoriaKey, out var c)
                    ? new ModularUsoCategoriaDto(c.CategoriaKey, c.Title, c.Color) : null)
                .Where(x => x is not null).Select(x => x!).ToList();
            list.Add(new ModularSeccionConfigDto(
                sec.Id, sec.FichaKey, sec.Title, sec.Icono, sec.Color, sec.Description,
                sec.AplicaA, sec.Areas, sec.Protegida, sec.SortOrder, flds, usada));
        }

        return new ModularEstructuraDto(list, Areas);
    }

    public async Task<(Guid? Id, string? Error)> CreateTerceroAsync(CreateModularTerceroRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return (null, "No hay tenant activo."); }
        var key = (request.CategoriaKey ?? string.Empty).Trim();
        if (!await _db.DirectorioCategorias.AnyAsync(c => c.CategoriaKey == key, cancellationToken))
        {
            return (null, "La categoria no existe.");
        }

        var valores = request.Valores ?? new();

        // Deteccion automatica de naturaleza (regla 2.1). Si se llenaron AMBOS bloques (nombre de empresa
        // + contacto) se crean DOS terceros vinculados: la Organizacion (principal) y la Persona (contacto
        // con EmpresaId apuntando a la organizacion) -> O1-3. Se valida el nombre ANTES de consumir
        // consecutivos (para no quemar numeros por un alta sin nombre).
        var nombreEmpresa = FindValue(valores, "nombre_empresa")
            ?? FindValue(valores, "razon_social") ?? FindValue(valores, "nombre_comercial");
        var contacto = FindValue(valores, "contacto");
        if (string.IsNullOrWhiteSpace(nombreEmpresa) && string.IsNullOrWhiteSpace(contacto))
        {
            return (null, "Falta al menos un nombre (empresa o contacto).");
        }

        // Datos de sistema comunes (regla 2.1): fecha (zona del tenant) + usuario iguales para la
        // operacion; el consecutivo TER-xxxxxx se emite por cada tercero creado.
        await _sequences.EnsureSequenceAsync(SequenceCode, cancellationToken);
        var fechaLocal = await ResolveFechaLocalAsync(cancellationToken);
        var usuario = await ResolveUsuarioAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(nombreEmpresa) && !string.IsNullOrWhiteSpace(contacto))
        {
            // Organizacion: ficha completa SIN los campos exclusivos de la persona (contacto/cargo/telefono).
            var valoresOrg = CloneValores(valores);
            QuitarCamposPersona(valoresOrg);
            var org = await NuevoTerceroAsync(tenantId, key, valoresOrg, TerceroTipo.Empresa, empresa: null, fechaLocal, usuario, cancellationToken);

            // Persona vinculada: ficha minima de contacto, EmpresaId = organizacion (se enlaza por navegacion).
            var valoresPer = SoloCamposPersona(valores);
            await NuevoTerceroAsync(tenantId, key, valoresPer, TerceroTipo.Persona, empresa: org, fechaLocal, usuario, cancellationToken);

            await _app.SaveChangesAsync(cancellationToken);   // organizacion + persona en una sola transaccion
            return (org.Id, null);
        }

        // Un solo tercero (naturaleza deducida por el bloque que se lleno).
        var tipo = !string.IsNullOrWhiteSpace(nombreEmpresa) ? TerceroTipo.Empresa : TerceroTipo.Persona;
        var solo = await NuevoTerceroAsync(tenantId, key, valores, tipo, empresa: null, fechaLocal, usuario, cancellationToken);
        await _app.SaveChangesAsync(cancellationToken);
        return (solo.Id, null);
    }

    /// <summary>Crea un Tercero Modular en memoria (sin guardar): fija naturaleza/nombre, estampa datos de
    /// sistema (consecutivo + fecha + usuario), lo enlaza a una organizacion (contacto) y a su categoria,
    /// y lo agrega al contexto. El caller hace el SaveChanges (para agrupar org + persona en una transaccion).</summary>
    private async Task<Tercero> NuevoTerceroAsync(Guid tenantId, string categoriaKey,
        Dictionary<string, Dictionary<string, string>> valores, TerceroTipo tipo, Tercero? empresa,
        DateTimeOffset fechaLocal, string usuario, CancellationToken cancellationToken)
    {
        var t = new Tercero
        {
            TenantId = tenantId,
            Estado = TerceroEstado.Activo,
            DirectoryEngine = DirectoryEngine.Modular
        };
        if (empresa is not null) { t.Empresa = empresa; }   // vinculo Persona -> Organizacion (O1-3)
        ApplyValores(t, valores, lockTipo: tipo);
        var codigo = await _sequences.NextAsync(SequenceCode, SequencePrefix, SequencePadding, cancellationToken);
        StampSistema(valores, codigo, fechaLocal, usuario);
        t.FichasJson = JsonSerializer.Serialize(valores);
        t.Categorias.Add(new TerceroCategoria { TenantId = tenantId, CategoriaKey = categoriaKey });
        _app.Terceros.Add(t);
        return t;
    }

    public async Task<ModularEditDto?> GetTerceroParaEditarAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var t = await _app.Terceros.AsNoTracking()
            .Include(x => x.Categorias)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (t is null || t.DirectoryEngine != DirectoryEngine.Modular) { return null; }

        Dictionary<string, Dictionary<string, string>> valores;
        try
        {
            valores = string.IsNullOrWhiteSpace(t.FichasJson)
                ? new()
                : JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(t.FichasJson) ?? new();
        }
        catch { valores = new(); }

        // Migrados desde el Clasico no traen la seccion mod_publica: la sintetizamos de las columnas base
        // para que el modal muestre los datos (nombre/ide/correo/telefono/ciudad/cargo).
        var pubKey = DirectorioModularDefaults.SeccionKey("publica");
        if (!valores.TryGetValue(pubKey, out var pub) || pub.Count == 0)
        {
            pub = new Dictionary<string, string>(StringComparer.Ordinal);
            if (t.Tipo == TerceroTipo.Empresa) { pub["nombre_empresa"] = t.Nombre ?? ""; }
            else { pub["contacto"] = t.Nombre ?? ""; }
            if (!string.IsNullOrWhiteSpace(t.IdValor)) { pub["ide"] = t.IdValor!; }
            if (!string.IsNullOrWhiteSpace(t.Email)) { pub["correo"] = t.Email!; }
            if (!string.IsNullOrWhiteSpace(t.Ciudad)) { pub["ciudad"] = t.Ciudad!; }
            if (!string.IsNullOrWhiteSpace(t.Cargo)) { pub["cargo"] = t.Cargo!; }
            if (!string.IsNullOrWhiteSpace(t.Telefono))
            {
                pub[t.Tipo == TerceroTipo.Persona ? "telefono_contacto" : "telefono_empresa"] = t.Telefono!;
            }
            valores[pubKey] = pub;
        }

        var catKey = t.Categorias.FirstOrDefault()?.CategoriaKey;
        var estado = t.Estado == TerceroEstado.Inactivo ? "Inactivo" : "Activo";
        return new ModularEditDto(t.Id, catKey, estado, t.Tipo, valores);
    }

    public async Task<string?> UpdateTerceroAsync(Guid id, CreateModularTerceroRequest request, string estado, CancellationToken cancellationToken = default)
    {
        var t = await _app.Terceros.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (t is null) { return "El tercero no existe."; }
        if (t.DirectoryEngine != DirectoryEngine.Modular) { return "Este tercero no pertenece al motor Modular."; }

        var valores = request.Valores ?? new();
        // Datos de sistema (codigo/fecha/usuario) inmutables (regla 2.1): se copian del registro y NO
        // se confia en lo que envie el cliente (los controles son de solo lectura, pero se blinda aqui).
        CarryOverSistema(valores, t.FichasJson);

        // Inmutabilidad de la naturaleza (O1-2): en edicion no se puede cambiar Empresa <-> Persona.
        var err = ApplyValores(t, valores, lockTipo: t.Tipo);
        if (err is not null) { return err; }
        t.Estado = string.Equals(estado, "Inactivo", StringComparison.OrdinalIgnoreCase)
            ? TerceroEstado.Inactivo : TerceroEstado.Activo;

        await _app.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<byte[]> ExportXlsxAsync(CancellationToken cancellationToken = default)
    {
        var ts = await _app.Terceros.AsNoTracking()
            .Where(t => t.DirectoryEngine == DirectoryEngine.Modular && t.Estado != TerceroEstado.Inactivo)
            .OrderBy(t => t.Nombre)
            .Select(t => new
            {
                t.Nombre, t.Tipo, t.Perfiles, t.Estado, t.IdTipo, t.IdValor,
                t.Ciudad, t.Sector, t.Cargo, t.Email, t.Telefono, t.Vendedor, t.VendedorAsesorId
            })
            .ToListAsync(cancellationToken);

        // Nombre del asesor (para que la columna Vendedor sea re-importable por nombre).
        var ids = ts.Where(x => x.VendedorAsesorId is not null).Select(x => x.VendedorAsesorId!.Value).Distinct().ToList();
        var asesores = ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await _app.Asesores.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Nombre, cancellationToken);

        var rows = ts.Select(t => new TerceroExportXlsx.Row(
            t.Nombre,
            t.Tipo.ToString(),
            t.Perfiles == TerceroPerfil.Ninguno ? string.Empty : t.Perfiles.ToString(),
            t.Estado.ToString(),
            t.IdTipo == TerceroIdTipo.Ninguno ? string.Empty : t.IdTipo.ToString(),
            t.IdValor, t.Ciudad,
            t.Tipo == TerceroTipo.Empresa ? t.Sector : null,
            t.Tipo == TerceroTipo.Persona ? t.Cargo : null,
            t.Email, t.Telefono,
            t.VendedorAsesorId is Guid g && asesores.TryGetValue(g, out var n) ? n : t.Vendedor));

        return TerceroExportXlsx.Build(rows);
    }

    public async Task<(int Done, int Failed)> ImportAsync(string categoriaKey, IReadOnlyList<TerceroImportXlsx.TerceroImportRow> rows, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return (0, rows.Count(r => r.IsValid)); }
        var key = (categoriaKey ?? string.Empty).Trim();
        if (!await _db.DirectorioCategorias.AnyAsync(c => c.CategoriaKey == key, cancellationToken))
        {
            return (0, rows.Count(r => r.IsValid));
        }
        var pubKey = DirectorioModularDefaults.SeccionKey("publica");

        // Datos de sistema (regla 2.1) tambien para los importados: fecha/usuario iguales para el lote;
        // el consecutivo se emite por fila (CAS atomico, sin choque cross-tenant).
        await _sequences.EnsureSequenceAsync(SequenceCode, cancellationToken);
        var fechaLocal = await ResolveFechaLocalAsync(cancellationToken);
        var usuario = await ResolveUsuarioAsync(cancellationToken);

        int done = 0, failed = 0;
        foreach (var row in rows.Where(r => r.IsValid))
        {
            try
            {
                var esEmpresa = row.Tipo == TerceroTipo.Empresa;
                var t = new Tercero
                {
                    TenantId = tenantId,
                    DirectoryEngine = DirectoryEngine.Modular,
                    Nombre = row.Nombre,
                    Tipo = row.Tipo,
                    Perfiles = row.Perfiles,
                    Estado = row.Estado,
                    IdTipo = row.IdTipo,
                    IdValor = row.NumeroId,
                    Ciudad = row.Ciudad,
                    Sector = esEmpresa ? row.Sector : null,
                    Cargo = esEmpresa ? null : row.Cargo,
                    Email = row.Email,
                    Telefono = row.Telefono,
                    Vendedor = row.VendedorAsesorId is null ? row.Vendedor : null,
                    VendedorAsesorId = row.VendedorAsesorId
                };

                // Seccion publica mapeada, para que la ficha muestre los datos al editar.
                var pub = new Dictionary<string, string>(StringComparer.Ordinal);
                if (esEmpresa) { pub["nombre_empresa"] = row.Nombre; } else { pub["contacto"] = row.Nombre; }
                if (!string.IsNullOrWhiteSpace(row.NumeroId)) { pub["ide"] = row.NumeroId!; }
                if (!string.IsNullOrWhiteSpace(row.Email)) { pub["correo"] = row.Email!; }
                if (!string.IsNullOrWhiteSpace(row.Ciudad)) { pub["ciudad"] = row.Ciudad!; }
                if (!esEmpresa && !string.IsNullOrWhiteSpace(row.Cargo)) { pub["cargo"] = row.Cargo!; }
                if (!string.IsNullOrWhiteSpace(row.Telefono))
                {
                    pub[esEmpresa ? "telefono_empresa" : "telefono_contacto"] = row.Telefono!;
                }
                pub["codigo"] = await _sequences.NextAsync(SequenceCode, SequencePrefix, SequencePadding, cancellationToken);
                pub["fecha_creacion"] = fechaLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                pub["usuario_creador"] = usuario;
                t.FichasJson = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal) { [pubKey] = pub });

                t.Categorias.Add(new TerceroCategoria { TenantId = tenantId, CategoriaKey = key });
                _app.Terceros.Add(t);
                await _app.SaveChangesAsync(cancellationToken);
                done++;
            }
            catch { failed++; }
        }
        return (done, failed);
    }

    public async Task<int> CountClasicoAsync(CancellationToken cancellationToken = default)
        => await _app.Terceros.CountAsync(
            t => t.DirectoryEngine == DirectoryEngine.Clasico && t.EmpresaId == null
                && t.Estado != TerceroEstado.Inactivo, cancellationToken);

    public async Task<int> MigrateAllFromClasicoAsync(CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return 0; }
        const string baseCat = "publico";

        // Solo migra si existe la categoria base (el seed ya corrio). Evita dejar terceros sin pestana.
        if (!await _db.DirectorioCategorias.AnyAsync(c => c.CategoriaKey == baseCat, cancellationToken))
        {
            return 0;
        }

        // 1) Estampa el motor en BLOQUE (ExecuteUpdate): evita el token de concurrencia Version (ADR-0013)
        //    y respeta el filtro global de tenant. Los campos base (nombre/ide/correo/...) ya viven en las
        //    columnas del Tercero, asi que el listado Modular los muestra sin tocar FichasJson.
        var migrados = await _app.Terceros
            .Where(t => t.DirectoryEngine == DirectoryEngine.Clasico)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.DirectoryEngine, DirectoryEngine.Modular), cancellationToken);
        if (migrados == 0) { return 0; }

        // 2) Asigna a la categoria base los terceros de nivel raiz que aun no pertenezcan (solo inserts,
        //    sin token de concurrencia). Idempotente.
        var yaMiembros = (await _db.TerceroCategorias
            .Where(tc => tc.CategoriaKey == baseCat)
            .Select(tc => tc.TerceroId)
            .ToListAsync(cancellationToken)).ToHashSet();
        var raiz = await _app.Terceros
            .Where(t => t.DirectoryEngine == DirectoryEngine.Modular && t.EmpresaId == null)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in raiz)
        {
            if (yaMiembros.Contains(id)) { continue; }
            _db.TerceroCategorias.Add(new TerceroCategoria { TenantId = tenantId, TerceroId = id, CategoriaKey = baseCat });
        }
        await _app.SaveChangesAsync(cancellationToken);
        return migrados;
    }

    /// <summary>Aplica los valores de la ficha a un Tercero (nuevo o existente): fija la naturaleza y el
    /// nombre, copia los campos base y serializa FichasJson. Devuelve un mensaje de error o null si OK.
    /// <paramref name="lockTipo"/> null = alta (la naturaleza se DEDUCE de lo que se lleno); con valor =
    /// edicion (la naturaleza es INMUTABLE, O1-2, y el nombre sale del campo de esa naturaleza).</summary>
    private static string? ApplyValores(Tercero t, Dictionary<string, Dictionary<string, string>> valores, TerceroTipo? lockTipo)
    {
        // Naturaleza deducida (v1): nombre de empresa -> Organizacion; contacto -> Persona.
        // Fiscal (sin seccion publica): cae a razon social / nombre comercial del RUT.
        var nombreEmpresa = FindValue(valores, "nombre_empresa")
            ?? FindValue(valores, "razon_social") ?? FindValue(valores, "nombre_comercial");
        var contacto = FindValue(valores, "contacto");

        TerceroTipo tipo;
        if (lockTipo is TerceroTipo fijo)
        {
            // Edicion: la naturaleza no cambia. El nombre sale del campo propio de esa naturaleza,
            // con respaldo en el otro por si la seccion compuesta no trae el campo esperado.
            tipo = fijo;
            var nombre = fijo == TerceroTipo.Empresa ? (nombreEmpresa ?? contacto) : (contacto ?? nombreEmpresa);
            if (string.IsNullOrWhiteSpace(nombre)) { return "Falta el nombre del registro."; }
            t.Nombre = nombre.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(nombreEmpresa)) { t.Nombre = nombreEmpresa.Trim(); tipo = TerceroTipo.Empresa; }
        else if (!string.IsNullOrWhiteSpace(contacto)) { t.Nombre = contacto.Trim(); tipo = TerceroTipo.Persona; }
        else { return "Falta al menos un nombre (empresa o contacto)."; }

        t.Tipo = tipo;
        t.Ciudad = FindValue(valores, "ciudad");
        t.IdValor = FindValue(valores, "ide") ?? FindValue(valores, "nit") ?? FindValue(valores, "numero_identificacion");
        t.Email = FindValue(valores, "correo");
        t.Telefono = tipo == TerceroTipo.Persona ? FindValue(valores, "telefono_contacto") : FindValue(valores, "telefono_empresa");
        t.Cargo = FindValue(valores, "cargo");
        t.FichasJson = JsonSerializer.Serialize(valores);
        return null;
    }

    /// <summary>Estampa los datos de sistema (codigo/fecha/usuario) en la seccion publica de la ficha.</summary>
    private static void StampSistema(Dictionary<string, Dictionary<string, string>> valores, string codigo, DateTimeOffset fecha, string usuario)
    {
        var pubKey = DirectorioModularDefaults.SeccionKey("publica");
        if (!valores.TryGetValue(pubKey, out var pub))
        {
            pub = valores[pubKey] = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        pub["codigo"] = codigo;
        pub["fecha_creacion"] = fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        pub["usuario_creador"] = usuario;
    }

    /// <summary>Copia los datos de sistema del JSON almacenado a <paramref name="destino"/> (para blindar la
    /// inmutabilidad en edicion). Si un campo nunca se estampo, lo quita del destino (el cliente no lo inventa).</summary>
    private static void CarryOverSistema(Dictionary<string, Dictionary<string, string>> destino, string? storedJson)
    {
        var stored = ParseFichas(storedJson);
        var pubKey = DirectorioModularDefaults.SeccionKey("publica");
        stored.TryGetValue(pubKey, out var pubStored);
        if (!destino.TryGetValue(pubKey, out var dest))
        {
            dest = destino[pubKey] = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        foreach (var campo in SistemaCampos)
        {
            if (pubStored is not null && pubStored.TryGetValue(campo, out var v)) { dest[campo] = v; }
            else { dest.Remove(campo); }
        }
    }

    /// <summary>Fecha "ahora" en la zona horaria del tenant (regla 2.1: zona del tenant + UTC).</summary>
    private async Task<DateTimeOffset> ResolveFechaLocalAsync(CancellationToken cancellationToken)
    {
        var nowUtc = _clock.GetUtcNow();
        string? tzId = null;
        if (_tenant.TenantId is Guid tenantId)
        {
            tzId = await _app.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId).Select(t => t.TimeZoneId)
                .FirstOrDefaultAsync(cancellationToken);
        }
        return TimeZoneInfo.ConvertTime(nowUtc, ScheduledJobRecurrence.ResolveTimeZone(tzId));
    }

    /// <summary>Nombre visible del usuario actual (DisplayName o Email) para el campo usuario_creador.</summary>
    private async Task<string> ResolveUsuarioAsync(CancellationToken cancellationToken)
    {
        if (_tenant.UserId is not Guid uid) { return string.Empty; }
        var name = await _app.PlatformUsers.AsNoTracking()
            .Where(u => u.Id == uid).Select(u => u.DisplayName ?? u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        return name ?? string.Empty;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseFichas(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new(); }
        try { return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json) ?? new(); }
        catch { return new(); }
    }

    // ---- Creacion simultanea Organizacion + Persona (O1-3): reparto de los valores de la ficha ----

    /// <summary>Campos exclusivos de la persona en la seccion publica (el resto es de la organizacion).</summary>
    private static readonly string[] CamposPersona = { "contacto", "telefono_contacto", "cargo" };

    private static Dictionary<string, Dictionary<string, string>> CloneValores(Dictionary<string, Dictionary<string, string>> src)
        => src.ToDictionary(k => k.Key, v => new Dictionary<string, string>(v.Value, StringComparer.Ordinal), StringComparer.Ordinal);

    /// <summary>Quita de la ficha de la ORGANIZACION los campos exclusivos de la persona.</summary>
    private static void QuitarCamposPersona(Dictionary<string, Dictionary<string, string>> valores)
    {
        var pubKey = DirectorioModularDefaults.SeccionKey("publica");
        if (valores.TryGetValue(pubKey, out var pub))
        {
            foreach (var campo in CamposPersona) { pub.Remove(campo); }
        }
    }

    /// <summary>Arma la ficha MINIMA de la PERSONA de contacto (solo sus campos, en la seccion publica).</summary>
    private static Dictionary<string, Dictionary<string, string>> SoloCamposPersona(Dictionary<string, Dictionary<string, string>> valores)
    {
        var pubKey = DirectorioModularDefaults.SeccionKey("publica");
        var pub = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var campo in CamposPersona)
        {
            var v = FindValue(valores, campo);
            if (!string.IsNullOrWhiteSpace(v)) { pub[campo] = v!; }
        }
        return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal) { [pubKey] = pub };
    }

    /// <summary>Busca el valor de un campo por su clave en cualquier seccion de la ficha.</summary>
    private static string? FindValue(Dictionary<string, Dictionary<string, string>> valores, string fieldKey)
    {
        foreach (var sec in valores.Values)
        {
            if (sec.TryGetValue(fieldKey, out var v) && !string.IsNullOrWhiteSpace(v)) { return v; }
        }
        return null;
    }
}
