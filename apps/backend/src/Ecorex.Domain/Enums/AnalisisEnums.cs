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

/// <summary>Tipo visual de un widget de una hoja de analisis (ADR-0158 D1/D3).</summary>
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
    Echarts = 8
}
