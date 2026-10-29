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
    decimal CantidadPlanificada,
    decimal MontoTotal,
    IReadOnlyList<RequerimientoHijoDto> Hijos,
    // Trazabilidad: quien pidio y desde que actividad.
    Guid? SolicitanteUserId = null,
    string? SolicitanteNombre = null,
    Guid? SourceTaskItemId = null,
    string? SourceTaskNumber = null,
    // Override por registro: sacar esta necesidad de la consolidacion (se planifica aparte).
    bool ForzarDividir = false);

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
    DateTimeOffset? FechaNecesidad,
    // Trazabilidad opcional: quien pidio (TenantUser) y la actividad origen. El nombre se resuelve si va vacio.
    Guid? SolicitanteUserId = null,
    string? SolicitanteNombre = null,
    Guid? SourceTaskItemId = null,
    // Observaciones opcionales (las puede alimentar el mapeo de una regla de formulario).
    string? Observaciones = null,
    string? ObservacionesDespacho = null);

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
    string? ProveedorNombre,
    RequerimientoEstado Estado,
    Guid? TaskItemId,
    decimal PrecioUnitario,
    Guid? CotizacionTaskItemId = null,
    Guid? ProveedorContactoId = null)
{
    /// <summary>Total del hijo = Cantidad * PrecioUnitario (Ola 6).</summary>
    public decimal Total => Cantidad * PrecioUnitario;
}

/// <summary>Un contacto de un proveedor (TerceroContacto) para elegir/editar desde Requerimientos (Ola 11).</summary>
public sealed record ProveedorContactoItemDto(Guid Id, string Nombre, string? Cargo, string? Email, string? Telefono);

/// <summary>Alta/edicion de un contacto de proveedor (Id null = nuevo).</summary>
public sealed record ContactoInput(Guid? Id, string Nombre, string? Cargo, string? Email, string? Telefono);

/// <summary>Datos de contacto de un proveedor (Tercero) para el autocompletado del modal: nombre del
/// contacto + correo + telefono. Prefiere el contacto embebido; cae a los datos del propio proveedor.</summary>
public sealed record ProveedorContactoDto(
    Guid Id,
    string Nombre,
    string? Contacto,
    string? Email,
    string? Telefono);

/// <summary>Ultimo precio unitario registrado para un producto+proveedor en planificaciones anteriores
/// (Ola 10, "ultimo precio cotizado" del historico: no captura nada nuevo, lee los hijos ya registrados).</summary>
public sealed record UltimoPrecioDto(decimal Precio, DateTimeOffset? Fecha);

/// <summary>Entrada para crear/editar un hijo (Id null = nuevo).</summary>
public sealed record HijoInput(
    Guid? Id,
    decimal Cantidad,
    DateTimeOffset? FechaProgramadaCompra,
    string? CaracteristicasDespacho,
    string? TiempoEntrega,
    Guid? ProveedorId,
    decimal PrecioUnitario,
    Guid? ProveedorContactoId = null);

/// <summary>Guarda TODA la planificacion (hijos) de una necesidad de una vez (reemplaza el conjunto).</summary>
public sealed record GuardarHijosRequest(Guid RequerimientoId, IReadOnlyList<HijoInput> Hijos);

/// <summary>Una linea del plan de grupo: una necesidad del producto consolidado + la cantidad a planificar en esta pasada.</summary>
public sealed record PlanificarGrupoLinea(Guid RequerimientoId, decimal Cantidad);

/// <summary>Planifica VARIAS necesidades del mismo producto (vista consolidada) de una vez: AGREGA una
/// distribucion (hijo) a cada una con el mismo proveedor/precio/fecha/despacho (no toca las ya existentes).</summary>
public sealed record PlanificarGrupoRequest(
    IReadOnlyList<PlanificarGrupoLinea> Lineas,
    Guid? ProveedorId,
    Guid? ProveedorContactoId,
    decimal PrecioUnitario,
    DateTimeOffset? FechaProgramadaCompra,
    string? CaracteristicasDespacho,
    string? TiempoEntrega);

/// <summary>Resultado del plan de grupo.</summary>
public sealed record PlanificarGrupoResult(bool Ok, string? Error, int HijosCreados);

/// <summary>Estado de la planificacion de una necesidad: cantidades y sus hijos.</summary>
public sealed record PlanificacionDto(
    Guid RequerimientoId,
    string Producto,
    decimal CantidadPadre,
    decimal CantidadPlanificada,
    decimal CantidadSinPlanificar,
    IReadOnlyList<RequerimientoHijoDto> Hijos);

