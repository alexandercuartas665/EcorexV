using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>Agencia turistica cliente del SaaS. Entidad global administrada por el Super Admin.</summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = null!;
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Country { get; set; }

    /// <summary>
    /// Zona horaria del tenant en formato IANA (ej. "America/Bogota"). Regla 9 del proyecto: toda fecha
    /// se calcula en la hora del tenant y se persiste en UTC. La usa el motor de programaciones (000889)
    /// para que "todos los lunes a las 08:00" signifique 08:00 EN EL TENANT. Null = default del sistema.
    /// </summary>
    public string? TimeZoneId { get; set; }
    public string? Currency { get; set; }

    // --- Perfil de contacto/domicilio de la empresa (migracion AddTenantProfile, ADR-0026) ---
    // Campos seguros de la ficha del modulo 000072 (adm_empresas) que el modelo Tenant no tenia.
    // El resto de la ficha legacy (contador/revisor fiscal, integraciones y modulos por empresa,
    // copiar datos/formularios, reglas) queda como placeholder visible-deshabilitado (ver ADR-0026).

    /// <summary>Ciudad principal de la empresa (SUCURSAL.CIUDAD del legacy).</summary>
    public string? City { get; set; }

    /// <summary>Direccion fisica de la empresa (SUCURSAL.DIRECCION del legacy).</summary>
    public string? Address { get; set; }

    /// <summary>Telefono(s) de contacto de la empresa (SUCURSAL.TELS del legacy).</summary>
    public string? Phone { get; set; }

    /// <summary>Correo de contacto de la empresa (SUCURSAL.EMAIL del legacy).</summary>
    public string? Email { get; set; }

    /// <summary>Ruta del logo de la agencia (subido por el cliente), p.ej. /uploads/tenant-{id}.png.</summary>
    public string? LogoUrl { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public TenantKind Kind { get; set; } = TenantKind.Standard;

    /// <summary>Reservas online por link publico habilitadas (columna legado del backbone; sin uso en ECOREX Tareas).</summary>
    public bool OnlineBookingEnabled { get; set; }

    /// <summary>Token opaco del link publico de reserva (/r/{token}). Se genera al habilitar. Unico.</summary>
    public string? PublicBookingToken { get; set; }

    /// <summary>Base publica del link (ej. https://beauty.ecorex.com.co), capturada al habilitar desde la
    /// consola. La usa el agente (sin request HTTP) para armar el link completo.</summary>
    public string? PublicBookingBaseUrl { get; set; }
}
