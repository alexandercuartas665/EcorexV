namespace Ecorex.Domain.Enums;

/// <summary>Quien puede ver un libro de indicadores (ADR-0158 D7).</summary>
public enum AnalisisVisibilidad
{
    /// <summary>Solo el dueno (y los Admin del tenant).</summary>
    Privado = 0,

    /// <summary>Todos los usuarios del tenant con permiso de ver el modulo (o la lista AllowedUserIds).</summary>
    CompartidoEnTenant = 1,

    /// <summary>Ademas, enlace publico de solo lectura /a/{token} (Ola 6).</summary>
    EnlacePublico = 2
}

/// <summary>
/// Tipo visual de un widget de una hoja de analisis (ADR-0158 D1/D3). Se persiste como int: los valores
/// existentes NO cambian de numero (agregar al final).
/// </summary>
public enum AnalisisTipoWidget
{
    Kpi = 0,
    Barras = 1,
    Dona = 2,
    Linea = 3,
    Area = 4,
    Heatmap = 5,
    Semaforo = 6,
    Tabla = 7,

    /// <summary>Reservado desde la Ola 2 (widget "avanzado"); no se ofrece ni se importa.</summary>
    Echarts = 8,

    // ---- Ola 7b: tipos nuevos (render ECharts) ----

    /// <summary>KPI como velocimetro contra una meta.</summary>
    Velocimetro = 9,

    /// <summary>Embudo por etapas (categorias de mayor a menor).</summary>
    Embudo = 10,

    /// <summary>Barras apiladas (eje + series).</summary>
    BarrasApiladas = 11,

    /// <summary>Barras horizontales (rankings con etiquetas largas).</summary>
    BarrasHorizontales = 12,

    /// <summary>Barras + linea en doble eje (combinado) o Pareto (una fuente: % acumulado).</summary>
    BarrasLinea = 13,

    /// <summary>Calendario de actividad por dia sobre el campo de fecha de la fuente.</summary>
    Calendario = 14,

    /// <summary>Mapa de arbol (una o dos agrupaciones).</summary>
    Treemap = 15
}
