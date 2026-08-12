namespace Ecorex.Domain.Enums;

/// <summary>
/// Fuente de una busqueda de contactos configurada (<see cref="Ecorex.Domain.Entities.ContactSearchDefinition"/>).
/// La UI agrupa LinkedIn/Instagram/Facebook/X bajo "Redes sociales" (y pregunta cual); Maps y Web van
/// aparte. El valor tambien se guarda como <c>Fuente</c> del prospecto capturado.
/// </summary>
public enum ContactSearchSource
{
    /// <summary>Google Maps: negocios por pais/region/ciudad + palabra clave.</summary>
    Maps = 0,
    LinkedIn,
    /// <summary>Buscador web general o directorios.</summary>
    Web,
    Instagram,
    Facebook,
    /// <summary>X (antes Twitter).</summary>
    X
}
