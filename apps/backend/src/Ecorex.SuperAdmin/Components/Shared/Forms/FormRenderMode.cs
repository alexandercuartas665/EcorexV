namespace Ecorex.SuperAdmin.Components.Shared.Forms;

/// <summary>Modo de render del DynamicFormRenderer (ADR-0015).</summary>
public enum FormRenderMode
{
    /// <summary>Vista previa del disenador: controles deshabilitados, sin respuesta.</summary>
    Design = 0,
    /// <summary>Llenado real: borrador con autosave (30s), validacion inmediata y envio.</summary>
    Fill,
    /// <summary>Solo lectura de una respuesta existente.</summary>
    ReadOnly,
    /// <summary>Vista previa INTERACTIVA de un borrador: editable (agregar/quitar filas, escribir,
    /// formulas en vivo) pero SIN persistir respuesta ni enviar. Sirve para probar el formulario
    /// antes de activarlo. No crea borrador en BD, no autoguarda y no muestra la barra de envio.</summary>
    Sandbox
}
