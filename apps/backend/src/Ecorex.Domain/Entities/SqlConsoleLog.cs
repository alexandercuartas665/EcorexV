namespace Ecorex.Domain.Entities;

/// <summary>
/// Auditoria de TODAS las queries ejecutadas via la consola SQL admin (/sql-admin).
/// NO es TenantEntity: guarda eventos que un Owner/Admin puede ejecutar sin filtro de
/// tenant; el TenantId del usuario activo se guarda como DATO (no como filtro).
/// Es append-only: nada en el sistema la borra; es la pista para reconstruir que se
/// hizo directamente contra la BD (regla 5: auditoria inmutable).
/// </summary>
public class SqlConsoleLog
{
    public Guid Id { get; set; }

    /// <summary>Tenant del usuario que ejecuto la query. Null si no habia tenant en sesion.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Id del usuario (plataforma o tenant) que ejecuto.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Email o username del usuario, snapshot.</summary>
    public string? UserName { get; set; }

    /// <summary>Texto SQL ejecutado, tal cual lo escribio el usuario.</summary>
    public string Query { get; set; } = null!;

    /// <summary>Tipo deducido por la primera palabra: SELECT, INSERT, UPDATE, DELETE, DDL, OTHER.</summary>
    public string? QueryType { get; set; }

    /// <summary>Filas afectadas (DML/DDL).</summary>
    public int? RowsAffected { get; set; }

    /// <summary>Filas devueltas (SELECT).</summary>
    public int? RowsReturned { get; set; }

    public long DurationMs { get; set; }

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset ExecutedAt { get; set; }
}
