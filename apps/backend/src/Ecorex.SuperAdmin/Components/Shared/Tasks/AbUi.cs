using Ecorex.Domain.Enums;

namespace Ecorex.SuperAdmin.Components.Shared.Tasks;

/// <summary>
/// Helpers de presentacion de los TABLEROS DE ACTIVIDADES (ADR-0020, pantalla 'work'
/// del prototipo ECOREX.dc.html): paleta de avatares, colores por columna (punto,
/// badge y barra de progreso), estados del tablero y formatos de fecha del prototipo.
/// Valores 1:1 con el fuente del prototipo (colPal / AVPAL / PR).
/// </summary>
public static class AbUi
{
    /// <summary>Paleta de avatares del prototipo (AVPAL): colores solidos apagados.</summary>
    private static readonly string[] AvatarPalette =
    [
        "#E0876B", "#7E78E6", "#3FA39B", "#5B8DEF", "#C77FD0", "#D9A441", "#5FAE6E", "#E07A9B"
    ];

    /// <summary>Fondo del avatar por hash de iniciales (misma formula avBg del prototipo).</summary>
    public static string AvatarBg(string? initials)
    {
        var value = string.IsNullOrEmpty(initials) ? "NA" : initials;
        var code = value[0] + (value.Length > 1 ? value[1] : 0);
        return AvatarPalette[code % AvatarPalette.Length];
    }

    // colPal del prototipo: [dot, [badgeBg, badgeFg]] por indice de columna (ciclico).
    private static readonly string[] ColumnDots =
    [
        "var(--ink-3)", "var(--warn)", "var(--t-amber)", "var(--ok)"
    ];

    private static readonly (string Bg, string Fg)[] ColumnBadges =
    [
        ("var(--ink)", "var(--surface)"), ("var(--warn)", "#fff"), ("var(--t-amber)", "#fff"), ("var(--ok)", "#fff")
    ];

    /// <summary>Barra de progreso de la tarjeta por columna (Por hacer / En progreso / En revision / Completado).</summary>
    private static readonly string[] ColumnProgress =
    [
        "var(--t-blue)", "var(--danger)", "var(--t-amber)", "var(--ok)"
    ];

    public static string ColumnDot(int index) => ColumnDots[Mod(index, ColumnDots.Length)];
    public static string ColumnBadgeBg(int index) => ColumnBadges[Mod(index, ColumnBadges.Length)].Bg;
    public static string ColumnBadgeFg(int index) => ColumnBadges[Mod(index, ColumnBadges.Length)].Fg;
    public static string ColumnProgressColor(int index) => ColumnProgress[Mod(index, ColumnProgress.Length)];

    private static int Mod(int index, int length) => ((index % length) + length) % length;

    // ---- Estado del tablero (badge del indice y punto del detalle) ----

    public static string BoardStatusLabel(TaskBoardStatus status) => status switch
    {
        TaskBoardStatus.OnTime => "A tiempo",
        TaskBoardStatus.InProgress => "En progreso",
        TaskBoardStatus.AtRisk => "En riesgo",
        TaskBoardStatus.Completed => "Completado",
        _ => status.ToString()
    };

    public static string BoardStatusColor(TaskBoardStatus status) => status switch
    {
        TaskBoardStatus.OnTime => "var(--ok)",
        TaskBoardStatus.InProgress => "var(--warn)",
        TaskBoardStatus.AtRisk => "var(--danger)",
        TaskBoardStatus.Completed => "var(--ink-3)",
        _ => "var(--ink-3)"
    };

    public static string BoardStatusSoft(TaskBoardStatus status) => status switch
    {
        TaskBoardStatus.OnTime => "var(--t-green-bg)",
        TaskBoardStatus.InProgress => "var(--t-amber-bg)",
        TaskBoardStatus.AtRisk => "var(--t-rose-bg)",
        TaskBoardStatus.Completed => "var(--surface-3)",
        _ => "var(--surface-3)"
    };

    // ---- Prioridad (PR del prototipo: color, soft) ----

    public static string PriorityColor(TaskPriority priority) => priority switch
    {
        TaskPriority.High => "var(--t-rose)",
        TaskPriority.Medium => "var(--t-amber)",
        TaskPriority.Low => "var(--t-green)",
        _ => "var(--ink-3)"
    };

    public static string PrioritySoft(TaskPriority priority) => priority switch
    {
        TaskPriority.High => "var(--t-rose-bg)",
        TaskPriority.Medium => "var(--t-amber-bg)",
        TaskPriority.Low => "var(--t-green-bg)",
        _ => "var(--surface-3)"
    };

    // ---- Fechas (formatos ASCII del prototipo) ----

    private static readonly string[] MonthsShort =
    [
        "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"
    ];

    private static readonly string[] MonthsLong =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    ];

    /// <summary>Fecha corta de tarjeta: "Hoy", "Manana" o "1 jul" (estilo del prototipo).</summary>
    public static string CardDue(DateTimeOffset due)
    {
        var local = due.ToLocalTime().Date;
        var today = DateTime.Now.Date;
        if (local == today) { return "Hoy"; }
        if (local == today.AddDays(1)) { return "Manana"; }
        var suffix = local.Year == today.Year ? "" : $" {local.Year}";
        return $"{local.Day} {MonthsShort[local.Month - 1]}{suffix}";
    }

    /// <summary>Color de la fecha de la tarjeta: vencida danger, hoy warn, resto ink-2.</summary>
    public static string CardDueColor(DateTimeOffset? due, bool columnIsDone)
    {
        if (due is null) { return "var(--ink-2)"; }
        var local = due.Value.ToLocalTime().Date;
        var today = DateTime.Now.Date;
        if (columnIsDone) { return "var(--ink-3)"; }
        if (local < today) { return "var(--danger)"; }
        if (local == today) { return "var(--warn)"; }
        return "var(--ink-2)";
    }

    /// <summary>Fecha limite del tablero: "12 julio, 2026" (formato del prototipo).</summary>
    public static string BoardDeadline(DateTimeOffset? due)
    {
        if (due is null) { return "Sin fecha limite"; }
        var local = due.Value.ToLocalTime().Date;
        return $"{local.Day} {MonthsLong[local.Month - 1]}, {local.Year}";
    }

    /// <summary>Titulo del calendario: "Julio 2026" (formato del prototipo).</summary>
    public static string MonthTitle(DateTime month)
    {
        var name = MonthsLong[month.Month - 1];
        return $"{char.ToUpperInvariant(name[0])}{name[1..]} {month.Year}";
    }

    /// <summary>Mes en mayusculas para la banda del gantt: "TAREA - JULIO 2026".</summary>
    public static string MonthUpper(DateTime month)
        => MonthsLong[month.Month - 1].ToUpperInvariant();

    /// <summary>Fondo suave para el chip de etiqueta a partir de su color hex (#RRGGBB + alpha).</summary>
    public static string TagSoft(string? color)
        => string.IsNullOrWhiteSpace(color) ? "var(--surface-3)" : color!.Trim() + "1f";

    public static string TagFg(string? color)
        => string.IsNullOrWhiteSpace(color) ? "var(--ink-2)" : color!.Trim();
}
