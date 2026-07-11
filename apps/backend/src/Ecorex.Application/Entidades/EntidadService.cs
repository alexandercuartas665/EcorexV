using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Entidades;

/// <summary>
/// Servicio de la Configuracion de la entidad. Tenant-scoped por el filtro global. Nunca borra
/// entidades (archiva). Los valores de campos dinamicos se serializan en Entidad.FieldValuesJson.
/// </summary>
public sealed class EntidadService : IEntidadService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public EntidadService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<EntidadDto>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var q = _db.Entidades.AsNoTracking().AsQueryable();
        if (!includeArchived) { q = q.Where(e => !e.IsArchived); }
        var list = await q
            .OrderByDescending(e => e.IsPrincipal).ThenBy(e => e.SortOrder).ThenBy(e => e.Codigo)
            .ToListAsync(ct);
        return list.Select(e => new EntidadDto(
            e.Id, e.Codigo, e.Nombre, e.NombreComercial, e.Ciudad, e.TipoEntidad, e.Kind,
            e.IsPrincipal, e.IsActive, e.IsArchived, !string.IsNullOrEmpty(e.LogoBase64),
            e.UpdatedAt ?? e.CreatedAt)).ToList();
    }

    public async Task<EntidadDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _db.Entidades.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return e is null ? null : MapDetail(e);
    }

    public async Task<EntidadDetailDto?> SaveAsync(SaveEntidadRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var nombre = (req.Nombre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(nombre)) { return null; }

        Entidad entity;
        if (req.Id is { } id)
        {
            var existing = await _db.Entidades.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (existing is null) { return null; }
            entity = existing;
        }
        else
        {
            entity = new Entidad
            {
                TenantId = tenantId,
                Codigo = await NextCodigoAsync(ct),
                SortOrder = (await _db.Entidades.Select(x => (int?)x.SortOrder).MaxAsync(ct) ?? -1) + 1
            };
            _db.Entidades.Add(entity);
        }

        var isArea = req.Kind == EntidadKind.Area;
        entity.Kind = req.Kind;
        entity.Nombre = nombre;
        entity.Sigla = Clean(req.Sigla);
        // Un Area es una unidad organizativa interna: reusamos RepresentanteLegal como "Responsable".
        entity.RepresentanteLegal = Clean(req.RepresentanteLegal);
        entity.Telefono = Clean(req.Telefono);
        entity.Email = Clean(req.Email);
        entity.ZonaHoraria = Clean(req.ZonaHoraria);
        entity.Idioma = Clean(req.Idioma);
        entity.Observaciones = Clean(req.Observaciones);
        entity.IsActive = req.IsActive;
        entity.IsPrincipal = req.IsPrincipal;
        entity.FieldValuesJson = SerializeValues(req.FieldValues);

        // Los datos legales / de ubicacion fisica / logo solo aplican a una Sede. Para un Area se
        // limpian, para no arrastrar datos que su modal no expone.
        entity.TipoEntidad = isArea ? "Area" : Clean(req.TipoEntidad);
        entity.NombreComercial = isArea ? null : Clean(req.NombreComercial);
        entity.TaxId = isArea ? null : Clean(req.TaxId);
        entity.TaxIdDv = isArea ? null : Clean(req.TaxIdDv);
        entity.NaturalezaJuridica = isArea ? null : Clean(req.NaturalezaJuridica);
        entity.Pais = isArea ? null : Clean(req.Pais);
        entity.Departamento = isArea ? null : Clean(req.Departamento);
        entity.Ciudad = isArea ? null : Clean(req.Ciudad);
        entity.Direccion = isArea ? null : Clean(req.Direccion);
        entity.Web = isArea ? null : Clean(req.Web);
        entity.LogoBase64 = isArea || string.IsNullOrWhiteSpace(req.LogoBase64) ? null : req.LogoBase64;

        // Solo una principal por tenant.
        if (req.IsPrincipal)
        {
            var others = await _db.Entidades.Where(x => x.Id != entity.Id && x.IsPrincipal).ToListAsync(ct);
            foreach (var o in others) { o.IsPrincipal = false; }
        }

        await _db.SaveChangesAsync(ct);
        return MapDetail(entity);
    }

    public async Task<bool> SetArchivedAsync(Guid id, bool archived, Guid actorUserId, CancellationToken ct = default)
    {
        var e = await _db.Entidades.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        e.IsArchived = archived;
        if (archived) { e.IsPrincipal = false; }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetPrincipalAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var e = await _db.Entidades.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        var others = await _db.Entidades.Where(x => x.Id != id && x.IsPrincipal).ToListAsync(ct);
        foreach (var o in others) { o.IsPrincipal = false; }
        e.IsPrincipal = true;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Campos dinamicos ----

    public async Task<IReadOnlyList<EntidadFieldDefDto>> ListFieldDefsAsync(CancellationToken ct = default)
    {
        var list = await _db.EntidadFieldDefinitions.AsNoTracking()
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Label).ToListAsync(ct);
        return list.Select(MapFieldDef).ToList();
    }

    public async Task<EntidadFieldDefDto?> SaveFieldDefAsync(SaveEntidadFieldDefRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var label = (req.Label ?? "").Trim();
        if (string.IsNullOrWhiteSpace(label)) { return null; }

        EntidadFieldDefinition entity;
        if (req.Id is { } id)
        {
            var existing = await _db.EntidadFieldDefinitions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (existing is null) { return null; }
            entity = existing;
        }
        else
        {
            entity = new EntidadFieldDefinition
            {
                TenantId = tenantId,
                FieldKey = await UniqueFieldKeyAsync(label, ct),
                SortOrder = (await _db.EntidadFieldDefinitions.Select(x => (int?)x.SortOrder).MaxAsync(ct) ?? -1) + 1
            };
            _db.EntidadFieldDefinitions.Add(entity);
        }

        entity.Label = label;
        entity.FieldType = req.FieldType;
        entity.Options = Clean(req.Options);
        entity.Column = req.Column is 1 or 2 ? req.Column : 1;
        entity.IsRequired = req.IsRequired;
        entity.Description = Clean(req.Description);

        await _db.SaveChangesAsync(ct);
        return MapFieldDef(entity);
    }

    public async Task<bool> DeleteFieldDefAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var e = await _db.EntidadFieldDefinitions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        _db.EntidadFieldDefinitions.Remove(e);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Selector para Tareas ----

    public async Task<IReadOnlyList<EntidadOptionDto>> ListOptionsAsync(CancellationToken ct = default)
    {
        var list = await _db.Entidades.AsNoTracking()
            .Where(e => !e.IsArchived && e.IsActive)
            .OrderByDescending(e => e.IsPrincipal).ThenBy(e => e.SortOrder).ThenBy(e => e.Nombre)
            .Select(e => new { e.Id, e.Nombre, e.Ciudad })
            .ToListAsync(ct);
        return list.Select(e => new EntidadOptionDto(e.Id,
            string.IsNullOrWhiteSpace(e.Ciudad) ? e.Nombre : $"{e.Nombre} ({e.Ciudad})")).ToList();
    }

    // ---- Seed demo ----

    public async Task EnsureDemoAsync(CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return; }
        if (await _db.Entidades.AnyAsync(ct)) { return; }
        _db.Entidades.Add(new Entidad
        {
            TenantId = tenantId,
            Codigo = "ENT-01",
            Kind = EntidadKind.Sede,
            Nombre = "SKY SYSTEM S.A.S",
            NombreComercial = "SKY SYSTEM",
            Sigla = "SKY",
            TipoEntidad = "Empresa",
            TaxId = "900123456",
            TaxIdDv = "7",
            RepresentanteLegal = "Owner SKY SYSTEM",
            Pais = "Colombia",
            Departamento = "Cundinamarca",
            Ciudad = "Bogota",
            Direccion = "Calle 100 # 10-20",
            Telefono = "+57 1 555 0100",
            Email = "contacto@sky-system.local",
            Web = "https://sky-system.local",
            ZonaHoraria = "America/Bogota",
            Idioma = "es",
            IsPrincipal = true,
            IsActive = true,
            SortOrder = 0
        });
        await _db.SaveChangesAsync(ct);
    }

    // ---- Helpers ----

    private static EntidadDetailDto MapDetail(Entidad e) => new(
        e.Id, e.Codigo, e.Kind, e.Nombre, e.NombreComercial, e.Sigla, e.TipoEntidad,
        e.TaxId, e.TaxIdDv, e.RepresentanteLegal, e.NaturalezaJuridica,
        e.Pais, e.Departamento, e.Ciudad, e.Direccion, e.Telefono, e.Email, e.Web,
        e.ZonaHoraria, e.Idioma, e.Observaciones, e.LogoBase64,
        e.IsPrincipal, e.IsActive, e.IsArchived, DeserializeValues(e.FieldValuesJson));

    private static EntidadFieldDefDto MapFieldDef(EntidadFieldDefinition f) => new(
        f.Id, f.FieldKey, f.Label, f.FieldType, f.Options, f.Column, f.SortOrder,
        f.Description, f.IsRequired, f.IsSystem);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? SerializeValues(IReadOnlyDictionary<string, string?>? values)
    {
        if (values is null || values.Count == 0) { return null; }
        var clean = values.Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return clean.Count == 0 ? null : JsonSerializer.Serialize(clean, Json);
    }

    private static IReadOnlyDictionary<string, string?> DeserializeValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new Dictionary<string, string?>(); }
        try { return JsonSerializer.Deserialize<Dictionary<string, string?>>(json, Json) ?? new(); }
        catch { return new Dictionary<string, string?>(); }
    }

    private async Task<string> NextCodigoAsync(CancellationToken ct)
    {
        var used = await _db.Entidades.Select(e => e.Codigo).ToListAsync(ct);
        var set = new HashSet<string>(used, StringComparer.OrdinalIgnoreCase);
        for (var n = 1; n <= 999; n++)
        {
            var candidate = $"ENT-{n:00}";
            if (!set.Contains(candidate)) { return candidate; }
        }
        return $"ENT-{Guid.NewGuid():N}"[..10];
    }

    private async Task<string> UniqueFieldKeyAsync(string label, CancellationToken ct)
    {
        var baseKey = Slugify(label);
        if (string.IsNullOrEmpty(baseKey)) { baseKey = "campo"; }
        var used = await _db.EntidadFieldDefinitions.Select(f => f.FieldKey).ToListAsync(ct);
        var set = new HashSet<string>(used, StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(baseKey)) { return baseKey; }
        for (var n = 2; n <= 999; n++)
        {
            var candidate = $"{baseKey}-{n}";
            if (!set.Contains(candidate)) { return candidate; }
        }
        return $"{baseKey}-{Guid.NewGuid():N}"[..20];
    }

    private static string Slugify(string s)
    {
        var sb = new StringBuilder(s.Length);
        var prevDash = false;
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) && ch < 128) { sb.Append(ch); prevDash = false; }
            else if (ch is 'a' or 'e' or 'i' or 'o' or 'u') { sb.Append(ch); prevDash = false; }
            else if (!prevDash && sb.Length > 0) { sb.Append('-'); prevDash = true; }
        }
        return sb.ToString().Trim('-');
    }
}
