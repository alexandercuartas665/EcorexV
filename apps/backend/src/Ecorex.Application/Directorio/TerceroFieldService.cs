using System.Globalization;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Application.Formulas;
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

    // Las listas materializan y luego mapean con Map: antes proyectaban a mano y cada propiedad nueva
    // habia que acordarse de agregarla en tres sitios. Una sola forma de armar el DTO.
    public async Task<IReadOnlyList<TerceroFieldDto>> ListFieldsAsync(CancellationToken cancellationToken = default) =>
        (await _db.TerceroFieldDefinitions
            .AsNoTracking()
            .OrderBy(f => f.FichaKey).ThenBy(f => f.SortOrder)
            .ToListAsync(cancellationToken))
            .Select(Map)
            .ToList();

    public async Task<IReadOnlyList<TerceroFieldDto>> ListByFichaAsync(string fichaKey, CancellationToken cancellationToken = default)
    {
        var key = (fichaKey ?? string.Empty).Trim().ToLowerInvariant();
        return (await _db.TerceroFieldDefinitions
            .AsNoTracking()
            .Where(f => f.FichaKey == key)
            .OrderBy(f => f.SortOrder)
            .ToListAsync(cancellationToken))
            .Select(Map)
            .ToList();
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
        // La clave es unica por TENANT (no por ficha): una formula referencia {clave} sin decir de que
        // ficha, y ademas un campo se puede mover de ficha. Dos claves iguales harian ambigua la
        // referencia y el movimiento chocaria.
        var existingKeys = await _db.TerceroFieldDefinitions.Select(f => f.FieldKey).ToListAsync(cancellationToken);
        key = EnsureUniqueKey(key, existingKeys);

        var formula = Clean(request.Formula);
        if (request.FieldType == TerceroFieldType.Calculated)
        {
            if (await ValidateFormulaAsync(formula, null, key, cancellationToken) is not null) { return null; }
        }
        else
        {
            formula = null;   // solo los calculados llevan formula: no dejar restos de un cambio de tipo
        }

        var maxOrder = await _db.TerceroFieldDefinitions.Where(f => f.FichaKey == ficha).Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1;
        var field = new TerceroFieldDefinition
        {
            TenantId = tenantId,
            FichaKey = ficha,
            FieldKey = key,
            Label = label,
            FieldType = request.FieldType,
            Column = Math.Clamp(request.Column, MinColumn, MaxColumn),
            SortOrder = maxOrder + 1,
            Options = Clean(request.Options),
            Description = Clean(request.Description),
            AllowMultiple = request.AllowMultiple,
            Formula = formula,
            ShowInFilter = request.ShowInFilter,
            RepeatWithFieldKey = Clean(request.RepeatWithFieldKey),
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
        var formula = Clean(request.Formula);
        if (request.FieldType == TerceroFieldType.Calculated)
        {
            if (await ValidateFormulaAsync(formula, fieldId, field.FieldKey, cancellationToken) is not null) { return null; }
        }
        else
        {
            formula = null;
        }

        field.Label = label;
        field.FieldType = request.FieldType;
        field.Column = Math.Clamp(request.Column, MinColumn, MaxColumn);
        field.Options = Clean(request.Options);
        field.Description = Clean(request.Description);
        field.AllowMultiple = request.AllowMultiple;
        field.Formula = formula;
        field.ShowInFilter = request.ShowInFilter;
        field.RepeatWithFieldKey = Clean(request.RepeatWithFieldKey);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    public async Task<string?> MoveFieldToFichaAsync(Guid fieldId, string targetFichaKey, CancellationToken cancellationToken = default)
    {
        var ficha = (targetFichaKey ?? string.Empty).Trim().ToLowerInvariant();
        if (!FichaKeys.Contains(ficha)) { return "La ficha destino no existe."; }

        var field = await _db.TerceroFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null) { return "El campo no existe."; }
        if (field.FichaKey == ficha) { return null; }   // ya esta ahi: nada que hacer

        // La clave es unica POR FICHA (indice de BD), asi que si el destino ya la tiene el movimiento
        // reventaria contra el indice. Se avisa en vez de dejar que falle el SaveChanges.
        var choca = await _db.TerceroFieldDefinitions
            .AnyAsync(f => f.FichaKey == ficha && f.FieldKey == field.FieldKey, cancellationToken);
        if (choca)
        {
            return $"La ficha destino ya tiene un campo con la clave '{field.FieldKey}'. Renombra uno de los dos.";
        }

        // Aterriza al final de la ficha destino. La clave NO cambia, asi que los valores ya
        // capturados y las formulas que lo referencian siguen apuntando al mismo campo.
        var maxOrder = await _db.TerceroFieldDefinitions
            .Where(f => f.FichaKey == ficha)
            .Select(f => (int?)f.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

        field.FichaKey = ficha;
        field.SortOrder = maxOrder + 1;
        await _db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ValidateFormulaAsync(
        string? formula, Guid? fieldId, string? fieldKey, CancellationToken cancellationToken = default)
    {
        var parsed = FormulaEngine.Parse(formula);
        if (!parsed.IsOk) { return parsed.Error; }

        var all = await _db.TerceroFieldDefinitions.AsNoTracking().ToListAsync(cancellationToken);
        var candidates = all.Where(f => f.Id != fieldId).ToList();
        var self = (fieldKey ?? string.Empty).Trim();

        foreach (var reference in parsed.References)
        {
            if (!string.IsNullOrEmpty(self) && string.Equals(reference, self, StringComparison.Ordinal))
            {
                return $"El campo no puede referenciarse a si mismo ({{{reference}}}).";
            }

            var matches = candidates.Where(f => string.Equals(f.FieldKey, reference, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0)
            {
                return $"No existe ningun campo con la clave {{{reference}}}.";
            }

            // La clave solo es unica POR FICHA, asi que puede haber la misma en dos fichas (p.ej.
            // "dias_de_pago" en cliente y en proveedor). Ahi {clave} no dice a cual se refiere, y como
            // los valores viven por ficha en FichasJson, elegir uno seria adivinar. Se rechaza.
            if (matches.Count > 1)
            {
                var fichas = string.Join(" y ", matches.Select(m => m.FichaKey).OrderBy(x => x));
                return $"{{{reference}}} es ambiguo: existe en las fichas {fichas}. Renombra uno de los dos.";
            }

            var target = matches[0];
            if (target.FieldType is not (TerceroFieldType.Number or TerceroFieldType.Currency or TerceroFieldType.Calculated))
            {
                return $"{{{reference}}} es de tipo {target.FieldType} y no se puede usar en un calculo.";
            }

            // Un campo repetido guarda VARIOS valores (un arreglo JSON en su celda), no un numero:
            // el motor leeria "[\"12\",\"8\"]" como 0. Se rechaza en vez de dar un cero silencioso.
            if (!string.IsNullOrWhiteSpace(target.RepeatWithFieldKey) || target.AllowMultiple)
            {
                return $"{{{reference}}} captura varios valores y no se puede usar en un calculo.";
            }
        }

        // Ciclos: se simula el conjunto YA con este campo dentro (con su formula nueva).
        if (!string.IsNullOrEmpty(self))
        {
            var calculated = all
                .Where(f => f.Id != fieldId && f.FieldType == TerceroFieldType.Calculated && !string.IsNullOrWhiteSpace(f.Formula))
                .Select(f => new CalculatedField(f.FieldKey, f.Formula!))
                .Append(new CalculatedField(self, formula!))
                .ToList();

            if (FormulaCalculator.FindCycle(calculated) is string cycle)
            {
                return $"La formula crea un ciclo: {cycle}.";
            }
        }

        return null;
    }

    public async Task<IReadOnlyDictionary<string, string?>> ComputeCalculatedAsync(
        IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default)
    {
        var calculated = (await _db.TerceroFieldDefinitions
            .AsNoTracking()
            .Where(f => f.FieldType == TerceroFieldType.Calculated && f.Formula != null)
            .ToListAsync(cancellationToken))
            .Select(f => new CalculatedField(f.FieldKey, f.Formula!))
            .ToList();

        return FormulaCalculator.EvaluateAll(calculated, values);
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

    /// <summary>Anchos validos en la rejilla de 3: pequena (1/3), media (2/3), completo.</summary>
    private const int MinColumn = 1;
    private const int MaxColumn = 3;

    /// <summary>Texto opcional normalizado: vacio o espacios se guardan como null.</summary>
    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TerceroFieldDto Map(TerceroFieldDefinition f) =>
        new(f.Id, f.FichaKey, f.FieldKey, f.Label, f.FieldType, f.Column, f.SortOrder, f.Options, f.Description,
            f.AllowMultiple, f.IsSystem, f.Formula, f.ShowInFilter, f.RepeatWithFieldKey);

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
