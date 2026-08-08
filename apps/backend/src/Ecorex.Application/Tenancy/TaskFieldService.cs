using System.Globalization;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Application.Formulas;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Implementacion de ITaskFieldService (campos configurables de la tarea POR tablero, ADR-0065).
/// Aislamiento por tenant via filtro global (nunca se filtra a mano por TenantId); el alta estampa
/// el TenantId del contexto. Calcado de ItemFieldService, agrupando por TaskBoard en vez de por
/// ItemType. La clave (slug) es unica por (tenant, tablero). Reusa FormulaEngine/FormulaCalculator
/// (motor compartido) para validar y evaluar los campos Calculated.
/// </summary>
public sealed class TaskFieldService : ITaskFieldService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public TaskFieldService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<TaskFieldDto>> ListAllAsync(CancellationToken cancellationToken = default) =>
        (await _db.TaskFieldDefinitions
            .AsNoTracking()
            .OrderBy(f => f.BoardId).ThenBy(f => f.SortOrder)
            .ToListAsync(cancellationToken))
            .Select(Map)
            .ToList();

    public async Task<IReadOnlyList<TaskFieldDto>> ListByBoardAsync(Guid boardId, CancellationToken cancellationToken = default) =>
        (await _db.TaskFieldDefinitions
            .AsNoTracking()
            .Where(f => f.BoardId == boardId)
            .OrderBy(f => f.SortOrder)
            .ToListAsync(cancellationToken))
            .Select(Map)
            .ToList();

    public async Task<TaskFieldDto?> CreateFieldAsync(CreateTaskFieldRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return null;
        }
        // El tablero debe existir en el tenant (el filtro global lo restringe al tenant activo).
        if (!await _db.TaskBoards.AnyAsync(b => b.Id == request.BoardId, cancellationToken))
        {
            return null;
        }
        var label = (request.Label ?? string.Empty).Trim();
        if (label.Length == 0)
        {
            return null;
        }

        var key = string.IsNullOrWhiteSpace(request.FieldKey) ? Slugify(label) : request.FieldKey.Trim();
        var existingKeys = await _db.TaskFieldDefinitions
            .Where(f => f.BoardId == request.BoardId).Select(f => f.FieldKey).ToListAsync(cancellationToken);
        key = EnsureUniqueKey(key, existingKeys);

        var formula = Clean(request.Formula);
        if (request.FieldType == TerceroFieldType.Calculated)
        {
            if (await ValidateFormulaAsync(formula, request.BoardId, null, key, cancellationToken) is not null) { return null; }
        }
        else
        {
            formula = null;   // solo los calculados llevan formula: no dejar restos de un cambio de tipo
        }

        var maxOrder = await _db.TaskFieldDefinitions
            .Where(f => f.BoardId == request.BoardId).Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1;
        var field = new TaskFieldDefinition
        {
            TenantId = tenantId,
            BoardId = request.BoardId,
            FieldKey = key,
            Label = label,
            FieldType = request.FieldType,
            Column = Math.Clamp(request.Column, MinColumn, MaxColumn),
            SortOrder = maxOrder + 1,
            Options = Clean(request.Options),
            Description = Clean(request.Description),
            AllowMultiple = request.AllowMultiple,
            Formula = formula,
            RepeatWithFieldKey = Clean(request.RepeatWithFieldKey),
            IsSystem = false
        };
        _db.TaskFieldDefinitions.Add(field);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    public async Task<TaskFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateTaskFieldRequest request, CancellationToken cancellationToken = default)
    {
        var field = await _db.TaskFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
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
            if (await ValidateFormulaAsync(formula, field.BoardId, fieldId, field.FieldKey, cancellationToken) is not null) { return null; }
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
        field.RepeatWithFieldKey = Clean(request.RepeatWithFieldKey);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    public async Task ReorderFieldsAsync(IReadOnlyList<Guid> orderedFieldIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedFieldIds);

        var fields = await _db.TaskFieldDefinitions.ToListAsync(cancellationToken);
        var order = 0;
        foreach (var id in orderedFieldIds)
        {
            var field = fields.FirstOrDefault(f => f.Id == id);
            if (field is not null) { field.SortOrder = order++; }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> MoveFieldToBoardAsync(Guid fieldId, Guid targetBoardId, CancellationToken cancellationToken = default)
    {
        var field = await _db.TaskFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null) { return "El campo no existe."; }
        if (field.BoardId == targetBoardId) { return null; }

        if (!await _db.TaskBoards.AnyAsync(b => b.Id == targetBoardId, cancellationToken))
        {
            return "El tablero destino no existe.";
        }

        // La clave es unica por (tenant, tablero): si el destino ya la tiene, el movimiento reventaria
        // contra el indice. Se avisa en vez de dejar que falle el SaveChanges.
        var choca = await _db.TaskFieldDefinitions
            .AnyAsync(f => f.BoardId == targetBoardId && f.FieldKey == field.FieldKey, cancellationToken);
        if (choca)
        {
            return $"El tablero destino ya tiene un campo con la clave '{field.FieldKey}'. Renombra uno de los dos.";
        }

        // Si alguna formula del tablero ORIGEN lo referencia, se quedaria sin ese dato al evaluarse.
        var loUsan = (await _db.TaskFieldDefinitions
            .AsNoTracking()
            .Where(f => f.BoardId == field.BoardId && f.Id != fieldId
                && f.FieldType == TerceroFieldType.Calculated && f.Formula != null)
            .ToListAsync(cancellationToken))
            .Where(f => FormulaEngine.Parse(f.Formula).References.Contains(field.FieldKey, StringComparer.Ordinal))
            .Select(f => f.Label)
            .ToList();
        if (loUsan.Count > 0)
        {
            return $"No se puede mover: lo usan las formulas de {string.Join(", ", loUsan)}.";
        }

        var maxOrder = await _db.TaskFieldDefinitions
            .Where(f => f.BoardId == targetBoardId)
            .Select(f => (int?)f.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

        field.BoardId = targetBoardId;
        field.SortOrder = maxOrder + 1;
        await _db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ValidateFormulaAsync(
        string? formula, Guid boardId, Guid? fieldId, string? fieldKey, CancellationToken cancellationToken = default)
    {
        var parsed = FormulaEngine.Parse(formula);
        if (!parsed.IsOk) { return parsed.Error; }

        // Solo el MISMO tablero: una tarea vive en un unico tablero, asi que los campos de otro no
        // existirian al evaluar. Aqui la clave si es unica dentro del tablero, sin ambiguedad posible.
        var sameBoard = await _db.TaskFieldDefinitions
            .AsNoTracking()
            .Where(f => f.BoardId == boardId && f.Id != fieldId)
            .ToListAsync(cancellationToken);
        var byKey = sameBoard.ToDictionary(f => f.FieldKey, f => f, StringComparer.Ordinal);

        var self = (fieldKey ?? string.Empty).Trim();

        foreach (var reference in parsed.References)
        {
            if (!string.IsNullOrEmpty(self) && string.Equals(reference, self, StringComparison.Ordinal))
            {
                return $"El campo no puede referenciarse a si mismo ({{{reference}}}).";
            }
            if (!byKey.TryGetValue(reference, out var target))
            {
                return $"No existe ningun campo con la clave {{{reference}}} en este tablero.";
            }
            if (target.FieldType is not (TerceroFieldType.Number or TerceroFieldType.Currency or TerceroFieldType.Calculated))
            {
                return $"{{{reference}}} es de tipo {target.FieldType} y no se puede usar en un calculo.";
            }
        }

        if (!string.IsNullOrEmpty(self))
        {
            var calculated = sameBoard
                .Where(f => f.FieldType == TerceroFieldType.Calculated && !string.IsNullOrWhiteSpace(f.Formula))
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
        Guid boardId, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default)
    {
        var calculated = (await _db.TaskFieldDefinitions
            .AsNoTracking()
            .Where(f => f.BoardId == boardId && f.FieldType == TerceroFieldType.Calculated && f.Formula != null)
            .ToListAsync(cancellationToken))
            .Select(f => new CalculatedField(f.FieldKey, f.Formula!))
            .ToList();

        return FormulaCalculator.EvaluateAll(calculated, values);
    }

    public async Task<bool> DeleteFieldAsync(Guid fieldId, CancellationToken cancellationToken = default)
    {
        var field = await _db.TaskFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null)
        {
            return false;
        }
        _db.TaskFieldDefinitions.Remove(field);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Anchos validos en la rejilla de 3: pequena (1/3), media (2/3), completo.</summary>
    private const int MinColumn = 1;
    private const int MaxColumn = 3;

    /// <summary>Texto opcional normalizado: vacio o espacios se guardan como null.</summary>
    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TaskFieldDto Map(TaskFieldDefinition f) =>
        new(f.Id, f.BoardId, f.FieldKey, f.Label, f.FieldType, f.Column, f.SortOrder, f.Options, f.Description,
            f.AllowMultiple, f.IsSystem, f.Formula, f.RepeatWithFieldKey);

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
