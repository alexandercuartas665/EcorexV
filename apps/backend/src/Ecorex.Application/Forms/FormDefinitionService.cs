using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms;

/// <summary>
/// Implementacion del CRUD de definiciones de formulario (ADR-0015). Reglas clave:
/// FieldCode unico por definicion y con formato de identificador; Select/MultiCheck/Radio
/// exigen al menos una opcion parseable; el pattern de ValidationJson debe compilar; y toda
/// mutacion estructural sobre una definicion Active incrementa Revision (version de negocio,
/// distinta del token de concurrencia Version/IVersioned).
/// </summary>
public sealed partial class FormDefinitionService : IFormDefinitionService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$")]
    private static partial Regex FieldCodeRegex();

    public FormDefinitionService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<FormDefinitionListItemDto>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var rows = await _db.FormDefinitions.AsNoTracking()
            .Where(d => includeArchived || !d.IsArchived)
            .OrderBy(d => d.Code)
            .Select(d => new
            {
                d.Id, d.Code, d.Title, d.Description, d.Status, d.Revision, d.IsArchived, d.Version,
                QuestionCount = _db.FormQuestions.Count(q => q.DefinitionId == d.Id)
            })
            .ToListAsync(cancellationToken);
        return rows
            .Select(d => new FormDefinitionListItemDto(
                d.Id, d.Code, d.Title, d.Description, d.Status, d.Revision, d.IsArchived,
                d.QuestionCount, d.Version))
            .ToList();
    }

    public async Task<FormDefinitionDetailDto?> GetAsync(Guid definitionId, CancellationToken cancellationToken = default)
    {
        var definition = await _db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        return definition is null ? null : await BuildDetailAsync(definition, cancellationToken);
    }

    public async Task<FormResult<FormDefinitionDetailDto>> CreateAsync(CreateFormDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return FormResult<FormDefinitionDetailDto>.Invalid("No hay tenant activo.");
        }
        var code = (request.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length is 0 or > 20)
        {
            return FormResult<FormDefinitionDetailDto>.Invalid("El codigo es obligatorio (maximo 20 caracteres).");
        }
        var title = (request.Title ?? "").Trim();
        if (title.Length is 0 or > 200)
        {
            return FormResult<FormDefinitionDetailDto>.Invalid("El titulo es obligatorio (maximo 200 caracteres).");
        }
        if (await _db.FormDefinitions.AnyAsync(d => d.Code == code, cancellationToken))
        {
            return FormResult<FormDefinitionDetailDto>.Invalid($"Ya existe un formulario con el codigo {code}.");
        }

        var definition = new FormDefinition
        {
            TenantId = tenantId,
            Code = code,
            Title = title,
            Description = Normalize(request.Description),
            Status = FormStatus.Draft,
            Revision = 1
        };
        _db.FormDefinitions.Add(definition);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<FormDefinitionDetailDto>.Ok(await BuildDetailAsync(definition, cancellationToken));
    }

    public async Task<FormResult<FormDefinitionDetailDto>> UpdateHeaderAsync(Guid definitionId, UpdateFormDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var definition = await _db.FormDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return FormResult<FormDefinitionDetailDto>.NotFound("Formulario no encontrado.");
        }
        var title = (request.Title ?? "").Trim();
        if (title.Length is 0 or > 200)
        {
            return FormResult<FormDefinitionDetailDto>.Invalid("El titulo es obligatorio (maximo 200 caracteres).");
        }
        if (definition.Version != request.Version)
        {
            return FormResult<FormDefinitionDetailDto>.Conflict(ConflictMessage);
        }

        definition.Title = title;
        definition.Description = Normalize(request.Description);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return FormResult<FormDefinitionDetailDto>.Conflict(ConflictMessage);
        }
        return FormResult<FormDefinitionDetailDto>.Ok(await BuildDetailAsync(definition, cancellationToken));
    }

    public async Task<FormResult<FormDefinitionDetailDto>> ActivateAsync(Guid definitionId, CancellationToken cancellationToken = default)
    {
        var definition = await _db.FormDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return FormResult<FormDefinitionDetailDto>.NotFound("Formulario no encontrado.");
        }
        if (definition.IsArchived)
        {
            return FormResult<FormDefinitionDetailDto>.Invalid("El formulario esta archivado.");
        }
        if (definition.Status == FormStatus.Active)
        {
            return FormResult<FormDefinitionDetailDto>.Ok(await BuildDetailAsync(definition, cancellationToken));
        }

        // Validacion estructural completa antes de publicar.
        var questions = await _db.FormQuestions.AsNoTracking()
            .Where(q => q.DefinitionId == definition.Id)
            .ToListAsync(cancellationToken);
        if (!questions.Any(q => !FormFieldValidator.IsNonInput(q.ControlType)))
        {
            return FormResult<FormDefinitionDetailDto>.Invalid("El formulario necesita al menos una pregunta que capture datos.");
        }
        foreach (var question in questions)
        {
            var structural = ValidateQuestionStructure(question.ControlType, question.OptionsJson, question.ValidationJson);
            if (structural is not null)
            {
                return FormResult<FormDefinitionDetailDto>.Invalid($"Pregunta '{question.FieldCode}': {structural}");
            }
        }

        definition.Status = FormStatus.Active;
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<FormDefinitionDetailDto>.Ok(await BuildDetailAsync(definition, cancellationToken));
    }

    public async Task<FormResult<FormDefinitionDetailDto>> DeactivateAsync(Guid definitionId, CancellationToken cancellationToken = default)
    {
        var definition = await _db.FormDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return FormResult<FormDefinitionDetailDto>.NotFound("Formulario no encontrado.");
        }
        if (definition.Status != FormStatus.Active)
        {
            return FormResult<FormDefinitionDetailDto>.Invalid("Solo un formulario activo puede desactivarse.");
        }
        definition.Status = FormStatus.Inactive;
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<FormDefinitionDetailDto>.Ok(await BuildDetailAsync(definition, cancellationToken));
    }

    public async Task<FormResult<bool>> SetArchivedAsync(Guid definitionId, bool archived, CancellationToken cancellationToken = default)
    {
        var definition = await _db.FormDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return FormResult<bool>.NotFound("Formulario no encontrado.");
        }
        definition.IsArchived = archived;
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<bool>.Ok(true);
    }

    // ---- Contenedores ----

    public async Task<FormResult<FormContainerDto>> AddContainerAsync(Guid definitionId, SaveFormContainerRequest request, CancellationToken cancellationToken = default)
    {
        var definition = await _db.FormDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return FormResult<FormContainerDto>.NotFound("Formulario no encontrado.");
        }
        var name = (request.Name ?? "").Trim();
        if (name.Length is 0 or > 150)
        {
            return FormResult<FormContainerDto>.Invalid("El nombre del contenedor es obligatorio (maximo 150 caracteres).");
        }
        if (request.ParentId is Guid parentId
            && !await _db.FormContainers.AnyAsync(c => c.Id == parentId && c.DefinitionId == definitionId, cancellationToken))
        {
            return FormResult<FormContainerDto>.Invalid("El contenedor padre no pertenece al formulario.");
        }

        var maxOrder = await _db.FormContainers
            .Where(c => c.DefinitionId == definitionId && c.ParentId == request.ParentId)
            .MaxAsync(c => (int?)c.SortOrder, cancellationToken) ?? -1;
        var container = new FormContainer
        {
            TenantId = definition.TenantId,
            DefinitionId = definitionId,
            Name = name,
            ContainerType = request.ContainerType,
            ParentId = request.ParentId,
            SortOrder = maxOrder + 1,
            Style = Normalize(request.Style)
        };
        _db.FormContainers.Add(container);
        TouchRevision(definition);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<FormContainerDto>.Ok(ToDto(container));
    }

    public async Task<FormResult<FormContainerDto>> UpdateContainerAsync(Guid containerId, SaveFormContainerRequest request, CancellationToken cancellationToken = default)
    {
        var container = await _db.FormContainers.FirstOrDefaultAsync(c => c.Id == containerId, cancellationToken);
        if (container is null)
        {
            return FormResult<FormContainerDto>.NotFound("Contenedor no encontrado.");
        }
        var name = (request.Name ?? "").Trim();
        if (name.Length is 0 or > 150)
        {
            return FormResult<FormContainerDto>.Invalid("El nombre del contenedor es obligatorio (maximo 150 caracteres).");
        }
        if (request.ParentId == containerId)
        {
            return FormResult<FormContainerDto>.Invalid("Un contenedor no puede ser su propio padre.");
        }
        if (request.ParentId is Guid parentId)
        {
            var containers = await _db.FormContainers.AsNoTracking()
                .Where(c => c.DefinitionId == container.DefinitionId)
                .ToListAsync(cancellationToken);
            if (containers.All(c => c.Id != parentId))
            {
                return FormResult<FormContainerDto>.Invalid("El contenedor padre no pertenece al formulario.");
            }
            // El nuevo padre no puede ser un descendiente (evita ciclos en el arbol).
            var byId = containers.ToDictionary(c => c.Id);
            var cursor = (Guid?)parentId;
            while (cursor is Guid cid && byId.TryGetValue(cid, out var node))
            {
                if (node.Id == containerId)
                {
                    return FormResult<FormContainerDto>.Invalid("El contenedor padre no puede ser un descendiente.");
                }
                cursor = node.ParentId;
            }
        }

        container.Name = name;
        container.ContainerType = request.ContainerType;
        container.ParentId = request.ParentId;
        container.Style = Normalize(request.Style);
        await TouchRevisionAsync(container.DefinitionId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<FormContainerDto>.Ok(ToDto(container));
    }

    public async Task<FormResult<bool>> DeleteContainerAsync(Guid containerId, CancellationToken cancellationToken = default)
    {
        var container = await _db.FormContainers.FirstOrDefaultAsync(c => c.Id == containerId, cancellationToken);
        if (container is null)
        {
            return FormResult<bool>.NotFound("Contenedor no encontrado.");
        }

        // Reubicacion explicita (las FKs son NO ACTION): preguntas y sub-contenedores
        // pasan al padre del contenedor borrado (o a la raiz).
        var questions = await _db.FormQuestions
            .Where(q => q.ContainerId == containerId)
            .ToListAsync(cancellationToken);
        foreach (var question in questions)
        {
            question.ContainerId = container.ParentId;
        }
        var children = await _db.FormContainers
            .Where(c => c.ParentId == containerId)
            .ToListAsync(cancellationToken);
        foreach (var child in children)
        {
            child.ParentId = container.ParentId;
        }

        _db.FormContainers.Remove(container);
        await TouchRevisionAsync(container.DefinitionId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<bool>.Ok(true);
    }

    public async Task<FormResult<bool>> MoveContainerAsync(Guid containerId, bool moveUp, CancellationToken cancellationToken = default)
    {
        var container = await _db.FormContainers.FirstOrDefaultAsync(c => c.Id == containerId, cancellationToken);
        if (container is null)
        {
            return FormResult<bool>.NotFound("Contenedor no encontrado.");
        }
        var siblings = await _db.FormContainers
            .Where(c => c.DefinitionId == container.DefinitionId && c.ParentId == container.ParentId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
        if (!Swap(siblings, container.Id, moveUp))
        {
            return FormResult<bool>.Ok(false);
        }
        await TouchRevisionAsync(container.DefinitionId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<bool>.Ok(true);
    }

    // ---- Preguntas ----

    public async Task<FormResult<FormQuestionDto>> AddQuestionAsync(Guid definitionId, SaveFormQuestionRequest request, CancellationToken cancellationToken = default)
    {
        var definition = await _db.FormDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return FormResult<FormQuestionDto>.NotFound("Formulario no encontrado.");
        }
        var error = await ValidateQuestionRequestAsync(definitionId, request, existingQuestionId: null, cancellationToken);
        if (error is not null)
        {
            return FormResult<FormQuestionDto>.Invalid(error);
        }

        var maxOrder = await _db.FormQuestions
            .Where(q => q.DefinitionId == definitionId && q.ContainerId == request.ContainerId)
            .MaxAsync(q => (int?)q.SortOrder, cancellationToken) ?? -1;
        var question = new FormQuestion
        {
            TenantId = definition.TenantId,
            DefinitionId = definitionId,
            SortOrder = maxOrder + 1
        };
        ApplyRequest(question, request);
        _db.FormQuestions.Add(question);
        TouchRevision(definition);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<FormQuestionDto>.Ok(ToDto(question));
    }

    public async Task<FormResult<FormQuestionDto>> UpdateQuestionAsync(Guid questionId, SaveFormQuestionRequest request, CancellationToken cancellationToken = default)
    {
        var question = await _db.FormQuestions.FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);
        if (question is null)
        {
            return FormResult<FormQuestionDto>.NotFound("Pregunta no encontrada.");
        }
        var error = await ValidateQuestionRequestAsync(question.DefinitionId, request, questionId, cancellationToken);
        if (error is not null)
        {
            return FormResult<FormQuestionDto>.Invalid(error);
        }

        var containerChanged = question.ContainerId != request.ContainerId;
        ApplyRequest(question, request);
        if (containerChanged)
        {
            var maxOrder = await _db.FormQuestions
                .Where(q => q.DefinitionId == question.DefinitionId && q.ContainerId == request.ContainerId && q.Id != questionId)
                .MaxAsync(q => (int?)q.SortOrder, cancellationToken) ?? -1;
            question.SortOrder = maxOrder + 1;
        }
        await TouchRevisionAsync(question.DefinitionId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<FormQuestionDto>.Ok(ToDto(question));
    }

    public async Task<FormResult<bool>> DeleteQuestionAsync(Guid questionId, CancellationToken cancellationToken = default)
    {
        var question = await _db.FormQuestions.FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);
        if (question is null)
        {
            return FormResult<bool>.NotFound("Pregunta no encontrada.");
        }
        _db.FormQuestions.Remove(question);
        await TouchRevisionAsync(question.DefinitionId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<bool>.Ok(true);
    }

    public async Task<FormResult<bool>> MoveQuestionAsync(Guid questionId, bool moveUp, CancellationToken cancellationToken = default)
    {
        var question = await _db.FormQuestions.FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);
        if (question is null)
        {
            return FormResult<bool>.NotFound("Pregunta no encontrada.");
        }
        var siblings = await _db.FormQuestions
            .Where(q => q.DefinitionId == question.DefinitionId && q.ContainerId == question.ContainerId)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.CreatedAt)
            .ToListAsync(cancellationToken);
        if (!Swap(siblings, question.Id, moveUp))
        {
            return FormResult<bool>.Ok(false);
        }
        await TouchRevisionAsync(question.DefinitionId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<bool>.Ok(true);
    }

    // ---- Vinculo nodo de flujo -> formulario ----

    public async Task<FormResult<bool>> AssignToWorkflowNodeAsync(Guid workflowNodeId, Guid? definitionId, CancellationToken cancellationToken = default)
    {
        var node = await _db.WorkflowNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == workflowNodeId, cancellationToken);
        if (node is null)
        {
            return FormResult<bool>.NotFound("Nodo de flujo no encontrado.");
        }

        var existing = await _db.WorkflowNodeForms
            .FirstOrDefaultAsync(f => f.NodeId == workflowNodeId, cancellationToken);
        if (definitionId is not Guid formId)
        {
            if (existing is not null)
            {
                _db.WorkflowNodeForms.Remove(existing);
                await _db.SaveChangesAsync(cancellationToken);
            }
            return FormResult<bool>.Ok(true);
        }

        if (!await _db.FormDefinitions.AnyAsync(d => d.Id == formId && !d.IsArchived, cancellationToken))
        {
            return FormResult<bool>.NotFound("Formulario no encontrado.");
        }
        if (existing is null)
        {
            _db.WorkflowNodeForms.Add(new WorkflowNodeForm
            {
                TenantId = node.TenantId,
                NodeId = workflowNodeId,
                DefinitionId = formId
            });
        }
        else
        {
            existing.DefinitionId = formId;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<bool>.Ok(true);
    }

    public async Task<Guid?> GetWorkflowNodeFormAsync(Guid workflowNodeId, CancellationToken cancellationToken = default)
        => await _db.WorkflowNodeForms.AsNoTracking()
            .Where(f => f.NodeId == workflowNodeId)
            .Select(f => (Guid?)f.DefinitionId)
            .FirstOrDefaultAsync(cancellationToken);

    // ---- Helpers ----

    private const string ConflictMessage = "Otro usuario modifico el formulario. Recarga e intenta de nuevo.";

    private async Task<FormDefinitionDetailDto> BuildDetailAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        var containers = await _db.FormContainers.AsNoTracking()
            .Where(c => c.DefinitionId == definition.Id)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
        var questions = await _db.FormQuestions.AsNoTracking()
            .Where(q => q.DefinitionId == definition.Id)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.CreatedAt)
            .ToListAsync(cancellationToken);
        return new FormDefinitionDetailDto(
            definition.Id, definition.Code, definition.Title, definition.Description,
            definition.Status, definition.Revision, definition.IsArchived, definition.Version,
            containers.Select(ToDto).ToList(),
            questions.Select(ToDto).ToList());
    }

    private static FormContainerDto ToDto(FormContainer c)
        => new(c.Id, c.Name, c.ContainerType, c.ParentId, c.SortOrder, c.Style);

    private static FormQuestionDto ToDto(FormQuestion q)
        => new(q.Id, q.ContainerId, q.FieldCode, q.Label, q.Caption, q.HelpText, q.ControlType,
            q.OptionsJson, q.Required, q.SortOrder, q.GridCol, q.Numeral, q.ValidationJson);

    private static void ApplyRequest(FormQuestion question, SaveFormQuestionRequest request)
    {
        question.ContainerId = request.ContainerId;
        question.FieldCode = request.FieldCode.Trim();
        question.Label = request.Label.Trim();
        question.Caption = Normalize(request.Caption);
        question.HelpText = Normalize(request.HelpText);
        question.ControlType = request.ControlType;
        question.OptionsJson = Normalize(request.OptionsJson);
        question.Required = request.Required && !FormFieldValidator.IsNonInput(request.ControlType);
        question.GridCol = string.IsNullOrWhiteSpace(request.GridCol) ? "col-12" : request.GridCol.Trim();
        question.Numeral = Normalize(request.Numeral);
        question.ValidationJson = Normalize(request.ValidationJson);
    }

    private async Task<string?> ValidateQuestionRequestAsync(
        Guid definitionId, SaveFormQuestionRequest request, Guid? existingQuestionId,
        CancellationToken cancellationToken)
    {
        var fieldCode = (request.FieldCode ?? "").Trim();
        if (fieldCode.Length is 0 or > 60)
        {
            return "FieldCode es obligatorio (maximo 60 caracteres).";
        }
        if (!FieldCodeRegex().IsMatch(fieldCode))
        {
            return "FieldCode debe iniciar con letra y usar solo letras, numeros, guion y guion bajo.";
        }
        var label = (request.Label ?? "").Trim();
        if (label.Length is 0 or > 500)
        {
            return "La etiqueta es obligatoria (maximo 500 caracteres).";
        }
        if (request.ContainerId is Guid containerId
            && !await _db.FormContainers.AnyAsync(c => c.Id == containerId && c.DefinitionId == definitionId, cancellationToken))
        {
            return "El contenedor no pertenece al formulario.";
        }
        // FieldCode unico por definicion (clave del documento JSON de respuestas).
        var duplicated = await _db.FormQuestions.AnyAsync(
            q => q.DefinitionId == definitionId && q.FieldCode == fieldCode && q.Id != existingQuestionId,
            cancellationToken);
        if (duplicated)
        {
            return $"Ya existe una pregunta con el FieldCode '{fieldCode}' en este formulario.";
        }
        return ValidateQuestionStructure(request.ControlType, request.OptionsJson, request.ValidationJson);
    }

    /// <summary>Reglas estructurales por tipo: opciones obligatorias y pattern compilable.</summary>
    private static string? ValidateQuestionStructure(FormControlType type, string? optionsJson, string? validationJson)
    {
        if (type is FormControlType.Select or FormControlType.MultiCheck or FormControlType.Radio)
        {
            var options = FormFieldValidator.ParseOptions(optionsJson);
            if (options.Count == 0)
            {
                return "Este tipo de control requiere al menos una opcion valida ([{id,label,value}]).";
            }
            if (options.Any(o => string.IsNullOrWhiteSpace(o.Id) || string.IsNullOrWhiteSpace(o.Label)))
            {
                return "Toda opcion necesita id y label.";
            }
            if (options.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != options.Count)
            {
                return "Los ids de las opciones deben ser unicos.";
            }
        }
        var rules = FormFieldValidator.ParseRules(validationJson);
        if (!string.IsNullOrWhiteSpace(validationJson) && rules is null)
        {
            return "ValidationJson no es un JSON valido ({minLength,maxLength,pattern,minValue,maxValue}).";
        }
        if (!string.IsNullOrEmpty(rules?.Pattern))
        {
            try
            {
                _ = new Regex(rules.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250));
            }
            catch (ArgumentException)
            {
                return "El pattern de validacion no es una expresion regular valida.";
            }
        }
        if (rules is { MinLength: int minL, MaxLength: int maxL } && minL > maxL)
        {
            return "minLength no puede ser mayor que maxLength.";
        }
        if (rules is { MinValue: decimal minV, MaxValue: decimal maxV } && minV > maxV)
        {
            return "minValue no puede ser mayor que maxValue.";
        }
        return null;
    }

    /// <summary>
    /// Snapshot logico de version: los cambios estructurales sobre una definicion Active
    /// incrementan Revision (las respuestas ya enviadas conservan su documento tal cual).
    /// </summary>
    private static void TouchRevision(FormDefinition definition)
    {
        if (definition.Status == FormStatus.Active)
        {
            definition.Revision++;
        }
    }

    private async Task TouchRevisionAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var definition = await _db.FormDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is not null)
        {
            TouchRevision(definition);
        }
    }

    /// <summary>Reordena por intercambio de SortOrder normalizado. Devuelve false si no hay a donde mover.</summary>
    private static bool Swap<T>(List<T> siblings, Guid id, bool moveUp) where T : class
    {
        // Normaliza SortOrder secuencial y ubica el elemento.
        var index = siblings.FindIndex(s => GetId(s) == id);
        if (index < 0)
        {
            return false;
        }
        var target = moveUp ? index - 1 : index + 1;
        if (target < 0 || target >= siblings.Count)
        {
            return false;
        }
        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        for (var i = 0; i < siblings.Count; i++)
        {
            SetSortOrder(siblings[i], i);
        }
        return true;

        static Guid GetId(T item) => item switch
        {
            FormContainer c => c.Id,
            FormQuestion q => q.Id,
            _ => Guid.Empty
        };
        static void SetSortOrder(T item, int order)
        {
            switch (item)
            {
                case FormContainer c: c.SortOrder = order; break;
                case FormQuestion q: q.SortOrder = order; break;
            }
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
