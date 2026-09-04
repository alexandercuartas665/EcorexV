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

    public async Task<(Guid? Id, string? Error)> CreateTerceroAsync(CreateModularTerceroRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return (null, "No hay tenant activo."); }
        var key = (request.CategoriaKey ?? string.Empty).Trim();
        if (!await _db.DirectorioCategorias.AnyAsync(c => c.CategoriaKey == key, cancellationToken))
        {
            return (null, "La categoria no existe.");
        }

        var valores = request.Valores ?? new();

        // Naturaleza deducida (v1): nombre de empresa -> Organizacion; contacto -> Persona.
        var nombreEmpresa = FindValue(valores, "nombre_empresa");
        var contacto = FindValue(valores, "contacto");
        // Fiscal (sin seccion publica): cae a razon social / nombre comercial del RUT.
        nombreEmpresa ??= FindValue(valores, "razon_social") ?? FindValue(valores, "nombre_comercial");

        string nombre;
        TerceroTipo tipo;
        if (!string.IsNullOrWhiteSpace(nombreEmpresa))
        {
            nombre = nombreEmpresa.Trim();
            tipo = TerceroTipo.Empresa;
        }
        else if (!string.IsNullOrWhiteSpace(contacto))
        {
            nombre = contacto.Trim();
            tipo = TerceroTipo.Persona;
        }
        else
        {
            return (null, "Falta al menos un nombre (empresa o contacto).");
        }

        var tercero = new Tercero
        {
            TenantId = tenantId,
            Nombre = nombre,
            Tipo = tipo,
            Estado = TerceroEstado.Activo,
            DirectoryEngine = DirectoryEngine.Modular,
            Ciudad = FindValue(valores, "ciudad"),
            IdValor = FindValue(valores, "ide") ?? FindValue(valores, "nit") ?? FindValue(valores, "numero_identificacion"),
            Email = FindValue(valores, "correo"),
            Telefono = tipo == TerceroTipo.Persona ? FindValue(valores, "telefono_contacto") : FindValue(valores, "telefono_empresa"),
            Cargo = FindValue(valores, "cargo"),
            FichasJson = JsonSerializer.Serialize(valores)
        };
        // Multi-membership: nace en la categoria desde la que se creo. Se enlaza por la navegacion para
        // que EF fije la FK al guardar (sin depender del momento en que se genera el Id).
        tercero.Categorias.Add(new TerceroCategoria { TenantId = tenantId, CategoriaKey = key });
        _app.Terceros.Add(tercero);

        await _app.SaveChangesAsync(cancellationToken);
        return (tercero.Id, null);
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
