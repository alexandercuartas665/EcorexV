using System.Data;
using System.Data.Common;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.External;
using Ecorex.Domain.Enums;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Ecorex.Infrastructure.External;

/// <summary>
/// Ejecutor ADO.NET del conector externo (ADR-0064). Vive en Infrastructure porque tiene los drivers
/// (Microsoft.Data.SqlClient / Npgsql). FUERZA la lectura:
/// - Guarda de solo lectura sobre el texto curado (<see cref="ExternalReadOnlyGuard"/>), defensa en
///   profundidad ademas del usuario de BD de solo lectura exigido por politica.
/// - En Postgres abre una transaccion READ ONLY real (el servidor bloquea escrituras).
/// - Los parametros se enlazan TIPADOS (DbParameter), jamas por concatenacion.
///
/// Nunca persiste ni loggea la cadena de conexion.
/// </summary>
public sealed class AdoExternalQueryExecutor : IExternalQueryExecutor
{
    public async Task<ReportDataSet> ExecuteAsync(ExternalQuery query, CancellationToken ct = default)
    {
        // Defensa en profundidad: rechaza cualquier cosa que no sea una lectura antes de abrir conexion.
        ExternalReadOnlyGuard.EnsureReadOnly(query.CommandText);

        await using var conn = CreateConnection(query.Provider, query.ConnectionString);
        await conn.OpenAsync(ct);

        await using var tx = query.Provider == ExternalDataProvider.Postgres
            ? await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : null;

        if (tx is not null && query.Provider == ExternalDataProvider.Postgres)
        {
            // Transaccion READ ONLY real: el servidor rechaza cualquier escritura.
            await using var readOnly = conn.CreateCommand();
            readOnly.Transaction = tx;
            readOnly.CommandText = "SET TRANSACTION READ ONLY";
            await readOnly.ExecuteNonQueryAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = query.CommandText;
        cmd.CommandType = CommandType.Text;
        cmd.CommandTimeout = query.TimeoutSeconds;
        if (tx is not null)
        {
            cmd.Transaction = tx;
        }

        BindParameters(cmd, query.Parameters);

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
        var dataSet = await MaterializeAsync(reader, query.MaxRows, ct);

        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        return dataSet;
    }

    public async Task<string?> TestConnectionAsync(ExternalDataProvider provider, string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var conn = CreateConnection(provider, connectionString);
            await conn.OpenAsync(ct);

            await using var tx = provider == ExternalDataProvider.Postgres
                ? await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                : null;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = 15;
            if (tx is not null)
            {
                cmd.Transaction = tx;
            }

            _ = await cmd.ExecuteScalarAsync(ct);
            if (tx is not null)
            {
                await tx.RollbackAsync(ct);
            }

            return null;
        }
        catch (Exception ex)
        {
            // Mensaje legible SIN la cadena de conexion (que puede llevar el secreto).
            return $"No se pudo conectar a la fuente externa: {ex.Message}";
        }
    }

    private static DbConnection CreateConnection(ExternalDataProvider provider, string connectionString) => provider switch
    {
        ExternalDataProvider.SqlServer => new SqlConnection(connectionString),
        ExternalDataProvider.Postgres => new NpgsqlConnection(connectionString),
        _ => throw new ReportValidationException($"Proveedor externo no soportado: {provider}.")
    };

    private static void BindParameters(DbCommand cmd, IReadOnlyList<ExternalBoundParameter> parameters)
    {
        foreach (var p in parameters)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = p.Name.StartsWith('@') ? p.Name : "@" + p.Name;
            param.Value = p.Value ?? DBNull.Value;
            // El tipo lo infiere el driver del valor .NET; los null van como DBNull tipado por el motor.
            cmd.Parameters.Add(param);
        }
    }

    private static async Task<ReportDataSet> MaterializeAsync(DbDataReader reader, int maxRows, CancellationToken ct)
    {
        var columns = new List<ReportColumn>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(new ReportColumn(reader.GetName(i), reader.GetName(i), MapClrType(reader.GetFieldType(i))));
        }

        var rows = new List<IReadOnlyList<object?>>();
        while (rows.Count < maxRows && await reader.ReadAsync(ct))
        {
            var values = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var v = reader.GetValue(i);
                values[i] = v is DBNull ? null : v;
            }

            rows.Add(values);
        }

        return new ReportDataSet(columns, rows);
    }

    private static ReportFieldType MapClrType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(bool)) { return ReportFieldType.Boolean; }
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset)) { return ReportFieldType.Date; }
        if (t == typeof(decimal) || t == typeof(double) || t == typeof(float)) { return ReportFieldType.Decimal; }
        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)) { return ReportFieldType.Number; }
        return ReportFieldType.Text;
    }
}
