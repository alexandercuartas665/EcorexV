using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

// =====================================================================================================
// Modulo Requerimientos y Compras (Capa 10). Gestiona necesidades de compra que llegan desde varias
// areas/fuentes, las agrupa por producto, permite planificar la compra con granularidad (dividir en el
// tiempo), elegir proveedor (directorio + matriz) y al ejecutar dispara una actividad del proceso de
// compra que corresponda al origen. Todas tenant-scoped (TenantId + filtro global), migraciones duales.
// =====================================================================================================

/// <summary>CONFIG: un origen/area de requerimientos. El primer campo del modulo es el area solicitante
/// (Proyectos, Insumos de Oficina, Reabastecimiento...). Define de donde llegan las necesidades
/// (<see cref="TipoFuente"/> + <see cref="FuenteRefId"/>) y que proceso de compra las EJECUTA
/// (<see cref="SubcategoriaCompraId"/> = concepto/subcategoria del modulo de Actividades).</summary>
public sealed class RequerimientoOrigen : TenantEntity
{
    public string Nombre { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public RequerimientoFuenteTipo TipoFuente { get; set; } = RequerimientoFuenteTipo.Manual;
    /// <summary>Id del formulario/contenedor que alimenta este origen (segun TipoFuente). Null si Manual.</summary>
    public Guid? FuenteRefId { get; set; }
    /// <summary>Subcategoria (concepto 000270) cuyo flujo/proceso ejecuta la compra de este genero.</summary>
    public Guid? SubcategoriaCompraId { get; set; }
    /// <summary>Subcategoria (concepto 000270) cuyo flujo/proceso pide la COTIZACION de este genero.</summary>
    public Guid? SubcategoriaCotizacionId { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>CONFIG: una fila de la MATRIZ de proveedores. Mapea una dimension del item
/// (categoria/grupo/subgrupo/marca/referencia) a un proveedor del Directorio. Se puede crear a mano o
/// quedar <see cref="Aprendida"/> cuando el sistema la agrega como memoria del ultimo ejercicio de compra.</summary>
public sealed class RequerimientoProveedorRegla : TenantEntity
{
    public RequerimientoDimension Dimension { get; set; } = RequerimientoDimension.Grupo;
    /// <summary>Valor de la dimension (p.ej. el codigo/nombre del grupo, o la referencia del item).</summary>
    public string ValorDimension { get; set; } = string.Empty;
    /// <summary>Proveedor (Tercero del Directorio General).</summary>
    public Guid ProveedorId { get; set; }
    public int Prioridad { get; set; }
    /// <summary>true = la agrego el sistema automaticamente tras una compra (memoria); false = manual.</summary>
    public bool Aprendida { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>OPERATIVO: una necesidad de compra (linea). Llega de un origen (o alta manual), referencia un
/// item del modulo de Inventarios y se agrupa por producto (<see cref="ClaveAgrupacion"/>). Se puede
/// dividir en <see cref="RequerimientoCompraHijo"/> para la granularidad en el tiempo.</summary>
public sealed class RequerimientoCompra : TenantEntity
{
    public Guid? OrigenId { get; set; }
    public RequerimientoOrigen? Origen { get; set; }
    public string Area { get; set; } = string.Empty;
    /// <summary>Item del modulo de Inventarios. Null si es producto libre (segun decision de la Ola 2).</summary>
    public Guid? ItemId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = string.Empty;
    public DateTimeOffset? FechaNecesidad { get; set; }
    public RequerimientoEstado Estado { get; set; } = RequerimientoEstado.Recibido;
    /// <summary>Clave por la que se agrupan los pedidos del MISMO producto (p.ej. ItemId o referencia).</summary>
    public string ClaveAgrupacion { get; set; } = string.Empty;

    // ---- Trazabilidad de la solicitud (quien pidio y desde que actividad) ----
    /// <summary>TenantUser que origino la solicitud (quien envio el formulario). Null si anonimo (/f publico).</summary>
    public Guid? SolicitanteUserId { get; set; }
    /// <summary>Nombre/correo del solicitante, snapshot al crear (para no perderlo si cambia el usuario).</summary>
    public string? SolicitanteNombre { get; set; }
    /// <summary>Actividad (TaskItem) donde se origino la solicitud (el formulario vivia en esa tarea). Null si no aplica.</summary>
    public Guid? SourceTaskItemId { get; set; }

    // ---- Observaciones (texto libre, se pueden alimentar desde el mapeo de una regla de formulario) ----
    /// <summary>Observaciones generales de la necesidad.</summary>
    public string? Observaciones { get; set; }
    /// <summary>Observaciones/caracteristicas de despacho deseadas; sirven de default al planificar el hijo.</summary>
    public string? ObservacionesDespacho { get; set; }

    public ICollection<RequerimientoCompraHijo> Hijos { get; set; } = new List<RequerimientoCompraHijo>();
}

/// <summary>OPERATIVO: la DIVISION/granularidad de una necesidad. Un requerimiento se abre en hijos con
/// cantidad + fecha programada de compra + caracteristicas de despacho/entrega + proveedor. Al EJECUTAR,
/// se crea una actividad (tarea) y se guarda su <see cref="TaskItemId"/>.</summary>
public sealed class RequerimientoCompraHijo : TenantEntity
{
    public Guid RequerimientoId { get; set; }
    public RequerimientoCompra? Requerimiento { get; set; }
    public decimal Cantidad { get; set; }
    public DateTimeOffset? FechaProgramadaCompra { get; set; }
    public string CaracteristicasDespacho { get; set; } = string.Empty;
    public string TiempoEntrega { get; set; } = string.Empty;
    /// <summary>Proveedor elegido (Tercero del Directorio General).</summary>
    public Guid? ProveedorId { get; set; }
    /// <summary>Contacto especifico del proveedor (TerceroContacto) elegido para esta linea (Ola 10/11).
    /// Null = usar el primer contacto / los datos del propio proveedor. Define el Requester de la actividad.</summary>
    public Guid? ProveedorContactoId { get; set; }
    /// <summary>Precio unitario pactado/estimado de la compra (Ola 6). El total del hijo = Cantidad * PrecioUnitario.</summary>
    public decimal PrecioUnitario { get; set; }
    public RequerimientoEstado Estado { get; set; } = RequerimientoEstado.Planificado;
    /// <summary>Actividad (TaskItem) generada al ejecutar la compra de este hijo.</summary>
    public Guid? TaskItemId { get; set; }
    /// <summary>Actividad (TaskItem) generada al pedir la COTIZACION de este hijo (paralela a la compra).</summary>
    public Guid? CotizacionTaskItemId { get; set; }
}
