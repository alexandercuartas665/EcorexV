namespace Ecorex.Application.Tenancy;

/// <summary>
/// Datos de CIERRE que el agente de IA logra capturar durante la conversacion. Es un DTO propio del
/// runtime de agentes: NO referencia la entidad Lead ni el dominio CRM, de modo que el motor de
/// inferencia pueda registrar un cierre sin depender del pipeline comercial.
/// </summary>
/// <param name="ContactName">Nombre del cliente (obligatorio).</param>
/// <param name="ContactPhone">Telefono del cliente (opcional; el toolset ya resolvio el default por conversacion).</param>
/// <param name="ChannelHint">Canal/tipo de cliente en texto libre (ej. "b2b", "productos", "cursos").</param>
/// <param name="Summary">Resumen breve de lo que quiere el cliente.</param>
/// <param name="EstimatedValue">Valor estimado de la venta (opcional).</param>
/// <param name="Currency">Moneda del valor estimado (opcional).</param>
public sealed record AgentLeadRequest(
    string ContactName,
    string? ContactPhone,
    string? ChannelHint,
    string? Summary,
    decimal? EstimatedValue,
    string? Currency);

/// <summary>
/// Resultado de intentar registrar un cierre a traves del sink. Wired indica si habia un destino real
/// conectado (adaptador CRM). Con el sink No-Op, Ok es true y Wired es false: el runtime no falla.
/// </summary>
/// <param name="Ok">true si la operacion no produjo error.</param>
/// <param name="Wired">true si un destino real (CRM) proceso el cierre; false para el No-Op.</param>
/// <param name="LeadId">Id del lead creado (solo si Wired y Ok).</param>
/// <param name="BusinessUnitName">Nombre de la unidad de negocio asignada (informativo).</param>
/// <param name="Message">Mensaje informativo para el modelo.</param>
/// <param name="Error">Mensaje de error si Ok es false.</param>
public sealed record AgentLeadResult(
    bool Ok,
    bool Wired,
    Guid? LeadId,
    string? BusinessUnitName,
    string? Message,
    string? Error)
{
    public static AgentLeadResult Success(Guid leadId, string? businessUnitName, string message)
        => new(true, true, leadId, businessUnitName, message, null);

    public static AgentLeadResult Failure(string error)
        => new(false, true, null, null, null, error);

    public static AgentLeadResult NotWired(string message)
        => new(true, false, null, null, message, null);
}

/// <summary>
/// Costura (seam) del cierre comercial para el runtime de agentes de IA. El toolset "crear_lead" depende
/// SOLO de esta interfaz, no de Lead/BusinessUnit/pipeline. Implementaciones:
/// - <c>NoOpAgentLeadSink</c> (default en DI): permite operar el agente sin CRM; no crea nada.
/// - <c>PipelineLeadSink</c> (adaptador CRM, registrado como implementacion viva): conserva el
///   comportamiento actual creando el lead en el embudo comercial.
/// El acoplamiento con el dominio CRM vive en UNA sola clase adaptadora (PipelineLeadSink).
/// </summary>
public interface IAgentLeadSink
{
    Task<AgentLeadResult> CreateLeadAsync(AgentLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sink por defecto: NO crea ningun lead. Permite que el runtime de agentes funcione aunque el tenant
/// no tenga CRM/pipeline conectado. Nunca lanza: devuelve un resultado tipado "no conectado".
/// </summary>
public sealed class NoOpAgentLeadSink : IAgentLeadSink
{
    public Task<AgentLeadResult> CreateLeadAsync(AgentLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentLeadResult.NotWired(
            "El cierre se registro localmente, pero no hay un pipeline comercial conectado para crear el lead."));
}
