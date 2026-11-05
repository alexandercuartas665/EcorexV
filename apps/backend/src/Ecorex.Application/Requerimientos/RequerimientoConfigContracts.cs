using Ecorex.Domain.Enums;

namespace Ecorex.Application.Requerimientos;

/// <summary>Opcion generica para los selectores de la config (subcategorias, formularios...).</summary>
public sealed record RequerimientoOpcionDto(Guid Id, string Nombre);

/// <summary>Un origen de requerimientos configurado (con nombres resueltos para mostrar).</summary>
public sealed record RequerimientoOrigenDto(
    Guid Id,
    string Nombre,
    string Area,
    RequerimientoFuenteTipo TipoFuente,
    Guid? FuenteRefId,
    string? FuenteNombre,
    Guid? SubcategoriaCompraId,
    string? SubcategoriaNombre,
    Guid? SubcategoriaCotizacionId,
    string? SubcategoriaCotizacionNombre,
    bool Activo);

/// <summary>Alta/edicion de un origen. Id null = alta.</summary>
public sealed record SaveRequerimientoOrigenRequest(
    Guid? Id,
    string Nombre,
    string Area,
    RequerimientoFuenteTipo TipoFuente,
    Guid? FuenteRefId,
    Guid? SubcategoriaCompraId,
    Guid? SubcategoriaCotizacionId,
    bool Activo);

/// <summary>Una condicion de una regla: dimension + valor (Guid del catalogo de inventario como texto).
/// Las condiciones de una regla se combinan con AND (la regla aplica si el item cumple TODAS).</summary>
public sealed record ReglaCondicion(RequerimientoDimension Dimension, string Valor);

/// <summary>Condicion de una regla con el nombre del valor ya resuelto (para mostrar).</summary>
public sealed record ReglaCondicionResueltaDto(RequerimientoDimension Dimension, string Valor, string? ValorNombre);

/// <summary>Una fila de la matriz de proveedores (con nombres resueltos para mostrar). Una regla puede
/// COMBINAR varias dimensiones (<see cref="Condiciones"/>, AND); Dimension/ValorDimension/ValorNombre son la
/// primera condicion (compatibilidad). <see cref="VigenciaDias"/> = dias que sostiene una cotizacion por esta
/// regla (gana sobre el default del proveedor); null = usar el del proveedor.</summary>
public sealed record RequerimientoReglaDto(
    Guid Id,
    RequerimientoDimension Dimension,
    string ValorDimension,
    string? ValorNombre,
    Guid ProveedorId,
    string? ProveedorNombre,
    int Prioridad,
    bool Aprendida,
    bool Activo,
    IReadOnlyList<ReglaCondicionResueltaDto> Condiciones,
    int? VigenciaDias,
    // Proceso de cotizacion (flujo/subcategoria) de ESTE proveedor dentro de la regla. Null = usa la
    // precedencia del modulo (producto > proveedor global > origen > default). Una misma regla (mismo juego
    // de condiciones) puede tener VARIOS proveedores, cada uno su proceso: una fila por proveedor.
    Guid? SubcategoriaCotizacionId = null,
    string? SubcategoriaNombre = null,
    // Proceso de COMPRA (flujo/subcategoria que corre al Ejecutar la compra) de ESTE proveedor en la regla.
    // Null = usa la precedencia del modulo (matriz > origen > default de compra). Espejo del de cotizacion.
    Guid? SubcategoriaCompraId = null,
    string? SubcategoriaCompraNombre = null);

/// <summary>Alta/edicion de una regla de la matriz. Id null = alta. <see cref="Condiciones"/> debe traer al
/// menos una (AND entre todas). <see cref="VigenciaDias"/> null = hereda del proveedor.
/// <see cref="SubcategoriaCotizacionId"/> = proceso de cotizacion de ESTE proveedor en la regla (null = default).</summary>
public sealed record SaveReglaRequest(
    Guid? Id,
    IReadOnlyList<ReglaCondicion> Condiciones,
    Guid ProveedorId,
    int Prioridad,
    bool Activo,
    int? VigenciaDias,
    Guid? SubcategoriaCotizacionId = null,
    // Proceso de COMPRA de ESTE proveedor en la regla (null = default). Espejo de SubcategoriaCotizacionId.
    Guid? SubcategoriaCompraId = null);

/// <summary>Dias de vigencia de cotizacion por proveedor (default del proveedor; una regla puede sobreescribirlo).</summary>
public sealed record ProveedorVigenciaDto(Guid ProveedorId, int? VigenciaDias);

