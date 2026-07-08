using Ecorex.Application.Organization;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Logica PURA del resolver de candidatos por nodo (asignacion por nodo, ADR-0035, ola F1).
/// Cubre: un Cargo resuelve sus Funcionarios; una Dependencia resuelve todos los funcionarios
/// descendientes; miembros y responsable se incluyen; sin unidades = vacio; distinct entre
/// varias unidades; tolerancia a ciclos en datos.
/// Arbol: Comercial(Dep) -> Asesor(Cargo) -> Ana(Func), Beto(Func); Finanzas(Dep) -> Aprobador(Cargo) -> Caro(Func).
/// </summary>
public class OrgAssigneeTreeTests
{
    private static readonly Guid Comercial = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Asesor = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
    private static readonly Guid FuncAna = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private static readonly Guid FuncBeto = Guid.Parse("00000000-0000-0000-0000-0000000000c4");
    private static readonly Guid Finanzas = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
    private static readonly Guid Aprobador = Guid.Parse("00000000-0000-0000-0000-0000000000f2");
    private static readonly Guid FuncCaro = Guid.Parse("00000000-0000-0000-0000-0000000000f3");

    private static readonly Guid UserAna = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid UserBeto = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid UserCaro = Guid.Parse("00000000-0000-0000-0000-0000000000a3");
    private static readonly Guid UserResp = Guid.Parse("00000000-0000-0000-0000-0000000000a4");
    private static readonly Guid UserMember = Guid.Parse("00000000-0000-0000-0000-0000000000a5");

    private static List<OrgAssigneeTree.UnitRow> Units(Guid? asesorResponsible = null) =>
    [
        new(Comercial, null, OrgUnitClassifier.Dependencia, null, null),
        new(Asesor, Comercial, OrgUnitClassifier.Cargo, asesorResponsible, null),
        new(FuncAna, Asesor, OrgUnitClassifier.Funcionario, null, UserAna),
        new(FuncBeto, Asesor, OrgUnitClassifier.Funcionario, null, UserBeto),
        new(Finanzas, null, OrgUnitClassifier.Dependencia, null, null),
        new(Aprobador, Finanzas, OrgUnitClassifier.Cargo, null, null),
        new(FuncCaro, Aprobador, OrgUnitClassifier.Funcionario, null, UserCaro)
    ];

    [Fact]
    public void Cargo_ResolvesItsFunctionaries()
    {
        var result = OrgAssigneeTree.ResolveForUnit(Asesor, Units(), []);
        Assert.Equal(2, result.Count);
        Assert.Contains(UserAna, result);
        Assert.Contains(UserBeto, result);
        Assert.DoesNotContain(UserCaro, result);
    }

    [Fact]
    public void Dependencia_ResolvesAllDescendantFunctionaries()
    {
        var result = OrgAssigneeTree.ResolveForUnit(Comercial, Units(), []);
        Assert.Equal(2, result.Count);
        Assert.Contains(UserAna, result);
        Assert.Contains(UserBeto, result);
    }

    [Fact]
    public void Members_And_Responsible_AreIncluded()
    {
        var members = new List<OrgAssigneeTree.MemberRow> { new(Asesor, UserMember) };
        var result = OrgAssigneeTree.ResolveForUnit(Asesor, Units(asesorResponsible: UserResp), members);
        Assert.Contains(UserAna, result);
        Assert.Contains(UserBeto, result);
        Assert.Contains(UserMember, result);
        Assert.Contains(UserResp, result);
    }

    [Fact]
    public void NoUnits_ResolvesEmpty()
    {
        var result = OrgAssigneeTree.ResolveForUnit(Guid.NewGuid(), Units(), []);
        Assert.Empty(result);
    }

    [Fact]
    public void MultipleUnits_AreDistinct()
    {
        // Ambas dependencias resuelven sus funcionarios sin duplicar entre si.
        var result = OrgAssigneeTree.ResolveForUnits([Comercial, Finanzas], Units(), []);
        Assert.Equal(3, result.Count);
        Assert.Contains(UserAna, result);
        Assert.Contains(UserBeto, result);
        Assert.Contains(UserCaro, result);
    }

    [Fact]
    public void OverlappingUnits_DoNotDuplicate()
    {
        // Comercial (padre) y Asesor (hijo) comparten a Ana y Beto: sin duplicados.
        var result = OrgAssigneeTree.ResolveForUnits([Comercial, Asesor], Units(), []);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void CyclicData_DoesNotHang()
    {
        // Datos corruptos: A -> B -> A, ambos con funcionario. El recorrido termina.
        var a = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
        var b = Guid.Parse("00000000-0000-0000-0000-0000000000e2");
        var units = new List<OrgAssigneeTree.UnitRow>
        {
            new(a, b, OrgUnitClassifier.Funcionario, null, UserAna),
            new(b, a, OrgUnitClassifier.Funcionario, null, UserBeto)
        };
        var result = OrgAssigneeTree.ResolveForUnit(a, units, []);
        Assert.Equal(2, result.Count);
    }
}
