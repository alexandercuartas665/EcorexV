using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Tercero del Directorio General (modulo 000232): empresa o persona natural con uno o
/// mas perfiles de negocio (cliente, sospechoso, proveedor, empleado). Una Empresa
/// agrupa contactos (personas asignadas via <see cref="EmpresaId"/> y/o contactos
/// embebidos <see cref="Contactos"/>). Multi-tenant (filtro global por reflexion).
///
/// Las "fichas" por perfil (fiscal, comercial, cliente, riesgo, proveedor, empleado)
/// son campos DINAMICOS y personalizables por el usuario, por eso se guardan como
/// jsonb en <see cref="FichasJson"/> (dict ficha -> dict campo -> valor), no en columnas.
/// </summary>
public class Tercero : TenantEntity
{
    public string Nombre { get; set; } = null!;

    /// <summary>Empresa o Persona natural.</summary>
    public TerceroTipo Tipo { get; set; } = TerceroTipo.Empresa;

    /// <summary>Perfiles acumulados (multi-valor, [Flags]).</summary>
    public TerceroPerfil Perfiles { get; set; } = TerceroPerfil.Ninguno;

    public TerceroEstado Estado { get; set; } = TerceroEstado.Activo;

    /// <summary>Vendedor asignado (texto libre; tambien vive en la ficha comercial).</summary>
    public string? Vendedor { get; set; }

    public string? Ciudad { get; set; }

    // ---- Identificacion ----
    public TerceroIdTipo IdTipo { get; set; } = TerceroIdTipo.Nit;

    /// <summary>Valor del documento (NIT, cedula, correo o telefono). Null si IdTipo=Ninguno.</summary>
    public string? IdValor { get; set; }

    // ---- Especificos de Empresa ----
    public string? Sector { get; set; }

    // ---- Especificos de Persona ----
    public string? Cargo { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }

    /// <summary>Si es Persona y esta asignada a una Empresa, apunta a esa empresa (self-FK).
    /// Null = cliente individual (aparece como fila propia en la lista).</summary>
    public Guid? EmpresaId { get; set; }
    public Tercero? Empresa { get; set; }

    /// <summary>Contactos embebidos de la empresa (personas de contacto nativas, no terceros).</summary>
    public ICollection<TerceroContacto> Contactos { get; set; } = new List<TerceroContacto>();

    /// <summary>Datos de las fichas por perfil, dinamicos. jsonb en PG / nvarchar(max) en SQL Server.</summary>
    public string? FichasJson { get; set; }

    /// <summary>Columna/estado de la Bolsa de contactos del Gestor de Clientes (000740) en la que
    /// esta este tercero. Null = no esta en la bolsa (solo vive en el Directorio). NO ACTION.</summary>
    public Guid? BolsaColumnaId { get; set; }
    public BolsaColumna? BolsaColumna { get; set; }
}
