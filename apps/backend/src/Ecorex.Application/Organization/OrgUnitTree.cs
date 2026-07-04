namespace Ecorex.Application.Organization;

/// <summary>
/// Validacion PURA del arbol del organigrama (sin EF, testeable en unit tests):
/// detecta si mover/crear una unidad bajo un padre generaria un ciclo, es decir,
/// si la unidad terminaria siendo su propio ancestro (ADR-0017).
/// </summary>
public static class OrgUnitTree
{
    /// <summary>
    /// True si poner <paramref name="unitId"/> bajo <paramref name="newParentId"/> crea un
    /// ciclo: el padre propuesto es la propia unidad o un descendiente suyo. Camina la cadena
    /// de ancestros del padre propuesto con un set de visitados, de modo que un arbol ya
    /// corrupto (ciclo preexistente en datos) tambien se reporta como ciclo en vez de colgarse.
    /// </summary>
    /// <param name="unitId">Unidad que se mueve/edita.</param>
    /// <param name="newParentId">Padre propuesto (null = raiz, nunca hay ciclo).</param>
    /// <param name="parentByUnit">Mapa Id -&gt; ParentId de TODAS las unidades del tenant.</param>
    public static bool WouldCreateCycle(Guid unitId, Guid? newParentId, IReadOnlyDictionary<Guid, Guid?> parentByUnit)
    {
        if (newParentId is null)
        {
            return false;
        }
        var visited = new HashSet<Guid>();
        var current = newParentId;
        while (current is Guid currentId)
        {
            if (currentId == unitId)
            {
                return true;
            }
            if (!visited.Add(currentId))
            {
                // Ciclo preexistente en los datos: tratarlo como invalido (fail-closed).
                return true;
            }
            current = parentByUnit.TryGetValue(currentId, out var parent) ? parent : null;
        }
        return false;
    }
}
