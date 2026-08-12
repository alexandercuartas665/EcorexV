using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion CONFIGURABLE de una busqueda de contactos (modulo Cargador de contactos, 000873) que
/// ejecuta el AGENTE COLMENA (navegador on-prem) guiado por un AGENTE IA, y cuyo resultado aterriza
/// como <see cref="ProspectoScrapeado"/> (Bolsa del Directorio, perfil Sospechoso). Es una
/// especializacion del patron <see cref="ScrapeFlow"/>: en vez de guardar filas en un Contenedor de
/// datos generico, clasifica y crea prospectos/leads. TENANT-SCOPED (filtro global por reflexion).
///
/// Las reglas por fuente (redes sociales -> cual; Maps -> pais/region/ciudad) son data-driven: el
/// usuario arma la busqueda una vez en el modal y queda guardada y reutilizable.
/// </summary>
public class ContactSearchDefinition : TenantEntity
{
    /// <summary>Nombre visible de la busqueda configurada.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Fuente donde busca. La UI agrupa LinkedIn/Instagram/Facebook/X como "Redes sociales".</summary>
    public ContactSearchSource SourceType { get; set; } = ContactSearchSource.Maps;

    /// <summary>Terminos de busqueda (que buscar: rubro, cargo, palabra clave...).</summary>
    public string? Query { get; set; }

    /// <summary>Sub-busqueda opcional: refinamiento anidado sobre cada resultado (ej. abrir la ficha y
    /// buscar el telefono). Vacio = sin sub-busqueda.</summary>
    public string? SubQuery { get; set; }

    // ---- Parametros geograficos (sobre todo para Maps / Web local) ----
    public string? Country { get; set; }
    /// <summary>Region/zona dentro del pais (ej. "Norte", "Sur", o un departamento).</summary>
    public string? Region { get; set; }
    public string? City { get; set; }

    /// <summary>
    /// Prompt en lenguaje natural para que el agente IA sepa QUE datos tomar de cada resultado
    /// (nombre, empresa, telefono, correo, ciudad, cargo, metrica...). Es la instruccion de extraccion.
    /// </summary>
    public string ExtractionPrompt { get; set; } = null!;

    /// <summary>Agente COLMENA (DataClient.ClientId) que navega/scrapea. Null = pedir al ejecutar.</summary>
    public string? ClientId { get; set; }

    /// <summary>Agente IA del tenant que maneja el navegador (function-calling) y clasifica el contacto.</summary>
    public Guid? ClassifierAiAgentId { get; set; }

    /// <summary>Tope de contactos a capturar por corrida (defensa: evita barridos enormes). 0 = sin tope.</summary>
    public int MaxContacts { get; set; } = 30;

    /// <summary>Cada cuanto deberia ejecutarse (por ahora solo se guarda; la corrida automatica es ola siguiente).</summary>
    public ContactSearchSchedule Schedule { get; set; } = ContactSearchSchedule.Manual;

    /// <summary>Hora del dia (HH:mm, hora del tenant) a la que corre segun el programador. Null = sin hora fija.</summary>
    public string? RunTime { get; set; }

    /// <summary>Dia de la semana para el programador Semanal (0=Domingo .. 6=Sabado). Null si no aplica.</summary>
    public int? DayOfWeek { get; set; }

    /// <summary>Dia del mes (1..31) para el programador Mensual. Null si no aplica.</summary>
    public int? DayOfMonth { get; set; }

    /// <summary>Ultima vez que se ejecuto (manual o automatica). Base para calcular "vencida" a futuro.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    public bool IsActive { get; set; } = true;
}
