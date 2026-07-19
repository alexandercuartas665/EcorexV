using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Flujo de extraccion por navegador (modulo 000730, capitulo "Extraccion de Datos", Ola 1).
///
/// Es el maestro que el operador disena en la web: una lista ordenada de <see cref="ScrapeStep"/>
/// que el sub-agente Navegador de la colmena ejecuta, y unas <see cref="ScrapeVariable"/> cifradas
/// para las credenciales. El resultado aterriza en una tabla de un Contenedor de datos
/// (<see cref="ContainerId"/>), y lo ejecuta el agente identificado por <see cref="ClientId"/>.
///
/// Sucede al maestro legacy WEB_SCRAPING colapsando sus 10 tablas: destino, identidad del agente y
/// (mas adelante) horario NO se remodelan porque YA existen en ECOREX (DataContainer, DataClient,
/// ImportProcess). Alcance de esta ola: la CONFIGURACION; el runtime es diferido (doc 03).
/// TENANT-SCOPED.
/// </summary>
public class ScrapeFlow : TenantEntity
{
    /// <summary>Nombre visible del flujo ("Precios competencia Homecenter").</summary>
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>URL de arranque del flujo.</summary>
    public string StartUrl { get; set; } = null!;

    /// <summary>Estado (Active/Inactive/Error). Reusa el enum del scraper simple.</summary>
    public ScrapeSourceStatus Status { get; set; } = ScrapeSourceStatus.Active;

    /// <summary>Agente de la colmena que lo ejecuta (el "bot asignado"). Misma identidad que el
    /// Gateway. NO ACTION al borrar el cliente: el flujo queda sin agente, no se borra.</summary>
    public Guid? ClientId { get; set; }
    public DataClient? Client { get; set; }

    /// <summary>Tabla destino por defecto de las extracciones del flujo (un paso puede apuntar a otra
    /// via ScrapeStep.TargetContainerId).</summary>
    public Guid? ContainerId { get; set; }
    public DataContainer? Container { get; set; }

    /// <summary>Ultima corrida registrada (se llena cuando exista el runtime).</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>Resumen humano de la ultima corrida.</summary>
    public string? LastResultSummary { get; set; }

    // ---- Paginacion controlada (Ola 5, el PAGINA_DESDE/HASTA del legacy) ----

    /// <summary>Nombre de la variable de pagina ({{PAGINA}}). Si esta puesto junto con
    /// <see cref="PageFrom"/>/<see cref="PageTo"/>, el runtime repite el flujo por cada pagina,
    /// sustituyendo esta variable con el numero de pagina.</summary>
    public string? PageVar { get; set; }

    /// <summary>Primera pagina (inclusive).</summary>
    public int? PageFrom { get; set; }

    /// <summary>Ultima pagina (inclusive).</summary>
    public int? PageTo { get; set; }

    public ICollection<ScrapeStep> Steps { get; set; } = new List<ScrapeStep>();
    public ICollection<ScrapeVariable> Variables { get; set; } = new List<ScrapeVariable>();
}

/// <summary>
/// Un paso de un <see cref="ScrapeFlow"/>. Tabla unica con discriminador <see cref="Kind"/>: cada
/// tipo usa el subconjunto de campos que le aplica (los demas quedan null), igual que ImportProcess
/// con Interval/Cron. Los pasos deterministas mapean a acciones tipadas del Navegador; el paso Ai es
/// una orquestacion. TENANT-SCOPED. Vive y muere con el flujo.
/// </summary>
public class ScrapeStep : TenantEntity
{
    public Guid FlowId { get; set; }
    public ScrapeFlow? Flow { get; set; }

    /// <summary>Orden de ejecucion (el operador reordena arrastrando).</summary>
    public int Order { get; set; }

    public ScrapeStepKind Kind { get; set; }

    /// <summary>Titulo del paso ("Login con credenciales").</summary>
    public string Name { get; set; } = null!;

    /// <summary>Espera tras el paso, en milisegundos.</summary>
    public int? WaitMs { get; set; }

    // ---- Deterministas ----

    /// <summary>URL destino (Navigate). Puede llevar {{VAR}}.</summary>
    public string? Url { get; set; }

    /// <summary>JS a inyectar (InjectScript / Extract / condicion de Wait). El servidor lo FIRMA al
    /// ejecutar (nunca se ejecuta sin firma valida en el agente).</summary>
    public string? Script { get; set; }

    /// <summary>Selector CSS (Click / Wait por elemento).</summary>
    public string? Selector { get; set; }

    /// <summary>Extract: mapeo campo-del-resultado -> columna de la tabla destino (JSON). Reusa el
    /// patron de DataConnector.MappingJson.</summary>
    public string? MappingJson { get; set; }

