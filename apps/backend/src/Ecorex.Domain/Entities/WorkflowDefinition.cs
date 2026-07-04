using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de un flujo de proceso BPMN 2.0 del tenant (FASE 4, ola 1: port del
/// DOC_PROCESOS legacy). El XML BPMN se guarda tal cual se importo (portabilidad con
/// bpmn.io / bpmn-js: el motor NUNCA lo modifica); nodos y aristas se materializan en
/// WorkflowNode/WorkflowEdge para la ejecucion. Versionado inmutable: reimportar el mismo
/// ProcessCode crea una version nueva (max+1) NO publicada; las instancias en curso siguen
/// ancladas a su version. Unico por (TenantId, ProcessCode, Version). TENANT-SCOPED.
/// </summary>
public class WorkflowDefinition : TenantEntity
{
    /// <summary>Codigo estable del proceso (equivalente al PROCESO legacy, ej. "COT-COM").</summary>
    public string ProcessCode { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>XML BPMN 2.0 original, sin modificar (round-trip con el editor bpmn-js).</summary>
    public string BpmnXml { get; set; } = null!;

    /// <summary>Version de la definicion (1..n). Las instancias fijan su version al arrancar.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Solo una version publicada por (TenantId, ProcessCode); publicar despublica la anterior.</summary>
    public bool IsPublished { get; set; }

    /// <summary>Archivada: no se ofrece para instancias nuevas pero conserva historia.</summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// Categoria/cargo del flujo en el indice del prototipo (pantalla 'flujos': tabs de
    /// filtro y badge de la tarjeta). Texto libre corto (ej. "Comercial", "Operaciones").
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Pausada (solo tiene efecto en publicadas): la definicion sigue publicada pero
    /// StartInstanceAsync rechaza instancias nuevas. Estado "Pausado" del indice del
    /// prototipo (En marcha = IsPublished y !IsPaused).
    /// </summary>
    public bool IsPaused { get; set; }
}
