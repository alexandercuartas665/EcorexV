using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Rules;

/// <summary>
/// Limpieza del TTL del historial de reglas (ADR-0016): borra RuleExecutionLog con
/// ExpiresAt vencido. Es el UNICO DELETE fisico permitido en el modulo (log con TTL de 90
/// dias documentado). Cross-tenant a proposito (IgnoreQueryFilters): corre en el worker de
/// fondo sin tenant en sesion y el vencimiento aplica a todos por igual.
/// </summary>
public interface IRuleExecutionLogCleaner
{
    /// <summary>Borra los logs expirados a la fecha dada. Devuelve filas eliminadas.</summary>
    Task<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

public sealed class RuleExecutionLogCleaner : IRuleExecutionLogCleaner
{
    private readonly IApplicationDbContext _db;

    public RuleExecutionLogCleaner(IApplicationDbContext db)
    {
        _db = db;
    }

    public Task<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        => _db.RuleExecutionLogs
            .IgnoreQueryFilters()
            .Where(l => l.ExpiresAt < now)
            .ExecuteDeleteAsync(cancellationToken);
}
