using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Marca/branding de la plataforma (global, configurable por el Super Admin).
/// Tabla de una sola fila: gobierna el logo y los textos de la pantalla de login.
/// </summary>
public class PlatformBranding : BaseEntity
{
    /// <summary>Nombre visible de la plataforma (ej. "ECOREX.tareas").</summary>
    public string PlatformName { get; set; } = "ECOREX.tareas";

    /// <summary>Bajada corta bajo el nombre (ej. "CRM Conversacional").</summary>
    public string? Tagline { get; set; }

    /// <summary>URL del logo principal del login (en /uploads/branding o /img/brand). Null = logo por defecto.</summary>
    public string? LoginLogoUrl { get; set; }

    /// <summary>Titular grande del panel izquierdo del login.</summary>
    public string? LoginHeadline { get; set; }

    /// <summary>Texto descriptivo del panel izquierdo del login.</summary>
    public string? LoginSubtext { get; set; }
}
