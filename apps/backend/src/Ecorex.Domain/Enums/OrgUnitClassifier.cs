namespace Ecorex.Domain.Enums;

/// <summary>
/// Clasificador semantico de una unidad del organigrama (asignacion por nodo, ADR-0035).
/// Jerarquia via ParentId: Dependencia contiene Cargos, Cargo contiene Funcionarios.
/// Solo Dependencia y Cargo son asignables a un nodo de flujo (WorkflowNodePolicy);
/// un Funcionario representa a la persona que ocupa un puesto y NUNCA se asigna a un nodo.
/// </summary>
public enum OrgUnitClassifier
{
    /// <summary>Area/departamento formal; contiene Cargos. Asignable a un nodo.</summary>
    Dependencia,

    /// <summary>Puesto de trabajo dentro de una Dependencia; contiene Funcionarios. Asignable a un nodo.</summary>
    Cargo,

    /// <summary>Persona que ocupa un Cargo (liga a TenantUserId). NO asignable a un nodo.</summary>
    Funcionario
}
