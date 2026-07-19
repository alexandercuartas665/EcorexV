namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de un paso de un flujo de extraccion (modulo 000730, capitulo "Extraccion de Datos").
/// Los pasos DETERMINISTAS mapean 1:1 a las acciones tipadas del sub-agente Navegador
/// (BrowserActionKind); el paso <see cref="Ai"/> es una orquestacion (un agente maneja el
/// navegador por el MCP local), no una accion tipada. El runtime que los ejecuta se documenta
/// aparte (doc 03 del capitulo) y es DIFERIDO: aqui solo se modela la CONFIGURACION.
/// </summary>
public enum ScrapeStepKind
{
    /// <summary>Ir a una URL (puede llevar variables {{VAR}}). -> BrowserAction.Navigate.</summary>
    Navigate = 0,

    /// <summary>Inyectar JS (el servidor lo FIRMA al ejecutar); no extrae por si solo. -> Eval.</summary>
    InjectScript = 1,

    /// <summary>Inyectar JS que devuelve filas + mapeo a columnas; el resultado se ingiere. -> Eval + ingesta.</summary>
    Extract = 2,

    /// <summary>Esperar un tiempo y/o a que un selector/condicion aparezca. -> BrowserAction.Wait.</summary>
    Wait = 3,

    /// <summary>Clic sobre un selector o coordenadas. -> BrowserAction.Mouse.</summary>
    Click = 4,

    /// <summary>Captura de pantalla (diagnostico/evidencia). -> BrowserAction.Screenshot.</summary>
    Screenshot = 5,

    /// <summary>Paso de IA: un agente maneja el navegador por el MCP local segun una instruccion,
    /// acotado por allow-list de tools y topes de pasos/tiempo. NO es una accion tipada.</summary>
    Ai = 6
}

/// <summary>
/// Que hacer si la ETIQUETA de advertencia de un paso aparece en lo que devuelve (el
/// CONDICION/advertencia del legacy). Ola 5.
/// </summary>
public enum ScrapeWarningAction
{
    /// <summary>Sin advertencia.</summary>
    None = 0,

    /// <summary>Si aparece la etiqueta, se anota en la bitacora pero el flujo sigue.</summary>
    Notify = 1,

    /// <summary>Si aparece la etiqueta, se DETIENE la corrida (p.ej. "captcha", "sesion expirada").</summary>
    Stop = 2
}
