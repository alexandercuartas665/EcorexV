using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

// =====================================================================================================
// FASE 1 - FUENTES DUMMY (stand-in). Estas 3 tablas simulan, DENTRO de ECOREX y con datos dummy, las
// fuentes que en el molde legacy son EXTERNAS: el bot DIAN (BOT_DIAN_RECIBIDOS), la plataforma NEWTON
// (BOT_NEWTON_RECIBIDOS) y el ERP (MOV de COMPRAS/INVEN/PROVE). Sirven para ejercitar la logica REAL de
// Importar + Cruzar en Fase 1. En Fase 2 se reemplazan por consultas a la conexion externa (ADR-0084) y
// estas tablas se pueden retirar. Son tenant-scoped para poder sembrar por tenant sin cruzar datos.
// =====================================================================================================

/// <summary>DUMMY (Fase 1): factura reportada a la DIAN por el proveedor. Espeja BOT_DIAN_RECIBIDOS.</summary>
public sealed class ConciliacionDianBotDummy : TenantEntity
{
    public string Cufe { get; set; } = string.Empty;
    public string TipoDoc { get; set; } = string.Empty;
    public DateTimeOffset FechaDoc { get; set; }
    public string NombreEmisor { get; set; } = string.Empty;
    public string NitEmisor { get; set; } = string.Empty;
    /// <summary>Numero de factura del proveedor (PREFIJO_FOLIO en el molde).</summary>
    public string PrefijoFolio { get; set; } = string.Empty;
    /// <summary>Identificador de compra que el bot ya trae; se copia a OrdenCompraSoldarco al importar.</summary>
    public string IdCompra { get; set; } = string.Empty;
    public decimal SubtotalBruto { get; set; }
    public decimal DescuentoComercial { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal TotalAntesRet { get; set; }
    public decimal RetencionFuente { get; set; }
    public decimal RetencionIca { get; set; }
    public decimal Total { get; set; }
    public string TipoPago { get; set; } = string.Empty;
    public string ProveedorTecnologico { get; set; } = string.Empty;
}

/// <summary>DUMMY (Fase 1): registro de la plataforma NEWTON. Espeja BOT_NEWTON_RECIBIDOS. Si tiene GuidPdf
/// hay representacion grafica (habilita PlataformaProveedor); EventId es la llave para radicar eventos.</summary>
public sealed class ConciliacionDianNewtonDummy : TenantEntity
{
    public string Cufe { get; set; } = string.Empty;
    public string GuidPdf { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
}

/// <summary>DUMMY (Fase 1): documento interno del ERP para el cruce. Espeja las referencias unificadas de
/// COMPRAS/INVEN/PROVE MOV. <see cref="Referencia"/> es el numero de factura (sin guiones) contra el que se
/// empareja; <see cref="DocumentoInterno"/> es el numero interno que se escribe en NumFacturaSoldarco.</summary>
public sealed class ConciliacionDianErpRefDummy : TenantEntity
{
    /// <summary>Numero de factura del proveedor SIN guiones (llave de cruce).</summary>
    public string Referencia { get; set; } = string.Empty;
    /// <summary>Numero de documento interno del ERP (lo que se asigna como NumFacturaSoldarco).</summary>
    public string DocumentoInterno { get; set; } = string.Empty;
}
