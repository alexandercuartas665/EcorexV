using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

// =====================================================================================================
// Modulo Requerimientos y Compras (Capa 10) - Ola A del proceso de cotizacion gobernado (RFQ).
// Una SOLICITUD DE COTIZACION (RFQ) agrupa en UNA unidad todas las lineas que se le piden a UN proveedor
// (un proveedor no quiere N solicitudes separadas). Es el agregado que da trazabilidad (a quien pedimos,
// cuando, que respondio) e idempotencia (no mandar dos veces). Corre sobre el motor de flujos preexistente:
// la RFQ tiene una TAREA PORTADORA cuya subcategoria dispara el flujo; el formulario /f inyectado y el
// retorno por clave llegan en olas posteriores. La adjudicacion NO vive aqui: cuando llegan los valores,
// el jefe de compras arma la compra en planeacion (el proveedor/precio del hijo es el "gano"). Tenant-scoped.
// =====================================================================================================

/// <summary>Una Solicitud de Cotizacion (RFQ) a UN proveedor, con N lineas. Lleva su consecutivo
/// (<see cref="Numero"/> = "RFQ-xxxx"), la tarea portadora que corre el flujo (<see cref="TaskItemId"/>),
/// la instancia del flujo y la respuesta de formulario (olas posteriores) y su estado de ciclo.</summary>
public sealed class RequerimientoRfq : TenantEntity
{
    /// <summary>Consecutivo por tenant, formato "RFQ-0001".</summary>
    public string Numero { get; set; } = string.Empty;
    /// <summary>Proveedor (Tercero del Directorio General) al que se le pide la cotizacion.</summary>
    public Guid ProveedorId { get; set; }
    /// <summary>Contacto especifico del proveedor (TerceroContacto) al que se le envia. Null = datos del proveedor.</summary>
    public Guid? ProveedorContactoId { get; set; }
    /// <summary>Tarea (TaskItem) portadora que corre el flujo de cotizacion de esta RFQ.</summary>
    public Guid? TaskItemId { get; set; }
    /// <summary>Instancia del flujo (workflow_instances) de la tarea portadora, si aplica.</summary>
    public Guid? WorkflowInstanceId { get; set; }
    /// <summary>Respuesta del formulario RFQ (FormResponse) enviado al proveedor. Se llena en la Ola B.</summary>
    public Guid? FormResponseId { get; set; }
    public RfqEstado Estado { get; set; } = RfqEstado.Borrador;
    /// <summary>Cuando salio al proveedor.</summary>
    public DateTimeOffset? FechaEnvio { get; set; }
    /// <summary>Cuando respondio el proveedor.</summary>
    public DateTimeOffset? FechaRespuesta { get; set; }
    /// <summary>Hasta cuando es valida la oferta recibida (vigencia). Null si aun no responde.</summary>
    public DateTimeOffset? VigenciaHasta { get; set; }
    // ---- Trazabilidad: quien solicito la cotizacion ----
    public Guid? SolicitanteUserId { get; set; }
    public string? SolicitanteNombre { get; set; }
    public string? Notas { get; set; }

    public ICollection<RequerimientoRfqLinea> Lineas { get; set; } = new List<RequerimientoRfqLinea>();
}

/// <summary>Una linea de una RFQ: la distribucion (hijo) que se le pide a ese proveedor. Lleva la
/// <see cref="ClaveProducto"/> (misma clave del comparador) para empatar exacto la ida (inyeccion al
/// formulario) y la vuelta (respuesta del proveedor), sin depender del texto. Los campos de respuesta
/// (<see cref="PrecioUnitario"/> ...) se llenan cuando el proveedor contesta (Ola C).</summary>
public sealed class RequerimientoRfqLinea : TenantEntity
{
    public Guid RfqId { get; set; }
    public RequerimientoRfq? Rfq { get; set; }
    /// <summary>Distribucion (RequerimientoCompraHijo) de origen que se esta cotizando. NULL en una RFQ de
    /// CANDIDATO (Ola E): se cotiza el PRODUCTO a varios proveedores sin comprometer una distribucion; la
    /// adjudicacion se hace despues en el planificador asignando el proveedor/precio ganador al hijo.</summary>
    public Guid? RequerimientoCompraHijoId { get; set; }
    /// <summary>Necesidad padre (RequerimientoCompra) de la distribucion, para navegar sin join adicional.</summary>
    public Guid RequerimientoId { get; set; }
    /// <summary>Clave del producto: "i:{ItemId}" o "d:{descripcion normalizada}". IGUAL que RequerimientoCotizacion.</summary>
    public string ClaveProducto { get; set; } = string.Empty;
    public Guid? ItemId { get; set; }
    // ---- Snapshot de lo que se pide (al crear la RFQ) ----
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = string.Empty;
    public DateTimeOffset? FechaRequerida { get; set; }
    // ---- Respuesta del proveedor (Ola C) ----
    public decimal? PrecioUnitario { get; set; }
    public int? VigenciaDias { get; set; }
    public string? LeadTime { get; set; }
    public string? Marca { get; set; }
    public string? Nota { get; set; }
}
