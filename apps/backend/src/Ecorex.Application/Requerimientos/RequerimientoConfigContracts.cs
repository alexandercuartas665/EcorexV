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

/// <summary>Una fila de la matriz de proveedores (con nombres resueltos para mostrar).</summary>
public sealed record RequerimientoReglaDto(
    Guid Id,
    RequerimientoDimension Dimension,
    string ValorDimension,
    string? ValorNombre,
    Guid ProveedorId,
    string? ProveedorNombre,
    int Prioridad,
    bool Aprendida,
    bool Activo);

/// <summary>Alta/edicion de una regla de la matriz. Id null = alta.</summary>
public sealed record SaveReglaRequest(
    Guid? Id,
    RequerimientoDimension Dimension,
    string ValorDimension,
    Guid ProveedorId,
    int Prioridad,
    bool Activo);

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
}
