namespace Ecorex.Domain.Enums;

/// <summary>
/// Ambito de una etiqueta de tarjeta (ADR-0097 Fase A): separa el catalogo de etiquetas de FLUJOS
/// del de FORMULARIOS. Cada modulo agrupa sus tarjetas con sus propias etiquetas.
/// </summary>
public enum CardTagScope
{
    Flow = 0,
    Form = 1
}
