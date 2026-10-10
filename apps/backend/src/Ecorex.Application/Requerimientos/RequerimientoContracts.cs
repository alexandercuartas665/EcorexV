using Ecorex.Domain.Enums;

namespace Ecorex.Application.Requerimientos;

/// <summary>Una necesidad de compra (linea) para la grilla, con nombres resueltos.</summary>
public sealed record RequerimientoDto(
    Guid Id,
    Guid? OrigenId,
    string? OrigenNombre,
    string Area,
    Guid? ItemId,
    string? ItemNombre,
    string? ItemSku,
    string Descripcion,
    decimal Cantidad,
    string UnidadMedida,
    DateTimeOffset? FechaNecesidad,
    RequerimientoEstado Estado,
    string ClaveAgrupacion,
    int CantidadHijos,
    decimal CantidadPlanificada);

/// <summary>Filtro de la grilla de necesidades.</summary>
public sealed record RequerimientoFiltro(string? Texto = null, RequerimientoEstado? Estado = null, Guid? OrigenId = null);

/// <summary>Alta manual de una necesidad.</summary>
public sealed record CrearRequerimientoRequest(
    Guid? OrigenId,
    string Area,
    Guid? ItemId,
    string Descripcion,
    decimal Cantidad,
    string UnidadMedida,
    DateTimeOffset? FechaNecesidad);

/// <summary>Opcion de item para el selector (del modulo de Inventarios).</summary>
public sealed record ItemOpcionDto(Guid Id, string Nombre, string? Sku);

/// <summary>Un registro HIJO (division/granularidad) de una necesidad.</summary>
public sealed record RequerimientoHijoDto(
    Guid Id,
    decimal Cantidad,
    DateTimeOffset? FechaProgramadaCompra,
    string CaracteristicasDespacho,
    string TiempoEntrega,
    Guid? ProveedorId,
    RequerimientoEstado Estado,
    Guid? TaskItemId);

/// <summary>Entrada para crear/editar un hijo (Id null = nuevo).</summary>
public sealed record HijoInput(
    Guid? Id,
    decimal Cantidad,
    DateTimeOffset? FechaProgramadaCompra,
    string? CaracteristicasDespacho,
    string? TiempoEntrega);

/// <summary>Guarda TODA la planificacion (hijos) de una necesidad de una vez (reemplaza el conjunto).</summary>
public sealed record GuardarHijosRequest(Guid RequerimientoId, IReadOnlyList<HijoInput> Hijos);

/// <summary>Estado de la planificacion de una necesidad: cantidades y sus hijos.</summary>
public sealed record PlanificacionDto(
    Guid RequerimientoId,
    string Producto,
    decimal CantidadPadre,
    decimal CantidadPlanificada,
    decimal CantidadSinPlanificar,
    IReadOnlyList<RequerimientoHijoDto> Hijos);

/// <summary>
/// Operativa del modulo de Requerimientos (Capa 10, Ola 2): recepcion (alta manual) + listado para la
/// grilla + agrupacion por producto. La ingesta automatica desde formularios se define en una fase
/// posterior (decision abierta #1). Tenant-scoped por el filtro global.
/// </summary>
public interface IRequerimientoService
{
    Task<IReadOnlyList<RequerimientoDto>> ListAsync(RequerimientoFiltro filtro, CancellationToken ct = default);
    Task<RequerimientoDto?> CrearAsync(CrearRequerimientoRequest req, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Items activos del modulo de Inventarios para el selector.</summary>
    Task<IReadOnlyList<ItemOpcionDto>> ListItemsAsync(CancellationToken ct = default);
    /// <summary>Origenes ACTIVOS para el alta.</summary>
    Task<IReadOnlyList<RequerimientoOpcionDto>> ListOrigenesActivosAsync(CancellationToken ct = default);

    // ---- Ola 3: granularidad (division en registros hijos + programacion) ----
    /// <summary>Planificacion actual de una necesidad (cantidades + hijos).</summary>
    Task<PlanificacionDto?> GetPlanificacionAsync(Guid requerimientoId, CancellationToken ct = default);
    /// <summary>Reemplaza el conjunto de hijos de una necesidad. Valida que la suma NO exceda al padre;
    /// pone el padre en Planificado si queda con hijos (o Recibido si queda sin ninguno). Devuelve el nuevo estado.</summary>
    Task<PlanificacionDto?> GuardarHijosAsync(GuardarHijosRequest req, CancellationToken ct = default);
}
