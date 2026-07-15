using System.Globalization;
using System.Runtime.CompilerServices;
using Ecorex.Contracts.Agent;
using Microsoft.Data.SqlClient;

namespace Ecorex.Agent.Gui.Services;

/// <summary>Fallo de ejecucion del Gateway con codigo estable para el FetchFailed.</summary>
public sealed class GatewayException : Exception
{
    public GatewayException(string code, string message, bool retryable = false) : base(message)
    {
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }
    public bool Retryable { get; }
}

/// <summary>
/// Ola C (doc 05 Ola 2): ejecuta un <c>FetchRequest.query</c> parametrizado y de SOLO LECTURA contra
/// una fuente SQL Server de la LAN, leyendo por lotes y devolviendo <c>FetchResult</c> en chunks. La
/// cadena de conexion (con credencial) la aporta el AGENTE localmente (nunca viaja por el canal).
/// </summary>
public sealed class SqlServerGatewayExecutor
{
    public async IAsyncEnumerable<FetchResultMsg> ExecuteAsync(
        string connectionString,
        string correlationId,
        QuerySpec query,
        PagingSpec? paging,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (ok, error) = QueryGuard.Validate(query.Text);
        if (!ok)
        {
            throw new GatewayException("QUERY_REJECTED", error ?? "Consulta no permitida.");
        }

        var pageSize = Math.Clamp(paging?.PageSize ?? 500, 1, 5000);
        var maxRows = paging?.MaxRows > 0 ? paging.MaxRows : 100000;

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(query.Text, conn)
        {
            CommandTimeout = Math.Max(0, query.TimeoutSeconds),
        };
        if (query.Params is not null)
        {
            foreach (var kv in query.Params)
            {
                cmd.Parameters.AddWithValue("@" + kv.Key, (object?)kv.Value ?? DBNull.Value);
            }
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var fields = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            fields[i] = reader.GetName(i);
        }

        var batch = new List<Dictionary<string, string?>>(pageSize);
        var chunkIndex = 0;
        var total = 0;
        var firstChunk = true;

        FetchResultMsg BuildChunk(bool isLast)
        {
            var msg = new FetchResultMsg(correlationId, chunkIndex, isLast, firstChunk ? fields : null,
                new List<Dictionary<string, string?>>(batch), batch.Count);
            chunkIndex++;
            firstChunk = false;
            batch = new List<Dictionary<string, string?>>(pageSize);
            return msg;
        }

        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, string?>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                row[fields[i]] = value is DBNull or null
                    ? null
                    : Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            batch.Add(row);
            total++;

            if (total >= maxRows) { break; }
            if (batch.Count >= pageSize) { yield return BuildChunk(isLast: false); }
        }

        // Ultimo chunk (o uno vacio si no hubo filas), marcado isLast.
        yield return BuildChunk(isLast: true);
    }
}
