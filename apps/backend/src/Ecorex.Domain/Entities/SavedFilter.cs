using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Filtro guardado y COMPARTIDO por el tenant para una grilla/modulo (p. ej. /conciliacion-dian). Lo arma un
/// usuario con el constructor de condiciones (grupos AND/OR anidados) y queda disponible para todos los usuarios
/// del tenant que abran esa grilla. El arbol de condiciones se guarda como JSON opaco para la BD: lo produce y
/// consume la pagina, asi evoluciona sin migraciones. Clave logica: (TenantId, Module, Name).
/// </summary>
public sealed class SavedFilter : TenantEntity
{
    /// <summary>Identificador estable de la grilla/modulo al que pertenece el filtro (p. ej. "ConciliacionDian").</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>Nombre visible del filtro (unico por tenant+modulo).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Arbol de condiciones serializado (grupos AND/OR + condiciones campo/operador/valor). JSON.</summary>
    public string DefinitionJson { get; set; } = "{}";

    /// <summary>Usuario que lo creo/actualizo por ultima vez (para mostrar autoria). Opcional.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Nombre del autor, cacheado para mostrarlo sin un join. Opcional.</summary>
    public string? CreatedByName { get; set; }
}
