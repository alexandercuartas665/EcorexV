using Ecorex.Domain.Enums;

namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

/// <summary>
/// Servicio del modulo "Conciliacion DIAN Compras" (Automatizaciones). Migra el nucleo real del molde legacy
/// soldarco_conciliacion_provdian. Fase 1: la logica de Importar/Cruzar corre sobre fuentes DUMMY internas;
/// en Fase 2 esas fuentes se cambian por consultas a la conexion externa (ADR-0084). Aislamiento por el filtro
/// global de tenant (nunca se filtra a mano por TenantId).
/// </summary>
public interface IConciliacionDianService
{
    // ---- Documentos CCD (encabezado mensual) ----
    Task<IReadOnlyList<ConciliacionDianDocumentoDto>> ListDocumentosAsync(CancellationToken ct = default);
    Task<ConciliacionDianDocumentoDto?> GetDocumentoAsync(Guid documentoId, CancellationToken ct = default);
    /// <summary>Crea el CCD del periodo (unico por tenant+anio+mes). Devuelve (doc, error).</summary>
    Task<(ConciliacionDianDocumentoDto? Doc, string? Error)> CrearDocumentoAsync(int anio, int mes, CancellationToken ct = default);

    // ---- Renglones ----
    Task<IReadOnlyList<ConciliacionDianRenglonDto>> ListRenglonesAsync(
        Guid documentoId, EstadoConciliacionDian estado, ConciliacionDianFiltro filtro, CancellationToken ct = default);
    /// <summary>Conteo por estado (para los badges de las 4 pestanas), respetando el filtro.</summary>
    Task<IReadOnlyDictionary<EstadoConciliacionDian, int>> ContarPorEstadoAsync(
        Guid documentoId, ConciliacionDianFiltro filtro, CancellationToken ct = default);

    // ---- Acciones ----
    /// <summary>Importa del bot DIAN (dummy en Fase 1) las facturas del periodo que falten (idempotente por CUFE).
    /// Devuelve cuantas se insertaron.</summary>
    Task<int> ImportarDesdeBotAsync(Guid documentoId, CancellationToken ct = default);
    /// <summary>Cruza los renglones contra el ERP (dummy): llena NumFacturaSoldarco, marca Inconsistencia!, auto-aprueba
    /// y recalcula PlataformaProveedor desde NEWTON (dummy).</summary>
    Task<ConciliacionCruceResult> CruzarAsync(Guid documentoId, CancellationToken ct = default);
    Task UpdateOrdenCompraAsync(Guid renglonId, string ordenCompra, CancellationToken ct = default);
    /// <summary>Fija (o limpia con null/"") el color del marcador PERSISTENTE de la fila. Solo visual; valida
    /// contra una whitelist de colores (cualquier otro valor limpia el marcador).</summary>
    Task SetMarkerColorAsync(Guid renglonId, string? color, CancellationToken ct = default);
    Task ToggleAprobadaAsync(Guid renglonId, bool aprobada, CancellationToken ct = default);
    Task SetSeleccionAsync(IReadOnlyList<Guid> renglonIds, bool seleccionado, CancellationToken ct = default);
    /// <summary>MOCK en Fase 1: marca los eventos RADIAN 030/032/033 en los renglones aprobados con plataforma.
    /// La API real de NEWTON (acto legal irreversible) va en Fase 2. Si renglonIds viene vacio, toma los
    /// seleccionados del documento.</summary>
    Task<ConciliacionEventosResult> ProcesarEventosAsync(Guid documentoId, IReadOnlyList<Guid> renglonIds, CancellationToken ct = default);

    /// <summary>Siembra datos DUMMY (fuentes bot/newton/erp + un CCD del mes actual) para el tenant activo, para
    /// poder validar el modulo en Fase 1. Idempotente. Devuelve un resumen legible.</summary>
    Task<string> SeedDummyAsync(CancellationToken ct = default);
}
