using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Un PASO de la secuencia de reactivacion. Tras <see cref="OffsetHoras"/> horas de inactividad (contadas
/// desde el ULTIMO mensaje del cliente), se emite un mensaje. Dentro de la ventana de 24h de Meta se usa
/// <see cref="MensajeTexto"/> (texto libre); fuera de ella se usa <see cref="Plantilla"/> (HSM aprobada). Si
/// el paso no tiene plantilla y la ventana esta cerrada, el paso se OMITE (nunca texto libre &gt;24h).
/// </summary>
public sealed record AgentReactivacionPaso(
    int OffsetHoras = 24,
    string? MensajeTexto = null,
    string? Plantilla = null,
    string? Idioma = null,
    bool Habilitado = true);

/// <summary>
/// Configuracion de REACTIVACION del agente. Se serializa a <see cref="Domain.Entities.AiAgent.ReactivacionJson"/>.
/// Mismo patron que <see cref="AgentCierreConfig"/> (JSON en el agente, sin tabla de pasos).
/// </summary>
public sealed record AgentReactivacionConfig(
    bool Habilitada = false,
    IReadOnlyList<AgentReactivacionPaso>? Pasos = null)
{
    public static readonly AgentReactivacionConfig Empty = new(false, Array.Empty<AgentReactivacionPaso>());

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Parsea el JSON almacenado. Null/vacio/invalido =&gt; configuracion vacia (sin reactivacion).</summary>
    public static AgentReactivacionConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Empty; }
        try
        {
            var cfg = JsonSerializer.Deserialize<AgentReactivacionConfig>(json, JsonOpts);
            return cfg is null ? Empty : cfg with { Pasos = cfg.Pasos ?? Array.Empty<AgentReactivacionPaso>() };
        }
        catch { return Empty; }
    }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>Pasos HABILITADOS ordenados por horas de inactividad (el orden en que deben dispararse).</summary>
    [JsonIgnore]
    public IReadOnlyList<AgentReactivacionPaso> PasosActivos =>
        (Pasos ?? Array.Empty<AgentReactivacionPaso>())
            .Where(p => p.Habilitado && p.OffsetHoras >= 0)
            .OrderBy(p => p.OffsetHoras)
            .ToList();

    /// <summary>true si no hay nada que hacer (deshabilitada o sin pasos activos).</summary>
    [JsonIgnore]
    public bool IsNoop => !Habilitada || PasosActivos.Count == 0;
}
