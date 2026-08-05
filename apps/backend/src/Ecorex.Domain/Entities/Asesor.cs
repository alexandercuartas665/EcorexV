using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Asesor comercial (vendedor) del tenant: catalogo propio, modulo 000074. Es la tabla que
/// alimenta el campo "Vendedor asignado" de los terceros del Directorio General.
///
/// Un asesor NO es necesariamente un usuario/login: puede existir solo como vendedor del
/// catalogo (<see cref="TenantUserId"/> null) o estar VINCULADO a un usuario del tenant
/// (<see cref="TenantUser"/>) para que ese login opere como asesor. La gestion de los
/// usuarios/logins vive aparte, en el modulo de Administracion de usuarios (000073).
///
/// Regla de negocio: un asesor no se puede eliminar si tiene terceros que lo referencian
/// como vendedor asignado (se valida en el servicio; ademas la FK es Restrict).
/// </summary>
public class Asesor : TenantEntity
{
    /// <summary>Nombre visible del asesor (obligatorio).</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Documento de identidad (opcional).</summary>
    public string? Documento { get; set; }

    public string? Email { get; set; }

    public string? Telefono { get; set; }

    /// <summary>Usuario/login del tenant al que esta vinculado este asesor, si alguno. Null =
    /// asesor "suelto" del catalogo, sin login. Apunta a <see cref="TenantUser"/>.</summary>
    public Guid? TenantUserId { get; set; }
    public TenantUser? TenantUser { get; set; }

    /// <summary>Un asesor inactivo no se ofrece para asignar, pero se conserva por historial.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>MARCA: el agente de IA puede ASIGNARLE tareas por reparto (round-robin). Solo los
    /// asesores con esta marca Y vinculados a un usuario (<see cref="TenantUserId"/>) entran en el
    /// reparto automatico; los demas no reciben tareas del agente. No todos tienen esta opcion.</summary>
    public bool AssignableByAgent { get; set; }

    /// <summary>Ultima vez que el agente le asigno una tarea. Es la clave del reparto round-robin:
    /// entre los elegibles, recibe primero el que hace MAS tiempo no recibe (null = nunca -> primero).</summary>
    public DateTimeOffset? LastAgentAssignmentAt { get; set; }
}