    // ---- Paso de IA (Kind = Ai) ----

    /// <summary>Instruccion en lenguaje natural para el agente ("saca la tabla de precios").</summary>
    public string? Instruction { get; set; }

    /// <summary>Tabla destino de este paso; si es null, usa la del flujo.</summary>
    public Guid? TargetContainerId { get; set; }

    /// <summary>Que tools browser.* puede usar el agente en este paso (JSON de un arreglo de nombres).
    /// Acota lo que el agente puede hacer.</summary>
    public string? ToolAllowListJson { get; set; }

    /// <summary>Tope de iteraciones del agente en este paso.</summary>
    public int? MaxSteps { get; set; }

    /// <summary>Tope de tiempo del agente en este paso, en segundos.</summary>
    public int? MaxSeconds { get; set; }

    /// <summary>Proveedor de IA a usar, entre los que habilite el Super Admin (AI Provider Gateway).
    /// Referencia SUAVE (sin FK dura) para no acoplar el dominio al modelo del gateway todavia.</summary>
    public Guid? AiProviderId { get; set; }

    /// <summary>Modelo del proveedor elegido.</summary>
    public string? AiModel { get; set; }

    // ---- Advertencia (Ola 5, el CONDICION del legacy) ----

    /// <summary>Etiqueta que, si aparece en lo que devuelve el paso, dispara la advertencia
    /// (p.ej. "captcha", "sesion expirada"). Null = sin advertencia.</summary>
    public string? WarningLabel { get; set; }

    /// <summary>Que hacer si aparece la etiqueta: nada, notificar (sigue) o detener la corrida.</summary>
    public ScrapeWarningAction WarningAction { get; set; } = ScrapeWarningAction.None;
}

/// <summary>
/// Variable de un flujo, sustituida como {{Name}} en los scripts. Las sensibles (usuario/clave/token)
/// van cifradas con ISecretProtector y NUNCA se devuelven en claro; el servidor las descifra y
/// sustituye justo antes de firmar el script. TENANT-SCOPED. Vive y muere con el flujo.
/// </summary>
public class ScrapeVariable : TenantEntity
{
    public Guid FlowId { get; set; }
    public ScrapeFlow? Flow { get; set; }

    /// <summary>Nombre que se usa como {{Name}} en los scripts.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Valor cifrado (ISecretProtector) si es secreto; en claro si no lo es.</summary>
    public string? ValueEncrypted { get; set; }

    /// <summary>Si es sensible: se pinta con candado y no se puede consultar (solo reescribir/rotar).</summary>
    public bool IsSecret { get; set; }
}

/// <summary>
/// Bitacora de UNA corrida de un <see cref="ScrapeFlow"/> (runtime, Ola 3). Misma razon de ser que
/// <c>ImportRun</c>: un flujo corre sin nadie mirando y sin registro un fallo es indistinguible de
/// "no habia datos". Es DEDICADA (no se reusa ImportRun) porque ImportRun cuelga de un ImportProcess
/// -que es la programacion, atada a conectores del Contenedor- y el disparo manual de un flujo no
/// tiene proceso; cuando la Ola 5 cablee la programacion, un ImportProcess podra apuntar al flujo sin
/// remodelar esto. El puente entre el despacho (Running) y el cierre (que llega por el hub, despues y
/// por otro camino) es el <see cref="CorrelationId"/>. TENANT-SCOPED. Vive y muere con el flujo.
/// </summary>
public class ScrapeFlowRun : TenantEntity
{
    public Guid FlowId { get; set; }
    public ScrapeFlow? Flow { get; set; }

    /// <summary>Cuando se disparo (UTC).</summary>
    public DateTimeOffset FiredAt { get; set; }

    /// <summary>Quien la disparo (Manual = "Ejecutar ahora"; Scheduled = horario, Ola 5). Reusa el
    /// enum de las corridas de importacion.</summary>
    public ImportRunTrigger Trigger { get; set; }

    /// <summary>En que quedo. Reusa el enum de las corridas de importacion (Running/Ok/Error/
    /// PendingOffline).</summary>
    public ImportRunResult Result { get; set; } = ImportRunResult.Running;

    /// <summary>Id de la orden empujada al agente; puente entre el despacho y el cierre.</summary>
    public string? CorrelationId { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Cuantos pasos tenia el flujo al compilarlo (para el resumen).</summary>
    public int StepCount { get; set; }

    /// <summary>Filas ingeridas por los pasos Extract (sumadas).</summary>
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }

    /// <summary>Mensaje humano del resultado o del fallo.</summary>
    public string? Detail { get; set; }
}