/// <summary>Mapeo del FORMULARIO de cotizacion (RFQ) para inyectar las lineas al proveedor (Ola B). El flujo de la
/// subcategoria de cotizacion presenta este formulario; su campo GRILLA (<see cref="GridFieldCode"/>) recibe una
/// fila por linea, con la CLAVE tecnica por fila en <see cref="ColClave"/> (columna OCULTA) para empatar la
/// respuesta en la Ola C. <see cref="ColDescripcion"/>/<see cref="ColCantidad"/>/<see cref="ColUnidad"/>/
/// <see cref="ColFecha"/> reciben el snapshot de la linea (lo que se cotiza). Todo opcional: sin definicion o sin
/// grilla no se inyecta nada (la RFQ se crea igual). Config por tenant en TenantConfiguration (sin migracion).
/// Las columnas de RESPUESTA (<see cref="ColPrecio"/>/<see cref="ColVigencia"/>/<see cref="ColLeadTime"/>/
/// <see cref="ColMarca"/>/<see cref="ColNota"/>) las llena el proveedor y la Ola C las empata por clave para
/// escribir la linea del RFQ y volcar la oferta al comparador (<see cref="ColPrecio"/> es la unica imprescindible).</summary>
public sealed record RfqFormMapDto(
    Guid? FormDefinitionId, string? GridFieldCode,
    string? ColClave, string? ColDescripcion, string? ColCantidad, string? ColUnidad, string? ColFecha,
    string? ColPrecio = null, string? ColVigencia = null, string? ColLeadTime = null,
    string? ColMarca = null, string? ColNota = null);

/// <summary>Una columna de una grilla (GridDetail), para los selectores del mapeo RFQ. <see cref="Hidden"/> marca
/// las columnas ocultas (candidatas naturales para la clave tecnica).</summary>
public sealed record RfqGridColDto(string Id, string Label, bool Hidden);

/// <summary>Un campo GRILLA (GridDetail) de un formulario con sus columnas, para elegir cual recibe las lineas.</summary>
public sealed record RfqGridFieldDto(string FieldCode, string Label, IReadOnlyList<RfqGridColDto> Columns);

/// <summary>
/// Configuracion del modulo de Requerimientos (Capa 10): ORIGENES (Ola 1) + MATRIZ de proveedores (Ola 4).
/// Tenant-scoped por el filtro global.
/// </summary>
public interface IRequerimientoConfigService
{
    Task<IReadOnlyList<RequerimientoOrigenDto>> ListOrigenesAsync(CancellationToken ct = default);
    Task<RequerimientoOrigenDto?> SaveOrigenAsync(SaveRequerimientoOrigenRequest req, CancellationToken ct = default);
    Task<bool> DeleteOrigenAsync(Guid id, CancellationToken ct = default);

    /// <summary>Modo de trabajo del modulo (Consolidado/Dividido). Config por tenant, se guarda en
    /// TenantConfiguration (clave/valor). Default Consolidado si no hay nada guardado.</summary>
    Task<RequerimientoModo> GetModoAsync(CancellationToken ct = default);
    /// <summary>Guarda el modo de trabajo del modulo para el tenant.</summary>
    Task SetModoAsync(RequerimientoModo modo, CancellationToken ct = default);

    /// <summary>Agente (del modulo de agentes) que evalua las cotizaciones del comparador de compras. Se
    /// guarda en TenantConfiguration. Null si no hay uno elegido.</summary>
    Task<Guid?> GetAgenteCotizacionesAsync(CancellationToken ct = default);
    /// <summary>Elige el agente evaluador de cotizaciones (null para quitarlo).</summary>
    Task SetAgenteCotizacionesAsync(Guid? agentId, CancellationToken ct = default);
    /// <summary>Agentes ACTIVOS del modulo de agentes para el selector de la config.</summary>
    Task<IReadOnlyList<RequerimientoOpcionDto>> ListAgentesAsync(CancellationToken ct = default);

    /// <summary>Catalogo de unidades de medida del modulo (p.ej. Unidad, Caja, Kilogramo). Config por tenant
    /// en TenantConfiguration. Si el tenant no ha guardado ninguna, devuelve las comunes por defecto
    /// (<see cref="RequerimientoUnidades.Comunes"/>).</summary>
    Task<IReadOnlyList<string>> GetUnidadesAsync(CancellationToken ct = default);
    /// <summary>Guarda el catalogo de unidades de medida del tenant (se normaliza: recorta, descarta vacias y
    /// deduplica conservando el orden). Lista vacia = vuelve a las unidades comunes por defecto.</summary>
    Task SetUnidadesAsync(IEnumerable<string> unidades, CancellationToken ct = default);

    /// <summary>Subcategorias (conceptos 000270) para elegir el proceso de compra del origen.</summary>
    Task<IReadOnlyList<RequerimientoOpcionDto>> ListSubcategoriasAsync(CancellationToken ct = default);
    /// <summary>Formularios publicados/activos para elegir la fuente (TipoFuente=Formulario).</summary>
    Task<IReadOnlyList<RequerimientoOpcionDto>> ListFormulariosAsync(CancellationToken ct = default);

