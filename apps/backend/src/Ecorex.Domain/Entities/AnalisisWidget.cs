using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Widget de una hoja de analisis (ADR-0158 D1/D6). Sus datos son un <c>ReportQuerySpec</c> serializado
/// (<see cref="SpecJson"/>) O la llave de un indicador curado (<see cref="IndicadorKey"/>). Posicion en la
/// grilla de 4 columnas por <see cref="Orden"/> + <see cref="ColSpan"/>.
/// </summary>
public sealed class AnalisisWidget : TenantEntity
{
    public Guid HojaId { get; set; }

    public AnalisisHoja? Hoja { get; set; }

    public AnalisisTipoWidget Tipo { get; set; }

    public string Titulo { get; set; } = string.Empty;

    /// <summary>ReportQuerySpec serializado (camelCase). Excluyente con <see cref="IndicadorKey"/>.</summary>
    public string? SpecJson { get; set; }

    /// <summary>Llave de un indicador curado (IIndicadorCurado.Key).</summary>
    public string? IndicadorKey { get; set; }

    /// <summary>Formato, colores, umbrales, top (JSON libre de presentacion).</summary>
    public string? ConfigJson { get; set; }

    /// <summary>Parametro de contexto -> campo del spec, para el modo embebido (Ola 4).</summary>
    public string? ContextBindingsJson { get; set; }

    public int Orden { get; set; }

    /// <summary>Ancho en columnas de la grilla (1..4).</summary>
    public int ColSpan { get; set; } = 1;

    public int RowSpan { get; set; } = 1;
}
