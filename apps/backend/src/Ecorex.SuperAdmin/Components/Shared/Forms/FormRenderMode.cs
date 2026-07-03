namespace Ecorex.SuperAdmin.Components.Shared.Forms;

/// <summary>Modo de render del DynamicFormRenderer (ADR-0015).</summary>
public enum FormRenderMode
{
    /// <summary>Vista previa del disenador: controles deshabilitados, sin respuesta.</summary>
    Design = 0,
    /// <summary>Llenado real: borrador con autosave (30s), validacion inmediata y envio.</summary>
    Fill,
    /// <summary>Solo lectura de una respuesta existente.</summary>
    ReadOnly
}
