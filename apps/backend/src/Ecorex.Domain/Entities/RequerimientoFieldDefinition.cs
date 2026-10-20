using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de un campo DINAMICO (a medida) del modulo Requerimientos y Compras (Capa 10, Ola 12), por
/// tenant. El usuario los crea sin codigo ("Campos") y aparecen como columnas/campos editables. El
/// <see cref="Scope"/> decide si el campo cuelga de la NECESIDAD (padre) o de cada linea de PLANIFICACION
/// (hija). Sus valores se guardan en <see cref="RequerimientoFieldValue"/> (EAV, como en conciliacion y el
/// directorio). Clave logica: (TenantId, Scope, FieldKey).
/// </summary>
public sealed class RequerimientoFieldDefinition : TenantEntity
{
    /// <summary>Clave estable del campo (slug, p. ej. "centro_costo"). No cambia aunque cambie la etiqueta.</summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>Etiqueta visible.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Ambito del campo: "Padre" (necesidad) | "Hija" (linea de planificacion).</summary>
    public string Scope { get; set; } = "Padre";

    /// <summary>Tipo: "Text" | "Number" | "Date" | "Bool" | "Select" | "Url".</summary>
    public string Type { get; set; } = "Text";

    /// <summary>Orden entre los campos dinamicos del mismo ambito.</summary>
    public int SortOrder { get; set; }

    /// <summary>Opciones (JSON array de strings) cuando Type = "Select". Null para los demas.</summary>
    public string? OptionsJson { get; set; }

    /// <summary>Descripcion opcional (tooltip en el encabezado/campo).</summary>
    public string? Description { get; set; }

    /// <summary>Si el campo se ofrece en el constructor de filtros del listado (solo ambito Padre). Default true.</summary>
    public bool ShowInFilter { get; set; } = true;
}
