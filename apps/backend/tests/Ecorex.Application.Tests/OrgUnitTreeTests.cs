using Ecorex.Application.Organization;

namespace Ecorex.Application.Tests;

/// <summary>
/// Validacion PURA de ciclos del arbol de OrgUnit (Dependencias, legacy 000850, ADR-0017):
/// una unidad no puede ser su propio ancestro. Cubre raiz, padre valido, auto-referencia,
/// hijo directo, descendiente profundo y arbol corrupto con ciclo preexistente.
/// </summary>
public class OrgUnitTreeTests
{
    private static readonly Guid Root = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Child = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid GrandChild = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Sibling = Guid.Parse("00000000-0000-0000-0000-000000000004");

    /// <summary>Arbol: Root -> Child -> GrandChild; Root -> Sibling.</summary>
    private static Dictionary<Guid, Guid?> Tree() => new()
    {
        [Root] = null,
        [Child] = Root,
        [GrandChild] = Child,
        [Sibling] = Root
    };

    [Fact]
    public void MoveToRoot_NeverCreatesCycle()
    {
        Assert.False(OrgUnitTree.WouldCreateCycle(Child, null, Tree()));
    }

    [Fact]
    public void MoveUnderSibling_IsValid()
    {
        // Child pasa a colgar de Sibling (rama distinta): no hay ciclo.
        Assert.False(OrgUnitTree.WouldCreateCycle(Child, Sibling, Tree()));
    }

    [Fact]
    public void MoveUnderItself_IsCycle()
    {
        Assert.True(OrgUnitTree.WouldCreateCycle(Child, Child, Tree()));
    }

    [Fact]
    public void MoveUnderDirectChild_IsCycle()
    {
        // Child no puede colgar de GrandChild (su hijo directo).
        Assert.True(OrgUnitTree.WouldCreateCycle(Child, GrandChild, Tree()));
    }

    [Fact]
    public void MoveRootUnderDeepDescendant_IsCycle()
    {
        // Root no puede colgar de GrandChild (descendiente a dos niveles).
        Assert.True(OrgUnitTree.WouldCreateCycle(Root, GrandChild, Tree()));
    }

    [Fact]
    public void GrandChildUnderRoot_IsValid()
    {
        // Subir un descendiente hacia un ancestro NO es ciclo (solo re-cuelga la rama).
        Assert.False(OrgUnitTree.WouldCreateCycle(GrandChild, Root, Tree()));
    }

    [Fact]
    public void PreexistingCorruptCycle_IsReportedAsCycle()
    {
        // Datos corruptos: A -> B -> A. Mover Sibling bajo A no debe colgarse ni aceptarse.
        var a = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        var b = Guid.Parse("00000000-0000-0000-0000-00000000000b");
        var corrupt = new Dictionary<Guid, Guid?> { [a] = b, [b] = a, [Sibling] = null };
        Assert.True(OrgUnitTree.WouldCreateCycle(Sibling, a, corrupt));
    }

    [Fact]
    public void ParentMissingFromMap_TreatedAsRoot()
    {
        // El padre propuesto no esta en el mapa (ej. recien creado): se corta la caminata.
        var unknown = Guid.Parse("00000000-0000-0000-0000-0000000000ff");
        Assert.False(OrgUnitTree.WouldCreateCycle(Child, unknown, Tree()));
    }
}
