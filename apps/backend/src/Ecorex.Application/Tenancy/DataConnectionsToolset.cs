using System.Text.Json;
using Ecorex.Application.Tenancy.DataConnections;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Herramienta (function calling / "MCP") de DATOS EXTERNOS: permite al agente de IA usar los DATASETS
/// que el tenant marco como "disponibles para agentes" (AgentEnabled). El agente puede listarlos, ver sus
/// parametros (con descripcion) y EJECUTARLOS con los valores que llene, recibiendo las filas resultantes.
///
/// Aislado por tenant y con el mismo gate del servicio (<see cref="ITenantDataConnectionService"/>): solo
/// datasets propios del tenant, habilitados y expuestos a agentes. La ejecucion respeta el AllowWrite de la
/// conexion (decision del dueño). Nunca expone la cadena de conexion (cifrada) ni el SQL crudo.
/// </summary>
public interface IDataConnectionsToolset : IAgentToolset { }

public sealed class DataConnectionsToolset : IDataConnectionsToolset
{
    private const int AgentMaxRows = 200;

    private readonly ITenantDataConnectionService _svc;

    public DataConnectionsToolset(ITenantDataConnectionService svc) => _svc = svc;

    public string GroupKey => "datos";
    public string GroupLabel => "Datos externos (datasets)";

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            "listar_datasets",
            "Lista los DATASETS (consultas guardadas a bases de datos externas) que este tenant expuso a los " +
            "agentes. Usala para saber que consultas tienes disponibles. Devuelve, por cada dataset: id, " +
            "nombre, descripcion, la conexion a la que pertenece y cuantos parametros pide.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new AiToolSpec(
            "ver_dataset",
            "Devuelve el DETALLE de un dataset por su id (el que entrega 'listar_datasets'): nombre, " +
            "descripcion, conexion y la lista de PARAMETROS con su nombre, tipo, descripcion y valor por " +
            "defecto. Usala ANTES de ejecutar para saber que valores debes llenar.",
            """{"type":"object","properties":{"dataset_id":{"type":"string","description":"Id (GUID) del dataset, tal como lo devuelve listar_datasets"}},"required":["dataset_id"],"additionalProperties":false}"""),
        new AiToolSpec(
            "ejecutar_dataset",
            "EJECUTA un dataset y devuelve sus filas. Pasa 'dataset_id' y 'parametros' como un objeto " +
            "{nombre: valor} con los parametros que pide el dataset (usa 'ver_dataset' para conocerlos); los " +
            "que no envies usan su valor por defecto. Devuelve 'columnas' y 'filas' (o un 'error' legible).",
            """{"type":"object","properties":{"dataset_id":{"type":"string","description":"Id (GUID) del dataset"},"parametros":{"type":"object","description":"Valores de los parametros como {nombre: valor}. Opcional si el dataset no tiene parametros.","additionalProperties":true}},"required":["dataset_id"],"additionalProperties":false}"""),
    };

    public async Task<AgentToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch { return Err("Los argumentos no son un JSON valido."); }

        try
        {
            return toolName switch
            {
                "listar_datasets" => await ListarAsync(cancellationToken),
                "ver_dataset" => await VerAsync(args, cancellationToken),
                "ejecutar_dataset" => await EjecutarAsync(args, actorUserId, cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    private async Task<AgentToolResult> ListarAsync(CancellationToken ct)
    {
        var list = await _svc.AgentListDatasetsAsync(ct);
        var payload = list.Select(d => new
        {
            id = d.Id,
            nombre = d.Name,
            descripcion = d.Description,
            conexion = d.ConnectionName,
            num_parametros = d.ParameterCount
        });
        return Ok(new { datasets = payload });
    }

    private async Task<AgentToolResult> VerAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGetId(args, "dataset_id", out var id)) { return Err("Falta 'dataset_id' (GUID)."); }
        var d = await _svc.AgentGetDatasetAsync(id, ct);
        if (d is null) { return Err("Dataset no encontrado o no disponible para agentes."); }
        return Ok(new
        {
            id = d.Id,
            nombre = d.Name,
            descripcion = d.Description,
            conexion = d.ConnectionName,
            parametros = d.Parameters.Select(p => new
            {
                nombre = p.Name,
                tipo = p.Type,
                descripcion = p.Description,
                valor_por_defecto = p.DefaultValue
            })
        });
    }

    private async Task<AgentToolResult> EjecutarAsync(JsonElement args, Guid actorUserId, CancellationToken ct)
    {
        if (!TryGetId(args, "dataset_id", out var id)) { return Err("Falta 'dataset_id' (GUID)."); }

        Dictionary<string, string?>? inputs = null;
        if (args.TryGetProperty("parametros", out var pj) && pj.ValueKind == JsonValueKind.Object)
        {
            inputs = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var prop in pj.EnumerateObject())
            {
                inputs[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Null => null,
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => prop.Value.GetRawText()
                };
            }
        }

        var result = await _svc.AgentRunDatasetAsync(id, inputs, AgentMaxRows, actorUserId, ct);
        if (!result.Ok) { return Err(result.Error ?? "No se pudo ejecutar el dataset."); }
        var grid = result.Grid!;
        return Ok(new
        {
            columnas = grid.Columns,
            filas = grid.Rows,
            total_filas = grid.RowCount,
            truncado = grid.Truncated
        });
    }

    private static bool TryGetId(JsonElement args, string name, out Guid id)
    {
        id = Guid.Empty;
        return args.TryGetProperty(name, out var el)
            && el.ValueKind == JsonValueKind.String
            && Guid.TryParse(el.GetString(), out id);
    }

    private static AgentToolResult Ok(object payload) =>
        new(JsonSerializer.Serialize(payload, JsonOut), false);

    private static AgentToolResult Err(string message) =>
        new(JsonSerializer.Serialize(new { error = message }, JsonOut), false);
}
