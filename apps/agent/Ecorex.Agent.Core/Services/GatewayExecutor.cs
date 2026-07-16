using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using Ecorex.Contracts.Agent;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Ecorex.Agent.Core.Services;

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
/// Ejecuta un <c>FetchRequest.query</c> parametrizado y de SOLO LECTURA contra la fuente de la LAN,
/// leyendo por lotes y devolviendo <c>FetchResult</c> en chunks.
///
/// **Un solo ejecutor para todos los motores**: SqlConnection y NpgsqlConnection derivan de
/// `DbConnection`, asi que la logica (validar, paginar, chunkear, convertir a texto) se escribe UNA
/// vez y lo unico que cambia por motor es que conexion se abre. Duplicarla por motor seria pedir que
/// los chunks de Postgres y los de SQL Server se comporten distinto con el tiempo.
/// </summary>
public sealed class GatewayExecutor
{
    /// <summary>Motores que el agente sabe hablar hoy.</summary>
    public static bool IsSupported(string? dbEngine) => Parse(dbEngine) is not null;

    private static string? Parse(string? dbEngine) => dbEngine?.Trim().ToLowerInvariant() switch
    {
        "sqlserver" or "mssql" => "sqlserver",
        "postgresql" or "postgres" or "npgsql" => "postgresql",
        _ => null,
    };

    /// <summary>
    /// Arma la cadena de conexion a partir de lo que mando el servidor (ADR-0040, opcion a). Devuelve
    /// null si el ConnectorSpec no trae credencial: ahi el llamador cae a la fuente LOCAL del agente
    /// (opcion b), que es como funcionaba la Ola C y se conserva.
    /// </summary>
    public static string? BuildConnectionString(ConnectorSpec? c)
    {
        if (c is null || string.IsNullOrWhiteSpace(c.Secret) || string.IsNullOrWhiteSpace(c.Host)) { return null; }

        var engine = Parse(c.DbEngine);
        return engine switch
        {
            // TrustServerCertificate: las fuentes SQL Server on-prem casi siempre tienen certificado
            // autofirmado, y SqlClient cifra por defecto. Aplica a la BD de la LAN, NO al canal con
            // el servidor. Deberia ser configurable por conector (pendiente).
            "sqlserver" => $"Data Source={c.Host}{(c.Port is > 0 ? "," + c.Port : "")};" +
                           $"Initial Catalog={c.Database};User Id={c.Username};Password={c.Secret};" +
                           "TrustServerCertificate=True;Connection Timeout=15",
            "postgresql" => $"Host={c.Host};Port={(c.Port is > 0 ? c.Port : 5432)};Database={c.Database};" +
                            $"Username={c.Username};Password={c.Secret};Timeout=15",
            _ => null,
        };
    }

    private static DbConnection OpenFor(string? dbEngine, string connectionString) => Parse(dbEngine) switch
    {
        "sqlserver" => new SqlConnection(connectionString),
        "postgresql" => new NpgsqlConnection(connectionString),
        _ => throw new GatewayException("ENGINE_UNSUPPORTED",
            $"El agente no sabe hablar con el motor '{dbEngine}'. Soportados: SqlServer, PostgreSql."),
    };

    public async IAsyncEnumerable<FetchResultMsg> ExecuteAsync(
        string? dbEngine,
        string connectionString,
        string correlationId,
        QuerySpec query,
        PagingSpec? paging,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Solo lectura: la puerta que impide que un servidor comprometido escriba en la BD del
        // cliente. Es agnostica del motor a proposito.
        var (ok, error) = QueryGuard.Validate(query.Text);
        if (!ok)
        {
            throw new GatewayException("QUERY_REJECTED", error ?? "Consulta no permitida.");
        }

        var pageSize = Math.Clamp(paging?.PageSize ?? 500, 1, 5000);
        var maxRows = paging?.MaxRows > 0 ? paging.MaxRows : 100000;

        await using var conn = OpenFor(dbEngine, connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = query.Text;
        cmd.CommandTimeout = Math.Max(0, query.TimeoutSeconds);
        if (query.Params is not null)
        {
            foreach (var kv in query.Params)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = "@" + kv.Key;   // SqlClient y Npgsql aceptan ambos el prefijo @
                p.Value = (object?)kv.Value ?? DBNull.Value;
                cmd.Parameters.Add(p);
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
                // Todo viaja como texto (doc 02 s5): el servidor convierte al leer, segun el tipo de
                // la columna del contenedor. InvariantCulture para que un decimal no cambie de forma
                // segun la configuracion regional del equipo del cliente.
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
