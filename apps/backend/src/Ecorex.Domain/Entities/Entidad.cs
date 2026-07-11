using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Entidad de negocio administrada por el tenant (agencia/area/sucursal, legacy SUCURSAL_R): una
/// cuenta puede tener VARIAS. Cada una tiene su identidad legal, ubicacion, config y logo, y es la
/// que alimenta el selector "Empresa/Area" al crear actividades. TENANT-SCOPED (filtro global).
/// El tenant en si (legacy SUCURSAL) es la cuenta ("Mi cuenta"), distinto de estas entidades.
/// Nunca se borra fisico: se archiva (IsArchived). Unica por (TenantId, Codigo). Los campos
/// dinamicos que cada tenant agrega viven en <see cref="FieldValuesJson"/> (dict FieldKey-&gt;valor),
/// definidos en <see cref="EntidadFieldDefinition"/> (calca el patron de ItemFieldDefinition).
/// </summary>
public class Entidad : TenantEntity
{
    /// <summary>Codigo legible unico por tenant (ej. "ENT-01").</summary>
    public string Codigo { get; set; } = null!;

    /// <summary>
    /// Naturaleza de la entidad: <see cref="EntidadKind.Sede"/> (ubicacion fisica con identidad
    /// legal) o <see cref="EntidadKind.Area"/> (unidad organizativa interna). Decide que campos
    /// aplican en el modal de edicion.
    /// </summary>
    public EntidadKind Kind { get; set; } = EntidadKind.Sede;

    /// <summary>Razon social / nombre de la entidad.</summary>
    public string Nombre { get; set; } = null!;

    public string? NombreComercial { get; set; }
    public string? Sigla { get; set; }

    /// <summary>Tipo de entidad (ej. Empresa, Agencia, Sucursal). Texto/seleccion libre.</summary>
    public string? TipoEntidad { get; set; }

    /// <summary>Identificacion fiscal generica (NIT/RUT/Tax ID). Multi-pais.</summary>
    public string? TaxId { get; set; }

    /// <summary>Digito verificador opcional del TaxId.</summary>
    public string? TaxIdDv { get; set; }

    public string? RepresentanteLegal { get; set; }
    public string? NaturalezaJuridica { get; set; }

    // ---- Ubicacion y contacto ----
    public string? Pais { get; set; }
    public string? Departamento { get; set; }
    public string? Ciudad { get; set; }
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Web { get; set; }

    // ---- Configuracion del sistema ----
    public string? ZonaHoraria { get; set; }
    public string? Idioma { get; set; }
    public string? Observaciones { get; set; }

    /// <summary>Logo de la entidad como data URI/base64 (opcional).</summary>
    public string? LogoBase64 { get; set; }

    /// <summary>Marca la entidad principal del tenant (solo una recomendada).</summary>
    public bool IsPrincipal { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsArchived { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Valores de los campos dinamicos: dict FieldKey -&gt; valor (jsonb PG / nvarchar(max) SQL).</summary>
    public string? FieldValuesJson { get; set; }
}
