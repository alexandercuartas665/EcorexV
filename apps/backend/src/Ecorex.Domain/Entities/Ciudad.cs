using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Catalogo GLOBAL de ciudades / municipios (NO tenant-scoped): las ciudades son compartidas por
/// todos los tenants. Alimenta el selector de "Ciudad" del Directorio General y del modal de
/// Tercero, reemplazando el antiguo input de texto libre. Se sigue guardando el NOMBRE de la
/// ciudad como string en Tercero.Ciudad (compatibilidad); este catalogo solo restringe la entrada
/// a ciudades reales. Semilla: municipios DANE de Colombia (departamento + municipio).
/// </summary>
public class Ciudad : BaseEntity
{
    /// <summary>Nombre del municipio / ciudad (ej. "Medellin"). Sin tildes por convencion ASCII.</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Departamento al que pertenece (ej. "Antioquia"). Opcional.</summary>
    public string? Departamento { get; set; }

    /// <summary>Codigo DANE del municipio (5 digitos), cuando se conoce. Opcional.</summary>
    public string? CodigoDane { get; set; }
}
