using System.Text.Json;
using Ecorex.Application.Common;
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

    public DirectorioModularFichaService(IApplicationDbContext app, IDirectorioModularDbContext db, ITenantContext tenant)
    {
        _app = app;
        _db = db;
        _tenant = tenant;
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
                    c.Options, c.RequeridoEn, c.ReadOnly, c.IsSystem, c.Description, c.SortOrder))
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

        var tercero = new Tercero
        {
            TenantId = tenantId,
            Estado = TerceroEstado.Activo,
            DirectoryEngine = DirectoryEngine.Modular
        };
        var err = ApplyValores(tercero, valores);
        if (err is not null) { return (null, err); }

        // Multi-membership: nace en la categoria desde la que se creo. Se enlaza por la navegacion para
        // que EF fije la FK al guardar (sin depender del momento en que se genera el Id).
        tercero.Categorias.Add(new TerceroCategoria { TenantId = tenantId, CategoriaKey = key });
        _app.Terceros.Add(tercero);

        await _app.SaveChangesAsync(cancellationToken);
        return (tercero.Id, null);
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
        return new ModularEditDto(t.Id, catKey, estado, valores);
    }

    public async Task<string?> UpdateTerceroAsync(Guid id, CreateModularTerceroRequest request, string estado, CancellationToken cancellationToken = default)
    {
        var t = await _app.Terceros.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (t is null) { return "El tercero no existe."; }
        if (t.DirectoryEngine != DirectoryEngine.Modular) { return "Este tercero no pertenece al motor Modular."; }

        var err = ApplyValores(t, request.Valores ?? new());
        if (err is not null) { return err; }
        t.Estado = string.Equals(estado, "Inactivo", StringComparison.OrdinalIgnoreCase)
            ? TerceroEstado.Inactivo : TerceroEstado.Activo;

        await _app.SaveChangesAsync(cancellationToken);
        return null;
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

    /// <summary>Aplica los valores de la ficha a un Tercero (nuevo o existente): deduce la naturaleza y el
    /// nombre, copia los campos base y serializa FichasJson. Devuelve un mensaje de error o null si OK.</summary>
    private static string? ApplyValores(Tercero t, Dictionary<string, Dictionary<string, string>> valores)
    {
        // Naturaleza deducida (v1): nombre de empresa -> Organizacion; contacto -> Persona.
        // Fiscal (sin seccion publica): cae a razon social / nombre comercial del RUT.
        var nombreEmpresa = FindValue(valores, "nombre_empresa")
            ?? FindValue(valores, "razon_social") ?? FindValue(valores, "nombre_comercial");
        var contacto = FindValue(valores, "contacto");

        TerceroTipo tipo;
        if (!string.IsNullOrWhiteSpace(nombreEmpresa)) { t.Nombre = nombreEmpresa.Trim(); tipo = TerceroTipo.Empresa; }
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
