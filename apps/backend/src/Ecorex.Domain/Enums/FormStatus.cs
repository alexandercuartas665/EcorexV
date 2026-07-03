namespace Ecorex.Domain.Enums;

/// <summary>Ciclo de vida de una definicion de formulario dinamico (FASE 4, ADR-0015).</summary>
public enum FormStatus
{
    /// <summary>En diseno: editable, no se puede responder.</summary>
    Draft = 0,
    /// <summary>Publicado: acepta respuestas; los cambios estructurales incrementan Revision.</summary>
    Active,
    /// <summary>Retirado: no acepta respuestas nuevas, conserva las existentes.</summary>
    Inactive
}
