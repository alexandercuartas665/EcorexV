namespace Ecorex.Application.Scheduling;

/// <summary>
/// CRUD del Motor de programaciones (modulo 000889 "Programar actividad"). Sucesor del DAL legacy
/// cl_programacionActividades (que armaba SQL por concatenacion): aqui todo es EF Core parametrizado
/// y tenant-scoped por el filtro global. Ola P1: lista + detalle + guardar (crear/actualizar) +
/// activar/pausar. El disparo real (worker + bitacora) llega en P2.
/// </summary>
public interface IScheduledJobService
{
    /// <summary>Lista las programaciones del tenant para la bandeja.</summary>
    Task<IReadOnlyList<ScheduledJobListItemDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Detalle completo de una programacion (para editar en el modal). Null si no existe.</summary>
    Task<ScheduledJobDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Crea (id null) o actualiza una programacion con sus reglas y canales. Consecutivo PAC al crear.</summary>
    Task<ScheduledJobSaveResult> SaveAsync(Guid? id, SaveScheduledJobRequest request, CancellationToken cancellationToken = default);

    /// <summary>Alterna Active &lt;-&gt; Paused.</summary>
    Task ToggleStatusAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Elimina la programacion (cascada a reglas y canales). Sin UI en el prototipo aun.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Catalogo de Conceptos (000270) para los selects Categoria/Sub-categoria del tipo Actividad.</summary>
    Task<IReadOnlyList<ScheduledJobCategoryDto>> GetConceptCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>Bitacora de ejecucion de una programacion (ola P2), de la mas reciente a la mas antigua.</summary>
    Task<IReadOnlyList<ScheduledJobRunDto>> ListRunsAsync(Guid jobId, int take = 10, CancellationToken cancellationToken = default);

    /// <summary>KPIs del tenant: ejecutados hoy, errores y programaciones activas (ola P2).</summary>
    Task<ScheduledJobKpisDto> GetKpisAsync(CancellationToken cancellationToken = default);
}
