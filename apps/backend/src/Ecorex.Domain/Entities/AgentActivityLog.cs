using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>Que sub-agente de la colmena atendio la orden.</summary>
public enum AgentActivityKind
{
    /// <summary>Navegador (WebView2): navegar/inyectar JS/extraer.</summary>
    Browser,
    /// <summary>Gateway de datos (consulta a una fuente on-prem: SQL Server/PostgreSQL/API).</summary>
    Fetch,
    /// <summary>Sub-agente Archivos.</summary>
    File
}

/// <summary>Como quedo la orden.</summary>
public enum AgentActivityResult { Ok, Error }

/// <summary>
/// Bitacora de actividad de los agentes colmena (ADR-0045, Ola 2): UN registro RESUMEN por orden que el
/// servidor despacha a un agente y ve completar. Centraliza lo que hoy estaba disperso (ScrapeFlowRun para
/// extraccion, ImportRun para contenedores) o era efimero (los hexagonos del panal). Tenant-scoped.
///
/// No reemplaza a las bitacoras de corridas de cada modulo: es una vista transversal "que hizo cada agente"
/// para el modulo de Agentes Colmena. Se escribe best-effort: si falla, NO tumba la orden.
/// </summary>
public class AgentActivityLog : TenantEntity
{
    /// <summary>ClientId publico del agente que atendio (cli_...). Estable aunque la fila del cliente
    /// cambie de nombre o se revoque; por eso se guarda el string y no solo el FK.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Nombre del cliente al momento de la orden (denormalizado para pintar sin join).</summary>
    public string? ClientName { get; set; }

    public AgentActivityKind Kind { get; set; }

    /// <summary>Id de la orden empujada al agente; puente con la bitacora especifica del modulo.</summary>
    public string CorrelationId { get; set; } = "";

    /// <summary>De donde salio la orden, en humano (ej. "Flujo: prueba de conexion", "Contenedor: X").</summary>
    public string? Origin { get; set; }

    public AgentActivityResult Result { get; set; }

    /// <summary>Cuando se despacho (UTC).</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Cuando termino (UTC). null si no cerro (no deberia, se escribe al terminar).</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Duracion en milisegundos (para operar/diagnosticar lentitud).</summary>
    public int DurationMs { get; set; }

    /// <summary>Resumen humano: url visitada + N acciones, N filas, o el mensaje de error.</summary>
    public string? Detail { get; set; }
}