    // ---- Ola 4: matriz de proveedores por dimension del item ----
    Task<IReadOnlyList<RequerimientoReglaDto>> ListReglasAsync(CancellationToken ct = default);
    Task<RequerimientoReglaDto?> SaveReglaAsync(SaveReglaRequest req, CancellationToken ct = default);
    Task<bool> DeleteReglaAsync(Guid id, CancellationToken ct = default);
    /// <summary>Proveedores del Directorio General (Tercero) para el selector.</summary>
    Task<IReadOnlyList<RequerimientoOpcionDto>> ListProveedoresAsync(CancellationToken ct = default);
    /// <summary>Valores posibles de una dimension (catalogo de inventario: tipo/grupo/subgrupo/marca/item).</summary>
    Task<IReadOnlyList<RequerimientoOpcionDto>> ListValoresDimensionAsync(RequerimientoDimension dimension, CancellationToken ct = default);

    /// <summary>Dias de vigencia de cotizacion por proveedor (default del proveedor). Mapa proveedorId -> dias.
    /// Config por tenant en TenantConfiguration (sin migracion). Solo incluye proveedores con valor definido.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetProveedorVigenciaMapAsync(CancellationToken ct = default);
    /// <summary>Fija (o limpia, con null) los dias de vigencia default de un proveedor.</summary>
    Task SetProveedorVigenciaAsync(Guid proveedorId, int? dias, CancellationToken ct = default);

    // ---- Ola D: precedencia del PROCESO de cotizacion (subcategoria/flujo que corre la RFQ) ----
    // Cadena de resolucion al "Cotizar por proveedor": producto (en planificacion) > matriz del proveedor >
    // origen de la linea (comportamiento previo) > default del modulo. Las tres capas nuevas viven en
    // TenantConfiguration (sin migracion).

    /// <summary>Proceso de cotizacion por DEFECTO del modulo (ultimo recurso de la cadena). Null si no se fijo.</summary>
    Task<Guid?> GetCotizacionDefaultAsync(CancellationToken ct = default);
    /// <summary>Fija (o limpia, con null) el proceso de cotizacion por defecto del modulo.</summary>
    Task SetCotizacionDefaultAsync(Guid? subcategoriaId, CancellationToken ct = default);

    /// <summary>Proceso de COMPRA por DEFECTO del modulo (ultimo recurso al Ejecutar la compra). La cadena del
    /// planificador es: matriz del proveedor (regla) > origen de la necesidad > este default. Null si no se fijo.</summary>
    Task<Guid?> GetCompraDefaultAsync(CancellationToken ct = default);
    /// <summary>Fija (o limpia, con null) el proceso de compra por defecto del modulo.</summary>
    Task SetCompraDefaultAsync(Guid? subcategoriaId, CancellationToken ct = default);

    /// <summary>Proceso de cotizacion fijado por PROVEEDOR (capa "matriz"). Mapa proveedorId -> subcategoria.
    /// Solo incluye proveedores con valor definido.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetProveedorProcesoMapAsync(CancellationToken ct = default);
    /// <summary>Fija (o limpia, con null) el proceso de cotizacion de un proveedor.</summary>
    Task SetProveedorProcesoAsync(Guid proveedorId, Guid? subcategoriaId, CancellationToken ct = default);

    /// <summary>Proceso de cotizacion fijado por PRODUCTO (capa mas especifica, "producto en planificacion").
    /// Mapa claveProducto -> subcategoria. La clave es la misma del comparador (<see cref="RequerimientoClave"/>).</summary>
    Task<IReadOnlyDictionary<string, Guid>> GetProductoProcesoMapAsync(CancellationToken ct = default);
    /// <summary>Lee el proceso de cotizacion fijado para un producto (por su item/descripcion). Null si no hay.</summary>
    Task<Guid?> GetProductoProcesoAsync(Guid? itemId, string? descripcion, CancellationToken ct = default);
    /// <summary>Fija (o limpia, con null) el proceso de cotizacion de un producto (por su item/descripcion).</summary>
    Task SetProductoProcesoAsync(Guid? itemId, string? descripcion, Guid? subcategoriaId, CancellationToken ct = default);

    // ---- Ola B: formulario de cotizacion (RFQ) ----

    /// <summary>Mapeo del formulario de cotizacion (RFQ) del tenant (Ola B). Nunca null: todo vacio si no se ha
    /// configurado. Config por tenant en TenantConfiguration (sin migracion).</summary>
    Task<RfqFormMapDto> GetRfqFormMapAsync(CancellationToken ct = default);
    /// <summary>Guarda el mapeo del formulario de cotizacion (RFQ) del tenant.</summary>
    Task SetRfqFormMapAsync(RfqFormMapDto map, CancellationToken ct = default);
    /// <summary>Campos GRILLA (GridDetail) de un formulario con sus columnas, para configurar el mapeo RFQ.
    /// Vacio si el formulario no existe o no tiene grillas.</summary>
    Task<IReadOnlyList<RfqGridFieldDto>> ListGridFieldsAsync(Guid definitionId, CancellationToken ct = default);
}
