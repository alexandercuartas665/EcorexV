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

// ---- Ola 13: comparador de cotizaciones de proveedor ----

/// <summary>Una oferta/cotizacion de un proveedor para un producto, con nombre resuelto y la variacion vs
/// el ultimo precio del historico de ESE proveedor para el MISMO producto (sube/baja %).</summary>
public sealed record CotizacionDto(
    Guid Id,
    Guid ProveedorId,
    string? ProveedorNombre,
    decimal PrecioUnitario,
    string TiempoEntrega,
    string CaracteristicasDespacho,
    string CondicionesPago,
    DateTimeOffset? VigenciaHasta,
    string? Notas,
    RequerimientoCotizacionFuente Fuente,
    Guid? ProveedorContactoId,
    decimal? UltimoPrecio,
    decimal? VariacionPct,
    DateTimeOffset? FechaCotizacion = null,
    string? ContactoNombre = null,
    string? ContactoTelefono = null,
    string? ContactoCorreo = null,
    // Dias que sostiene su cotizacion (resuelto: regla mas especifica que aplique, si no el default del
    // proveedor). Null = sin vigencia configurada. La tarjeta lo usa para marcar "por vencer"/"vencida".
    int? VigenciaDias = null);

/// <summary>Alta/edicion de una oferta (Id null = alta). Identifica el producto por item o por descripcion.</summary>
public sealed record SaveCotizacionRequest(
    Guid? Id,
    Guid? ItemId,
    string Descripcion,
    Guid ProveedorId,
    decimal PrecioUnitario,
    string? TiempoEntrega,
    string? CaracteristicasDespacho,
    string? CondicionesPago,
    DateTimeOffset? VigenciaHasta,
    string? Notas,
    Guid? ProveedorContactoId = null,
    DateTimeOffset? FechaCotizacion = null,
    // Procedencia de la oferta. Default Manual (captura humana); la Ola C pasa Rfq cuando entra por la
    // respuesta del proveedor a una Solicitud de Cotizacion.
    RequerimientoCotizacionFuente Fuente = RequerimientoCotizacionFuente.Manual);

/// <summary>Una linea de una Solicitud de Cotizacion (RFQ): la distribucion que se le pide al proveedor, con
/// su snapshot de lo pedido y (cuando responda) los campos de oferta.</summary>
public sealed record RfqLineaDto(
    Guid Id,
    Guid? HijoId,
    string Descripcion,
    decimal Cantidad,
    string UnidadMedida,
    DateTimeOffset? FechaRequerida,
    decimal? PrecioUnitario,
    int? VigenciaDias,
    string? LeadTime,
    string? Marca,
    string? Nota);

/// <summary>Una Solicitud de Cotizacion (RFQ) a un proveedor, con su numero, estado, tarea portadora y lineas.</summary>
public sealed record RfqDto(
    Guid Id,
    string Numero,
    Guid ProveedorId,
    string? ProveedorNombre,
    RfqEstado Estado,
    Guid? TaskItemId,
    string? TaskNumber,
    DateTimeOffset? FechaEnvio,
    DateTimeOffset CreatedAt,
    int LineasCount,
    IReadOnlyList<RfqLineaDto> Lineas);

/// <summary>Pide cotizar un conjunto de distribuciones (hijos) AGRUPANDOLAS por proveedor: crea una RFQ por
/// proveedor con todas sus lineas y su tarea portadora (que dispara el flujo). Idempotente por hijo.</summary>
public sealed record CotizarPorProveedorRequest(IReadOnlyList<Guid> HijoIds);

/// <summary>Resultado de cotizar por proveedor: cuantas RFQ se crearon, lineas incluidas, omitidas y avisos.</summary>
public sealed record CotizarPorProveedorResultado(
    bool Ok,
    string? Error,
    int RfqCreados,
    int LineasIncluidas,
    int Omitidas,
    IReadOnlyList<string> Avisos,
    IReadOnlyList<RfqDto> Rfqs);

/// <summary>Ola E - multi-candidato: cotiza el MISMO producto (el de la necesidad <see cref="RequerimientoId"/>) a
/// VARIOS proveedores a la vez. Crea 1 RFQ por proveedor, cada una con una sola linea del producto (SIN
/// distribucion: la adjudicacion se hace despues en el planificador). <see cref="Cantidad"/> null = cantidad
/// pendiente (sin planificar) de la necesidad.</summary>
public sealed record CotizarCandidatosRequest(
    Guid RequerimientoId,
    IReadOnlyList<Guid> ProveedorIds,
    decimal? Cantidad = null);

/// <summary>Resultado de cotizar a varios candidatos.</summary>
public sealed record CotizarCandidatosResultado(
    bool Ok,
    string? Error,
    int RfqCreados,
    int Omitidas,
    IReadOnlyList<string> Avisos,
    IReadOnlyList<RfqDto> Rfqs);

/// <summary>Ola F - un proveedor resuelto desde la MATRIZ para un producto: proveedor + su proceso de
/// cotizacion (flujo/subcategoria). La regla mas especifica que aplica al producto puede traer VARIOS
/// proveedores; el fan-out crea una RFQ candidata a cada uno y es el FLUJO de cada proceso quien envia.</summary>
public sealed record ReglaProveedorResueltoDto(
    Guid ProveedorId,
    string ProveedorNombre,
    Guid? SubcategoriaCotizacionId,
    string? SubcategoriaNombre);

/// <summary>Una entrada del ranking que devuelve el agente evaluador.</summary>
public sealed record CotizacionRankItem(Guid ProveedorId, int Posicion, string Motivo);

