namespace Ecorex.Domain.Enums;

/// <summary>Estado de una cuenta de usuario de plataforma (Notas dev sec.1.5).</summary>
public enum PlatformUserStatus
{
    Active,
    Invited,
    Blocked,
    Suspended,
    /// <summary>
    /// Cuenta creada por auto-registro pero todavia no activada (el usuario debe ingresar el
    /// codigo enviado por correo). No puede iniciar sesion hasta cambiar a Active.
    /// </summary>
    PendingActivation,
    /// <summary>
    /// Baja logica: el usuario fue "eliminado" de la empresa por un administrador. NO se borra
    /// la fila porque de ella cuelgan tareas, notas y auditoria; conservarla mantiene el historial
    /// legible. Un TenantUser en este estado no puede iniciar sesion (AuthService exige Active) y
    /// queda fuera de los selectores de usuario. No hace falta migracion en ninguno de los dos
    /// motores: este enum se persiste como TEXTO (HaveConversion&lt;string&gt; en EcorexDbContext),
    /// asi que un valor nuevo solo agrega una cadena posible y no altera los existentes.
    /// </summary>
    Removed
}