/// <summary>Resultado de ejecutar una compra (Ola 5): la actividad creada + la planificacion refrescada.</summary>
public sealed record EjecutarCompraResultado(
    bool Ok,
    string? Error,
    Guid? TaskItemId,
    string? TaskNumber,
    PlanificacionDto? Plan);

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

    // ---- Ola 6: ciclo de vida (cerrar/reabrir) ----
    /// <summary>Cierra una necesidad Ejecutada (compra terminada): Ejecutado -> Cerrado.</summary>
    Task<RequerimientoDto?> CerrarAsync(Guid id, CancellationToken ct = default);
    /// <summary>Reabre una necesidad Cerrada: Cerrado -> Ejecutado.</summary>
    Task<RequerimientoDto?> ReabrirAsync(Guid id, CancellationToken ct = default);

    /// <summary>Marca/desmarca una necesidad como "dividir" (override por registro): si es true, no se
    /// consolida con las del mismo producto aunque el modulo este en modo Consolidado.</summary>
    Task<RequerimientoDto?> SetForzarDividirAsync(Guid id, bool dividir, CancellationToken ct = default);

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

    /// <summary>Planifica de una sola vez varias necesidades del mismo producto (vista consolidada): AGREGA
    /// una distribucion a cada una con proveedor/precio/fecha compartidos, sin tocar las existentes, validando
    /// el restante de cada necesidad. Transaccional (todo o nada).</summary>
    Task<PlanificarGrupoResult> PlanificarGrupoAsync(PlanificarGrupoRequest req, CancellationToken ct = default);

    // ---- Ola 4: proveedores ----
    /// <summary>Proveedores del Directorio General para el selector del hijo.</summary>
    Task<IReadOnlyList<RequerimientoOpcionDto>> ListProveedoresAsync(CancellationToken ct = default);
    /// <summary>Sugiere un proveedor para un item segun la matriz: gana la regla mas especifica que aplique
    /// (Referencia > Marca > Subgrupo > Grupo > Categoria; a igual especificidad, por prioridad). Null si ninguna.</summary>
    Task<RequerimientoOpcionDto?> SugerirProveedorAsync(Guid itemId, CancellationToken ct = default);

    // ---- Ola 5: ejecutar compra -> Actividad + aprendizaje ----
    /// <summary>Ejecuta la compra de un registro HIJO: crea una Actividad (TaskItem) con la subcategoria/proceso
    /// del origen, enlaza el TaskItemId al hijo y lo pone en Ejecutado (el padre pasa a Ejecutado cuando TODOS
    /// sus hijos estan ejecutados). Si el hijo tiene proveedor, APRENDE la matriz: graba/actualiza una regla
    /// Aprendida en la dimension MAS especifica del item -> ese proveedor. Idempotente: si el hijo ya tiene
    /// actividad, no crea otra.</summary>
    Task<EjecutarCompraResultado> EjecutarHijoAsync(Guid hijoId, Guid actorUserId, string actorName, CancellationToken ct = default);

    // ---- Ola 8: cotizar + contacto del proveedor ----
    /// <summary>Pide la COTIZACION de un registro HIJO: crea una Actividad (TaskItem) con la subcategoria de
    /// COTIZACION del origen, enlaza el CotizacionTaskItemId al hijo. No cambia el estado de la compra.
    /// Idempotente: si el hijo ya tiene actividad de cotizacion, no crea otra.</summary>
    Task<EjecutarCompraResultado> CotizarHijoAsync(Guid hijoId, Guid actorUserId, string actorName, CancellationToken ct = default);

    /// <summary>Datos de contacto de un proveedor (nombre del contacto + correo + telefono) para el autocompletado.
    /// Si <paramref name="contactoId"/> es null usa el primer contacto; si no, usa ese contacto especifico.</summary>
    Task<ProveedorContactoDto?> GetProveedorContactoAsync(Guid proveedorId, Guid? contactoId, CancellationToken ct = default);

    /// <summary>Sobrecarga sin contacto especifico (usa el primero). Compatibilidad.</summary>
    Task<ProveedorContactoDto?> GetProveedorContactoAsync(Guid proveedorId, CancellationToken ct = default);

    // ---- Ola 11: editar los contactos del proveedor desde Requerimientos ----
    /// <summary>Lista los contactos (TerceroContacto) de un proveedor para elegir/editar.</summary>
    Task<IReadOnlyList<ProveedorContactoItemDto>> ListContactosAsync(Guid proveedorId, CancellationToken ct = default);
    /// <summary>Crea o actualiza un contacto del proveedor (mas telefonos/correos). Devuelve el contacto guardado.</summary>
    Task<ProveedorContactoItemDto> GuardarContactoAsync(Guid proveedorId, ContactoInput input, CancellationToken ct = default);
    /// <summary>Elimina un contacto del proveedor.</summary>
    Task EliminarContactoAsync(Guid contactoId, CancellationToken ct = default);

    // ---- Ola 10: ultimo precio del historico ----
    /// <summary>Ultimo precio unitario (&gt;0) usado para el MISMO producto+proveedor en cualquier hijo previo.
    /// Empareja por ItemId cuando existe; si no, por Descripcion del padre. Excluye la necesidad actual.
    /// Devuelve null si no hay historial. No persiste nada: sirve de referencia al planificar.</summary>
    Task<UltimoPrecioDto?> GetUltimoPrecioAsync(Guid? itemId, string descripcion, Guid proveedorId, Guid excluirRequerimientoId, CancellationToken ct = default);
}
