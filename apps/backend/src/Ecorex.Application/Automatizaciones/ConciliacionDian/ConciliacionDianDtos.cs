using Ecorex.Domain.Enums;

namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

/// <summary>Encabezado CCD (documento mensual) para el selector.</summary>
public sealed record ConciliacionDianDocumentoDto(
    Guid Id, string Consecutivo, int Anio, int Mes, string Estado, DateTimeOffset FechaCreacion);

/// <summary>Renglon conciliable para la grilla.</summary>
public sealed record ConciliacionDianRenglonDto(
    Guid Id,
    EstadoConciliacionDian Estado,
    string TipoDocDian,
    DateTimeOffset FechaEmision,
    string NombreProveedor,
    string NitProveedor,
    string NumFacturaProveedor,
    string NumFacturaSoldarco,
    string OrdenCompraSoldarco,
    decimal SubtotalBruto,
    decimal DescuentoComercial,
    decimal SubtotalNeto,
    decimal IvaDescontable,
    decimal TotalAntesRetenciones,
    decimal RetRetefuente,
    decimal RetIca,
    decimal TotalFactura,
    string TipoPago,
    bool FacturaAprobada,
    bool PlataformaProveedor,
    string? RutEscaneado,
    bool Evento30,
    bool Evento31,
    bool Evento32,
    bool Evento33,
    bool Evento34,
    bool Seleccionado,
    string Cufe,
    string? MarkerColor,
    string? MarkerNote,
    Guid? AssignedTenantUserId,
    bool Rechazado,
    OrigenRenglonConciliacionDian Origen,
    PresenciaConciliacionDian Presencia);

/// <summary>Presencia del documento comparando DIAN vs proveedor tecnologico NEWTON, acotada al periodo del
/// CCD. Derivada por el servicio al listar (no se persiste): <see cref="SoloDian"/> esta en la DIAN y no en
/// NEWTON; <see cref="Ambos"/> esta en ambos; <see cref="SoloNewton"/> esta en NEWTON y no en la DIAN
/// (renglon fantasma). "Esta en NEWTON" = el CUFE existe en NEWTON (con o sin PDF).</summary>
public enum PresenciaConciliacionDian
{
    SoloDian = 0,
    Ambos = 1,
    SoloNewton = 2
}

/// <summary>Filtros del panel (proveedor, rango de fechas de emision, rango de numero de factura).</summary>
public sealed record ConciliacionDianFiltro(
    string? Proveedor = null,
    DateTimeOffset? Desde = null,
    DateTimeOffset? Hasta = null,
    string? DocDesde = null,
    string? DocHasta = null);

/// <summary>Resultado del cruce contra el ERP.</summary>
public sealed record ConciliacionCruceResult(int Cruzadas, int Inconsistencias, int AutoAprobadas);

/// <summary>Resultado (mock en Fase 1) de la radicacion de eventos RADIAN.</summary>
public sealed record ConciliacionEventosResult(int FacturasProcesadas, int EventosMarcados, int SinPlataforma);

/// <summary>Resultado de radicar eventos RADIAN en NEWTON. Si <see cref="Simulado"/> es true fue un dry-run
/// (NO se llamo a la API; nada se radico ni se marco). Si es false fue el envio REAL (irreversible).</summary>
public sealed record ConciliacionRadicacionResult(
    int FacturasElegibles, int EventosEnviados, int Fallidos, int SinPlataforma, int SinEventId, bool Simulado, string? Error);
