using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms;

/// <summary>
/// Import / Export de definiciones de formulario por JSON portable (cabecera + contenedores +
/// preguntas). El Export serializa el DetailDto dentro de un sobre con version de formato. El Import
/// crea SIEMPRE un formulario NUEVO (codigo unico): nunca sobrescribe uno existente. Reutiliza los
/// metodos de mutacion validados (CreateAsync/AddContainerAsync/AddQuestionAsync), remapeando los Ids
/// de contenedor (padres antes que hijos) para preservar la jerarquia y el orden.
/// </summary>
public sealed partial class FormDefinitionService
{
    private sealed record FormExportEnvelope(int FormatVersion, FormDefinitionDetailDto Definition);

    private static readonly JsonSerializerOptions ImpExpJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<FormResult<string>> ExportAsync(Guid definitionId, CancellationToken cancellationToken = default)
    {
        var definition = await _db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null) { return FormResult<string>.NotFound("Formulario no encontrado."); }
        var detail = await BuildDetailAsync(definition, cancellationToken);
        var json = JsonSerializer.Serialize(new FormExportEnvelope(1, detail), ImpExpJson);
        return FormResult<string>.Ok(json);
    }

    public async Task<FormResult<FormDefinitionDetailDto>> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return FormResult<FormDefinitionDetailDto>.Invalid("El JSON esta vacio.");
        }

        FormDefinitionDetailDto? src;
        try
        {
            // Acepta el sobre {formatVersion, definition} o el DetailDto pelado.
            src = json.Contains("\"definition\"", StringComparison.OrdinalIgnoreCase)
                ? JsonSerializer.Deserialize<FormExportEnvelope>(json, ImpExpJson)?.Definition
                : JsonSerializer.Deserialize<FormDefinitionDetailDto>(json, ImpExpJson);
        }
        catch (JsonException ex)
        {
            return FormResult<FormDefinitionDetailDto>.Invalid($"JSON invalido: {ex.Message}");
        }

        if (src is null || string.IsNullOrWhiteSpace(src.Title))
        {
            return FormResult<FormDefinitionDetailDto>.Invalid("El JSON no contiene un formulario valido (falta el titulo).");
        }

        var code = await UniqueFormCodeAsync(src.Code, src.Title, cancellationToken);

        var created = await CreateAsync(new CreateFormDefinitionRequest(code, src.Title, src.Description), cancellationToken);
        if (!created.IsOk || created.Value is null) { return created; }
        var defId = created.Value.Id;

        // Cabecera: transaccionalidad + layout de tarjeta (si difieren del default). El modulo/menu NO
        // se replica en el import (evita crear nodos de menu inesperados; se habilita a mano si se quiere).
        if (src.IsTransactional)
        {
            await SetTransactionalAsync(defId,
                new SetFormTransactionalRequest(true, src.IdentityMode, src.IdentitySourceFieldCode, src.CardLayout),
                cancellationToken);
        }
        else if (src.CardLayout != FormCardLayout.Normal)
        {
            await SetTransactionalAsync(defId,
                new SetFormTransactionalRequest(false, FormIdentityMode.None, null, src.CardLayout),
                cancellationToken);
        }

        // Contenedores: crear PADRES antes que hijos, remapeando ParentId viejo -> nuevo.
        var idMap = new Dictionary<Guid, Guid>();
        foreach (var c in OrderContainersParentFirst(src.Containers))
        {
            Guid? newParent = c.ParentId is Guid p && idMap.TryGetValue(p, out var np) ? np : null;
            var req = new SaveFormContainerRequest(
                c.Name, c.ContainerType, newParent, c.Style, c.TabsJson, c.Width, c.IsLocked, c.IsHidden, c.InlineLabels,
                c.AllowedCargosJson, c.VisibleWhenJson);
            var res = await AddContainerAsync(defId, req, cancellationToken);
            if (res.IsOk && res.Value is not null) { idMap[c.Id] = res.Value.Id; }
        }

        // Preguntas: en orden, remapeando ContainerId. SubformDefinitionId es una referencia cruzada a
        // otra definicion: se conserva solo si existe en este tenant; si no, se anula (evita ref colgada).
        foreach (var q in src.Questions.OrderBy(x => x.SortOrder))
        {
            Guid? newContainer = q.ContainerId is Guid cid && idMap.TryGetValue(cid, out var nc) ? nc : null;
            Guid? subform = q.SubformDefinitionId is Guid sf
                && await _db.FormDefinitions.AnyAsync(d => d.Id == sf, cancellationToken) ? sf : null;
            var req = new SaveFormQuestionRequest(
                newContainer, q.FieldCode, q.Label, q.ControlType, q.Caption, q.HelpText, q.OptionsJson,
                q.Required, q.GridCol, q.Numeral, q.ValidationJson, q.Width, q.PlaceholderText, q.DefaultValue,
                q.IsLocked, q.IsHidden, q.SourceKind, q.SourceRef, q.DisplayField, q.ValueField, q.FilterJson,
                q.AutofillMapJson, q.Presentation, q.CalcExpression, q.Aggregate, subform,
                q.DefaultDynamic, q.Format, q.FieldVisibilityJson, q.CascadeConfigJson, q.VisibleWhenJson);
            await AddQuestionAsync(defId, req, cancellationToken);
        }

        var final = await _db.FormDefinitions.FirstOrDefaultAsync(d => d.Id == defId, cancellationToken);
        return FormResult<FormDefinitionDetailDto>.Ok(await BuildDetailAsync(final!, cancellationToken));
    }

    // Orden topologico simple: primero los sin padre, luego los que ya tienen su padre emitido.
    private static IEnumerable<FormContainerDto> OrderContainersParentFirst(IReadOnlyList<FormContainerDto> containers)
    {
        var remaining = containers.OrderBy(c => c.SortOrder).ToList();
        var emitted = new HashSet<Guid>();
        var result = new List<FormContainerDto>();
        var guard = 0;
        while (remaining.Count > 0 && guard++ < 2000)
        {
            var progressed = false;
            foreach (var c in remaining.ToList())
            {
                if (c.ParentId is null || emitted.Contains(c.ParentId.Value))
                {
                    result.Add(c);
                    emitted.Add(c.Id);
                    remaining.Remove(c);
                    progressed = true;
                }
            }
            if (!progressed) { result.AddRange(remaining); break; } // ciclo o padre ausente: emite el resto
        }
        return result;
    }

    // Codigo unico (<=20, mayusculas). Base = codigo original o derivado del titulo; sufijo -N si choca.
    private async Task<string> UniqueFormCodeAsync(string? preferred, string title, CancellationToken ct)
    {
        var baseCode = (preferred ?? string.Empty).Trim().ToUpperInvariant();
        if (baseCode.Length == 0)
        {
            baseCode = new string((title ?? "FORM").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        }
        if (baseCode.Length == 0) { baseCode = "FORM"; }
        if (baseCode.Length > 16) { baseCode = baseCode[..16]; }

        var code = baseCode;
        var n = 1;
        while (await _db.FormDefinitions.AnyAsync(d => d.Code == code, ct))
        {
            n++;
            var suffix = "-" + n;
            var room = 20 - suffix.Length;
            code = (baseCode.Length > room ? baseCode[..room] : baseCode) + suffix;
        }
        return code;
    }
}
