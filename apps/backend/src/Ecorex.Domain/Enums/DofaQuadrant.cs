namespace Ecorex.Domain.Enums;

/// <summary>
/// Cuadrante del analisis DOFA/SWOT de un proyecto (legacy PROYECTOS_DOFA). Se persiste como string.
/// </summary>
public enum DofaQuadrant
{
    /// <summary>Fortaleza (interno, positivo).</summary>
    Fortaleza = 0,

    /// <summary>Oportunidad (externo, positivo).</summary>
    Oportunidad = 1,

    /// <summary>Debilidad (interno, negativo).</summary>
    Debilidad = 2,

    /// <summary>Amenaza (externo, negativo).</summary>
    Amenaza = 3,
}
