namespace Ecorex.Domain.Enums;

/// <summary>Estado de una respuesta de formulario dinamico (ADR-0015).</summary>
public enum FormResponseStatus
{
    /// <summary>Borrador con autosave: editable, sin validacion completa.</summary>
    Draft = 0,
    /// <summary>Enviada: paso la validacion completa del servidor; inmutable para el llenador.</summary>
    Submitted
}
