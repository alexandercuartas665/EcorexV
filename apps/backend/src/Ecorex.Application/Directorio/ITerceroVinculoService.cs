using Ecorex.Domain.Enums;

namespace Ecorex.Application.Directorio;

/// <summary>Un vinculo Persona &lt;-&gt; Organizacion visto desde una ficha (panel de relaciones, O4-1).
/// "Otro" es el tercero al otro lado del vinculo. VinculoId null + EsLegado = enlace primario legado
/// (Tercero.EmpresaId), que se muestra junto a los vinculos M:N nuevos.</summary>
public sealed record TerceroVinculoDto(
    Guid? VinculoId, Guid OtroId, string OtroNombre, bool OtroEsEmpresa, string? Cargo, bool Principal, bool EsLegado);

/// <summary>Resultado de la busqueda progresiva para vincular terceros existentes (autocompletar).</summary>
public sealed record TerceroBuscarDto(Guid Id, string Nombre, bool EsEmpresa, string? Identificacion);

/// <summary>
/// Relaciones Persona &lt;-&gt; Organizacion del Directorio Modular (Capa 8, regla 3.1, Ola 4). Gestiona los
/// vinculos M:N con cargo por vinculo (<see cref="Ecorex.Domain.Entities.TerceroVinculo"/>) y los combina
/// con el enlace primario legado (EmpresaId) para el panel bidireccional. Tenant-scoped por filtro global.
/// </summary>
public interface ITerceroVinculoService
{
    /// <summary>Relaciones de un tercero: si es Persona, sus organizaciones; si es Empresa, sus personas.
    /// Une los vinculos M:N con el enlace primario legado (EmpresaId), sin duplicar.</summary>
    Task<IReadOnlyList<TerceroVinculoDto>> ListDeAsync(Guid terceroId, CancellationToken cancellationToken = default);

    /// <summary>Crea (o actualiza el cargo de) un vinculo Persona -> Organizacion. Devuelve error o null.</summary>
    Task<string?> AgregarAsync(Guid personaId, Guid organizacionId, string? cargo, CancellationToken cancellationToken = default);

    /// <summary>Quita el vinculo entre una persona y una organizacion (borra el M:N; si es el enlace
    /// primario legado, limpia EmpresaId). Devuelve error o null.</summary>
    Task<string?> QuitarAsync(Guid personaId, Guid organizacionId, CancellationToken cancellationToken = default);

    /// <summary>Busca terceros existentes del tipo dado (para vincular por autocompletado). Excluye el
    /// tercero actual y los inactivos. Tope de resultados.</summary>
    Task<IReadOnlyList<TerceroBuscarDto>> BuscarAsync(string term, TerceroTipo tipo, Guid excluir, CancellationToken cancellationToken = default);
}
