using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Token de acceso de la API REST de configuracion (FASE 1), por-tenant. Es un Bearer opaco que
/// un administrador del tenant emite desde el panel para que una sesion EXTERNA (script, WebFetch)
/// configure Contenedores/Conectores/Agentes sin la UI Blazor. El token en claro se muestra UNA
/// sola vez al emitirlo; aqui SOLO se guarda su hash SHA-256 (hex), nunca el valor. La resolucion
/// del tenant a partir del Bearer es la unica lectura cross-tenant (por hash opaco); una vez
/// resuelto, el filtro global de tenant aisla todo el trabajo. TENANT-SCOPED.
/// </summary>
public class TenantApiToken : TenantEntity
{
    /// <summary>Nombre legible para identificar el token en el listado (ej. "script Siigo").</summary>
    public string Name { get; set; } = null!;

    /// <summary>SHA-256 (hex, 64 chars) del token en claro. UNICO: por el se resuelve el tenant.</summary>
    public string TokenHash { get; set; } = null!;

    /// <summary>Alcance del token (ej. "admin"). Reservado para granularidad futura.</summary>
    public string Scope { get; set; } = "admin";

    /// <summary>Cuando se revoco (UTC). null = activo. Un token revocado no resuelve tenant (401).</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Ultima vez que se uso con exito (UTC), para auditoria operativa.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }
}
