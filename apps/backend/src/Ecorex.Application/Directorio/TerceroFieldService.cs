using System.Globalization;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Implementacion de ITerceroFieldService (campos configurables por ficha del Directorio
/// General, modulo 000232). Aislamiento por tenant via filtro global (nunca se filtra a mano
/// por TenantId); el alta estampa el TenantId del contexto. Calcado del patron ya probado de
/// PipelineService (CUBOT.travels), agrupando por ficha en vez de por etapa.
/// </summary>
public sealed class TerceroFieldService : ITerceroFieldService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public TerceroFieldService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>Fichas validas del Directorio General (clave -> campos por defecto del prototipo).</summary>
    public static readonly IReadOnlyList<string> FichaKeys =
        ["fiscal", "comercial", "cliente", "proveedor", "empleado"];

    // Campos por defecto de cada ficha, tomados del spec del prototipo (000232). El orden de la
    // lista fija el SortOrder; la columna alterna 1/2 al construir.
    private static readonly (string Ficha, (string Key, string Label, TerceroFieldType Type, string? Options)[] Fields)[] Defaults =
    [
        ("fiscal",
        [
            ("tipo_de_persona", "Tipo de persona", TerceroFieldType.Select, "Natural\nJuridica"),
            ("regimen_tributario", "Regimen tributario", TerceroFieldType.Select, "Responsable de IVA\nNo responsable de IVA\nGran contribuyente"),
            ("actividad_economica_ciiu", "Actividad economica (CIIU)", TerceroFieldType.Text, null),
            ("autorretenedor", "Autorretenedor", TerceroFieldType.Select, "Si\nNo"),
            ("razon_social", "Razon social", TerceroFieldType.Text, null),
            ("sector_industria", "Sector / Industria", TerceroFieldType.Text, null),
            ("tamano_de_la_empresa", "Tamano de la empresa", TerceroFieldType.Select, "Micro\nPequena\nMediana\nGrande"),
            ("sitio_web", "Sitio web", TerceroFieldType.Text, null),
            ("representante_legal", "Representante legal", TerceroFieldType.Text, null),
            ("direccion", "Direccion", TerceroFieldType.Text, null)
        ]),
        ("comercial",
        [
            ("vendedor_asignado", "Vendedor asignado", TerceroFieldType.Text, null),
            ("zona_territorio", "Zona / Territorio", TerceroFieldType.Text, null),
            ("lista_de_precios", "Lista de precios", TerceroFieldType.Select, "General\nMayorista\nDistribuidor"),
            ("origen", "Origen", TerceroFieldType.Select, "LinkedIn\nMaps\nWeb\nReferido\nCampana\nImportado\nManual\nFrio"),
            ("motivo_de_sospecha", "Motivo de sospecha", TerceroFieldType.Text, null),
            ("nivel_de_riesgo", "Nivel de riesgo", TerceroFieldType.Select, "Alto\nMedio\nBajo"),
            ("estado_de_riesgo", "Estado de riesgo", TerceroFieldType.Select, "En revision\nBloqueado\nLiberado"),
            ("reportado_por", "Reportado por", TerceroFieldType.Text, null)
        ]),
        ("cliente",
        [
            ("cupo_de_credito", "Cupo de credito", TerceroFieldType.Number, null),
            ("dias_de_pago", "Dias de pago", TerceroFieldType.Number, null),
            ("direccion_de_factura", "Direccion de factura", TerceroFieldType.Text, null),
            ("direccion_de_despacho", "Direccion de despacho", TerceroFieldType.Text, null)
        ]),
        ("proveedor",
        [
            ("dias_de_pago", "Dias de pago", TerceroFieldType.Number, null),
            ("forma_de_pago", "Forma de pago", TerceroFieldType.Select, "Contado\nCredito\nAnticipo"),
            ("cuenta_bancaria", "Cuenta bancaria", TerceroFieldType.Text, null)
        ]),
        ("empleado",
        [
            ("cargo", "Cargo", TerceroFieldType.Text, null),
            ("tipo_de_contrato", "Tipo de contrato", TerceroFieldType.Select, "Indefinido\nFijo\nPrestacion de servicios\nAprendizaje"),
            ("salario", "Salario", TerceroFieldType.Number, null),
            ("fecha_de_ingreso", "Fecha de ingreso", TerceroFieldType.Date, null)
        ])
    ];

    /// <summary>
    /// Construye las definiciones de campos por defecto (IsSystem=true) para un tenant. Se usa
    /// tanto en EnsureDefaultsAsync (tenant del contexto) como en el seeder (tenant explicito),
    /// para que los defaults vivan en un solo lugar. La columna alterna 1/2 dentro de cada ficha.
    /// </summary>
    public static IReadOnlyList<TerceroFieldDefinition> BuildDefaultFields(Guid tenantId)
    {
        var result = new List<TerceroFieldDefinition>();
        foreach (var (ficha, fields) in Defaults)
        {
            var order = 0;
            foreach (var (key, label, type, options) in fields)
            {
                result.Add(new TerceroFieldDefinition
                {
                    TenantId = tenantId,
                    FichaKey = ficha,
                    FieldKey = key,
                    Label = label,
                    FieldType = type,
                    Options = options,
                    Column = (order % 2) + 1,
                    SortOrder = order++,
                    IsSystem = true
                });
            }
        }
        return result;
    }

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return;
        }
        if (await _db.TerceroFieldDefinitions.AnyAsync(cancellationToken))
        {
            return;
        }

        _db.TerceroFieldDefinitions.AddRange(BuildDefaultFields(tenantId));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TerceroFieldDto>> ListFieldsAsync(CancellationToken cancellationToken = default) =>
        await _db.TerceroFieldDefinitions
            .AsNoTracking()
            .OrderBy(f => f.FichaKey).ThenBy(f => f.SortOrder)
            .Select(f => new TerceroFieldDto(f.Id, f.FichaKey, f.FieldKey, f.Label, f.FieldType, f.Column, f.SortOrder, f.Options, f.Description, f.AllowMultiple, f.IsSystem))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TerceroFieldDto>> ListByFichaAsync(string fichaKey, CancellationToken cancellationToken = default)
    {
        var key = (fichaKey ?? string.Empty).Trim().ToLowerInvariant();
        return await _db.TerceroFieldDefinitions
            .AsNoTracking()
            .Where(f => f.FichaKey == key)
            .OrderBy(f => f.SortOrder)
            .Select(f => new TerceroFieldDto(f.Id, f.FichaKey, f.FieldKey, f.Label, f.FieldType, f.Column, f.SortOrder, f.Options, f.Description, f.AllowMultiple, f.IsSystem))
            .ToListAsync(cancellationToken);
    }

    public async Task<TerceroFieldDto?> CreateFieldAsync(CreateTerceroFieldRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return null;
        }
        var ficha = (request.FichaKey ?? string.Empty).Trim().ToLowerInvariant();
        if (!FichaKeys.Contains(ficha))
        {
            return null;
        }
        var label = (request.Label ?? string.Empty).Trim();
        if (label.Length == 0)
        {
            return null;
        }

        var key = string.IsNullOrWhiteSpace(request.FieldKey) ? Slugify(label) : request.FieldKey.Trim();
        var existingKeys = await _db.TerceroFieldDefinitions.Where(f => f.FichaKey == ficha).Select(f => f.FieldKey).ToListAsync(cancellationToken);
        key = EnsureUniqueKey(key, existingKeys);

        var maxOrder = await _db.TerceroFieldDefinitions.Where(f => f.FichaKey == ficha).Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1;
        var field = new TerceroFieldDefinition
        {
            TenantId = tenantId,
            FichaKey = ficha,
            FieldKey = key,
            Label = label,
            FieldType = request.FieldType,
            Column = Math.Clamp(request.Column, 1, 2),
            SortOrder = maxOrder + 1,
            Options = string.IsNullOrWhiteSpace(request.Options) ? null : request.Options.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            AllowMultiple = request.AllowMultiple,
            IsSystem = false
        };
        _db.TerceroFieldDefinitions.Add(field);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    public async Task<TerceroFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateTerceroFieldRequest request, CancellationToken cancellationToken = default)
    {
        var field = await _db.TerceroFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null)
        {
            return null;
        }
        var label = (request.Label ?? string.Empty).Trim();
        if (label.Length == 0)
        {
            return null;
        }
        field.Label = label;
        field.FieldType = request.FieldType;
        field.Column = Math.Clamp(request.Column, 1, 2);
        field.Options = string.IsNullOrWhiteSpace(request.Options) ? null : request.Options.Trim();
        field.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        field.AllowMultiple = request.AllowMultiple;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    public async Task ReorderFieldsAsync(ReorderFieldsRequest request, CancellationToken cancellationToken = default)
    {
        var fields = await _db.TerceroFieldDefinitions.ToListAsync(cancellationToken);
        var order = 0;
        foreach (var id in request.OrderedFieldIds)
        {
            var field = fields.FirstOrDefault(f => f.Id == id);
            if (field is not null) { field.SortOrder = order++; }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteFieldAsync(Guid fieldId, CancellationToken cancellationToken = default)
    {
        var field = await _db.TerceroFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null)
        {
            return false;
        }
        _db.TerceroFieldDefinitions.Remove(field);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static TerceroFieldDto Map(TerceroFieldDefinition f) =>
        new(f.Id, f.FichaKey, f.FieldKey, f.Label, f.FieldType, f.Column, f.SortOrder, f.Options, f.Description, f.AllowMultiple, f.IsSystem);

    private static string EnsureUniqueKey(string key, IReadOnlyCollection<string> existing)
    {
        if (!existing.Contains(key))
        {
            return key;
        }
        var i = 2;
        while (existing.Contains($"{key}{i}"))
        {
            i++;
        }
        return $"{key}{i}";
    }

    private static string Slugify(string label)
    {
        var normalized = label.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) { continue; }
            if (char.IsLetterOrDigit(c)) { sb.Append(c); }
            else if (sb.Length > 0 && sb[^1] != '_') { sb.Append('_'); }
        }
        var slug = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(slug) ? "campo" : slug;
    }
}
