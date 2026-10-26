namespace Ecorex.Domain.Enums;

/// <summary>
/// Accion auditada sobre una respuesta de formulario (FormResponseAudit). Tenant-scoped e inmutable.
/// El autosave continuo (SaveAsync sin submit) NO se audita por fila (seria ruido): se auditan los
/// eventos del ciclo de vida que importan para no perder datos: alta, envio, borrado, anulacion y el
/// snapshot al generar/enviar el PDF.
/// </summary>
public enum FormResponseAuditAction
{
    Create = 0,
    Update = 1,
    Submit = 2,
    Delete = 3,
    Void = 4,
    PdfSnapshot = 5
}
