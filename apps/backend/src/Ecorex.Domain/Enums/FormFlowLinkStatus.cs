namespace Ecorex.Domain.Enums;

/// <summary>Estado del vinculo respuesta-de-formulario / paso-de-flujo (ADR-0015).</summary>
public enum FormFlowLinkStatus
{
    /// <summary>El paso del flujo espera el envio del formulario.</summary>
    Pending = 0,
    /// <summary>El formulario fue enviado y el paso se completo via IWorkflowEngine.</summary>
    Completed
}
