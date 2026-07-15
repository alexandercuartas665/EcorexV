namespace Ecorex.Application.Admin;

/// <summary>Resultado de ejecutar una query en la consola SQL admin.
/// Si fue SELECT, llena Columnas + Filas. Si fue DML/DDL, llena RowsAffected.</summary>
public sealed record SqlConsoleExecutionDto(
    bool Success,
    string QueryType,
    IReadOnlyList<string> Columnas,
    IReadOnlyList<IReadOnlyList<string?>> Filas,
    int? RowsAffected,
    int? RowsReturned,
    long DurationMs,
    string? ErrorMessage);

public sealed record SqlConsoleLogDto(
    Guid Id,
    Guid? TenantId,
    Guid? UserId,
    string? UserName,
    string Query,
    string? QueryType,
    int? RowsAffected,
    int? RowsReturned,
    long DurationMs,
    bool Success,
    string? ErrorMessage,
    DateTimeOffset ExecutedAt);

/// <summary>Resumen de una tabla del schema public, para el explorador de la consola.
/// FilasEstimadas es la estimacion de pg_stat_user_tables (rapida, no un COUNT exacto).</summary>
public sealed record SqlTableInfoDto(
    string Tabla,
    string Descripcion,
    long FilasEstimadas,
    string Grupo);

/// <summary>
/// Consola SQL de administracion (modulo 000077, /sql-admin). Ejecuta SQL crudo contra la
/// BD del sistema. Herramienta gobernada por AUDITORIA: cada query queda en sql_console_logs
/// con usuario, tenant, tipo, resultado y fecha. Solo accesible con Perm:sql-admin:View
/// (Owner/Admin por poder organico; otros roles solo si se les concede).
/// </summary>
public interface ISqlConsoleService
{
    /// <summary>Ejecuta SQL crudo. SELECT devuelve filas (hasta rowLimit); DML/DDL devuelve filas
    /// afectadas. Registra SIEMPRE en sql_console_logs (exito o error).</summary>
    Task<SqlConsoleExecutionDto> EjecutarAsync(string sql, Guid actorUserId, string? actorUserName,
        int rowLimit = 1000, CancellationToken ct = default);

    Task<IReadOnlyList<SqlConsoleLogDto>> ListarHistorialAsync(int take = 50, CancellationToken ct = default);

    /// <summary>Lista las tablas del schema public con descripcion humana y conteo aproximado.
    /// Solo PostgreSQL (usa pg_stat_user_tables); en otros proveedores devuelve lista vacia.</summary>
    Task<IReadOnlyList<SqlTableInfoDto>> ListarTablasAsync(CancellationToken ct = default);
}