/// <summary>Resultado de la evaluacion por IA de las ofertas de un producto: mejor opcion + ranking + resumen.</summary>
public sealed record EvaluacionCotizacionesDto(
    bool Ok,
    string? Error,
    Guid? ProveedorGanadorId,
    string Resumen,
    IReadOnlyList<CotizacionRankItem> Ranking);

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

    // ---- Cotizacion gobernada por flujo (RFQ): Ola A ----
    /// <summary>Cotiza por PROVEEDOR: toma las distribuciones (hijos) indicadas, las AGRUPA por su proveedor y
    /// crea una Solicitud de Cotizacion (RFQ) por proveedor con todas sus lineas y una tarea portadora (cuya
    /// subcategoria de cotizacion dispara el flujo). Idempotente: salta hijos que ya esten en una RFQ abierta
    /// y los que no tengan proveedor. Devuelve el resumen + las RFQ creadas.</summary>
    Task<CotizarPorProveedorResultado> CotizarPorProveedorAsync(CotizarPorProveedorRequest req, Guid actorUserId, string actorName, CancellationToken ct = default);

    /// <summary>Ola E - multi-candidato: cotiza el MISMO producto de una necesidad a VARIOS proveedores a la vez.
    /// Crea 1 RFQ por proveedor con una sola linea del producto, SIN comprometer distribucion (la adjudicacion se
    /// hace despues en el planificador). Reusa la precedencia de proceso (Ola D) y la inyeccion del formulario
    /// (Ola B); las respuestas caen todas en el MISMO comparador por clave de producto. Idempotente por
    /// (necesidad, proveedor) con RFQ abierta.</summary>
    Task<CotizarCandidatosResultado> CotizarCandidatosAsync(CotizarCandidatosRequest req, Guid actorUserId, string actorName, CancellationToken ct = default);

    /// <summary>Ola F - resuelve, desde la MATRIZ de proveedores, los proveedores (con su proceso de cotizacion)
    /// que aplican al PRODUCTO de la necesidad indicada: la regla mas especifica que aplica puede tener VARIOS
    /// proveedores. Para pre-mostrar el fan-out en el planificador. Vacio si el producto no tiene item o no hay
    /// regla que aplique.</summary>
    Task<IReadOnlyList<ReglaProveedorResueltoDto>> GetProveedoresMatrizParaProductoAsync(Guid requerimientoId, CancellationToken ct = default);

    /// <summary>Ola F - fan-out desde la matriz: crea una RFQ CANDIDATA (Ola E) a CADA proveedor de la regla mas
    /// especifica que aplica al producto de la necesidad, usando el proceso de cotizacion de cada proveedor (si lo
    /// tiene en la regla; si no, cae a la precedencia del modulo). Idempotente (omite proveedores con RFQ abierta).
    /// El envio al proveedor lo hace el FLUJO de cada proceso (el modulo solo lo dispara al crear la tarea).</summary>
    Task<CotizarCandidatosResultado> CotizarSegunMatrizAsync(Guid requerimientoId, Guid actorUserId, string actorName, CancellationToken ct = default);

    /// <summary>Lista las Solicitudes de Cotizacion (RFQ) recientes del tenant (numero, proveedor, estado,
    /// tarea portadora y lineas). Para ver lo cotizado desde el modulo.</summary>
    Task<IReadOnlyList<RfqDto>> ListRfqAsync(int max = 100, CancellationToken ct = default);

    /// <summary>Lista las RFQ que incluyen el PRODUCTO indicado (por su item/descripcion, empatado por la clave
    /// del comparador), con su estado y oferta. Para el panel "estado RFQ" del planificador (Ola E).</summary>
    Task<IReadOnlyList<RfqDto>> ListRfqPorProductoAsync(Guid? itemId, string? descripcion, CancellationToken ct = default);

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

    // ---- Ola 13: comparador de cotizaciones de proveedor ----
    /// <summary>Ofertas de proveedor registradas para un producto (item o descripcion), con la variacion de
    /// cada una vs el ultimo precio del historico de ese proveedor. Ordenadas por precio ascendente.</summary>
    Task<IReadOnlyList<CotizacionDto>> ListCotizacionesAsync(Guid? itemId, string descripcion, decimal cantidad, CancellationToken ct = default);
    /// <summary>Crea/actualiza una oferta de proveedor (upsert por producto+proveedor). Devuelve la lista refrescada.</summary>
    Task<IReadOnlyList<CotizacionDto>> GuardarCotizacionAsync(SaveCotizacionRequest req, decimal cantidad, CancellationToken ct = default);
    /// <summary>Elimina una oferta.</summary>
    Task<bool> EliminarCotizacionAsync(Guid id, CancellationToken ct = default);
    /// <summary>Siembra ofertas para el producto desde la matriz de proveedores + el ultimo precio del
    /// historico (idempotente: no duplica proveedores que ya tengan oferta). Devuelve la lista refrescada.</summary>
    Task<IReadOnlyList<CotizacionDto>> SembrarCotizacionesAsync(Guid? itemId, string descripcion, decimal cantidad, CancellationToken ct = default);
    /// <summary>Evalua las ofertas con el agente configurado en el modulo (modulo de agentes) y propone la
    /// mejor opcion + ranking + resumen. Si no hay agente configurado o no hay ofertas, devuelve Ok=false.</summary>
    Task<EvaluacionCotizacionesDto> EvaluarCotizacionesAsync(Guid? itemId, string descripcion, decimal cantidad, CancellationToken ct = default);
}
