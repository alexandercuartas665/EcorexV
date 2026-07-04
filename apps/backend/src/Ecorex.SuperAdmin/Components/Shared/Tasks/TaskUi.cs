using Ecorex.Domain.Enums;

namespace Ecorex.SuperAdmin.Components.Shared.Tasks;

/// <summary>
/// Helpers de presentacion del nucleo de tareas (FASE 3): etiquetas en espanol,
/// clases CSS por estado/prioridad y formatos de fecha/duracion compartidos por
/// el kanban, el wizard y el detalle.
/// </summary>
public static class TaskUi
{
    /// <summary>Columnas fijas del kanban por estado (Closed no se muestra como columna).</summary>
    public static readonly TaskItemStatus[] BoardStatuses =
    [
        TaskItemStatus.Pending,
        TaskItemStatus.Active,
        TaskItemStatus.InProgress,
        TaskItemStatus.Done,
        TaskItemStatus.Suspended
    ];

    public static string StatusLabel(TaskItemStatus status) => status switch
    {
        TaskItemStatus.Pending => "Pendiente",
        TaskItemStatus.Active => "Activa",
        TaskItemStatus.InProgress => "En proceso",
        TaskItemStatus.Done => "Terminada",
        TaskItemStatus.Suspended => "Suspendida",
        TaskItemStatus.Closed => "Cerrada",
        _ => status.ToString()
    };

    public static string StatusClass(TaskItemStatus status) => status switch
    {
        TaskItemStatus.Pending => "st-pending",
        TaskItemStatus.Active => "st-active",
        TaskItemStatus.InProgress => "st-progress",
        TaskItemStatus.Done => "st-done",
        TaskItemStatus.Suspended => "st-suspended",
        TaskItemStatus.Closed => "st-closed",
        _ => "st-pending"
    };

    public static string PriorityLabel(TaskPriority priority) => priority switch
    {
        TaskPriority.High => "Alta",
        TaskPriority.Medium => "Media",
        TaskPriority.Low => "Baja",
        _ => priority.ToString()
    };

    public static string PriorityClass(TaskPriority priority) => priority switch
    {
        TaskPriority.High => "pr-high",
        TaskPriority.Medium => "pr-medium",
        TaskPriority.Low => "pr-low",
        _ => "pr-medium"
    };

    public static string ProjectStatusLabel(ProjectStatus status) => status switch
    {
        ProjectStatus.Planning => "Planeacion",
        ProjectStatus.Active => "Activo",
        ProjectStatus.InExecution => "En ejecucion",
        ProjectStatus.Closed => "Cerrado",
        ProjectStatus.Cancelled => "Cancelado",
        _ => status.ToString()
    };

    public static string ProjectStatusClass(ProjectStatus status) => status switch
    {
        ProjectStatus.Planning => "pj-planning",
        ProjectStatus.Active => "pj-active",
        ProjectStatus.InExecution => "pj-exec",
        ProjectStatus.Closed => "pj-closed",
        ProjectStatus.Cancelled => "pj-cancelled",
        _ => "pj-planning"
    };

    /// <summary>Duracion total del worklog en formato compacto, ej. "4h 32m".</summary>
    public static string FormatDuration(long totalSeconds)
    {
        if (totalSeconds <= 0) { return "0m"; }
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        if (hours == 0 && minutes == 0) { return $"{totalSeconds}s"; }
        return hours > 0 ? $"{hours}h {minutes:00}m" : $"{minutes}m";
    }

    public static bool IsOverdue(DateTimeOffset? due, TaskItemStatus status)
        => due is not null
           && due.Value.ToLocalTime().Date < DateTime.Now.Date
           && status is not (TaskItemStatus.Done or TaskItemStatus.Closed);

    public static string FormatDue(DateTimeOffset due)
    {
        var local = due.ToLocalTime().Date;
        var today = DateTime.Now.Date;
        if (local == today) { return "Hoy"; }
        if (local == today.AddDays(1)) { return "Manana"; }
        if (local == today.AddDays(-1)) { return "Ayer"; }
        return local.ToString("dd MMM yyyy");
    }

    public static string FormatRelative(DateTimeOffset when)
    {
        var diff = DateTimeOffset.UtcNow - when;
        if (diff.TotalMinutes < 1) { return "ahora mismo"; }
        if (diff.TotalMinutes < 60) { return $"hace {(int)diff.TotalMinutes} min"; }
        if (diff.TotalHours < 24) { return $"hace {(int)diff.TotalHours} h"; }
        if (diff.TotalDays < 7) { return $"hace {(int)diff.TotalDays} d"; }
        return when.ToLocalTime().ToString("dd MMM yyyy");
    }

    /// <summary>Iniciales para el avatar a partir de un nombre o email.</summary>
    public static string InitialsOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) { return "?"; }
        var trimmed = name.Trim();
        var atIdx = trimmed.IndexOf('@');
        if (atIdx > 0) { trimmed = trimmed[..atIdx]; }
        var parts = trimmed.Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { return trimmed.Length >= 2 ? trimmed[..2].ToUpperInvariant() : trimmed.ToUpperInvariant(); }
        if (parts.Length == 1) { return parts[0].Length >= 2 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant(); }
        return (parts[0][0].ToString() + parts[1][0]).ToUpperInvariant();
    }

    /// <summary>
    /// Nombre legible para dropdowns de asignado (ola 3): DisplayName del PlatformUser
    /// si existe; si no, se DERIVA de la parte local del email con cada palabra
    /// capitalizada ("ana.garcia@x.com" -> "Ana Garcia"). Decision documentada: el
    /// nombre real solo vive en PlatformUser.DisplayName y puede ser null.
    /// </summary>
    public static string UserLabel(Ecorex.Application.Tenancy.TenantUserDto user)
    {
        if (!string.IsNullOrWhiteSpace(user.DisplayName)) { return user.DisplayName!.Trim(); }
        var local = user.Email;
        var at = local.IndexOf('@');
        if (at > 0) { local = local[..at]; }
        var parts = local.Split(['.', '-', '_', '+'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { return user.Email; }
        return string.Join(" ", parts.Select(p =>
            p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }

    /// <summary>Validacion simple de formato de email (suficiente para la UI).</summary>
    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) { return false; }
        var value = email.Trim();
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1) { return false; }
        var domain = value[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.') && !value.Contains(' ');
    }
}
