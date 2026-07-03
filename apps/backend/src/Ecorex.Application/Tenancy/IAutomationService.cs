namespace Ecorex.Application.Tenancy;

/// <summary>Reglas de automatizacion del tenant (modulo 2.5): disparador -> accion, encendido/apagado.</summary>
public interface IAutomationService
{
    Task<IReadOnlyList<AutomationRuleDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<AutomationRuleDto?> CreateAsync(SaveAutomationRuleRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<AutomationRuleDto?> UpdateAsync(Guid id, SaveAutomationRuleRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<AutomationRuleDto?> SetActiveAsync(Guid id, bool active, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Evalua ahora las reglas activas de "sin respuesta" y crea las tareas de seguimiento que correspondan.</summary>
    Task<AutomationRunResult> RunNowAsync(Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Crea un catalogo de reglas de ejemplo si el tenant aun no tiene ninguna. Devuelve cuantas creo.</summary>
    Task<int> SeedDefaultsAsync(CancellationToken cancellationToken = default);
}
