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
    string? MarkerColor);

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
