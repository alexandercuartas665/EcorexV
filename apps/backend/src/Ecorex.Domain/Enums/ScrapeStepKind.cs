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
    Ai = 6,

    // ---- Tipos heredados del legacy WEB_SCRAPING_RS.TIPO ("el dron"), para paridad. Se conservan como
    // variantes seleccionables; en el runtime se comportan como Extract (los que producen datos) o
    // InjectScript (los que solo ejecutan JS). Ints > 6 para no chocar con los nativos. ----

    /// <summary>Legacy 'Tabla': el script devuelve filas que se ingieren (como Extract).</summary>
    Tabla = 7,

    /// <summary>Legacy 'TablaID': igual que Tabla identificando filas por id.</summary>
    TablaID = 8,

    /// <summary>Legacy 'Exploracion': explora/alimenta el paso (devuelve datos).</summary>
    Exploracion = 9,

    /// <summary>Legacy 'Ensamblado': invoca una Regla de negocio con el resultado.</summary>
    Ensamblado = 10,

    /// <summary>Legacy 'WeBresponse': captura la respuesta web.</summary>
    WebResponse = 11,

    /// <summary>Legacy 'EjecutarSQL': ejecuta un SQL de proceso.</summary>
    EjecutarSql = 12,

    /// <summary>Legacy 'Variable': el script fija una variable (como InjectScript).</summary>
    Variable = 13,

    /// <summary>Legacy 'Api': invoca una API configurada.</summary>
    Api = 14,

    /// <summary>Legacy 'tramite': paso de tramite.</summary>
    Tramite = 15,

    /// <summary>Legacy 'cerran dron': cierra el dron al terminar.</summary>
    CerrarDron = 16,

    /// <summary>Legacy 'mouse': accion de mouse (como Click).</summary>
    Mouse = 17,

    /// <summary>Leer un OTP/token de un buzon por correo (IMAP, server-side) y ponerlo en una variable del
    /// flujo. Para logins que mandan un codigo al correo. La config (buzon, remitente, asunto, regex,
    /// variable, timeout) va en MappingJson del paso.</summary>
    LeerCorreoOtp = 18,

    /// <summary>Procesar los archivos YA descargados (server-side): lee una carpeta, parsea cada archivo
    /// segun un formato (p.ej. factura DIAN UBL de un ZIP) y vuelca los datos a un destino, sin duplicar
    /// (llave configurable, p.ej. CUFE). Opcional: auto-importar al modulo destino y mover los procesados.
    /// La config (carpeta, patron, formato, destino, llave, autoimportar, mover) va en MappingJson.</summary>
    ProcesarArchivos = 19,

    /// <summary>Ingesta por API del proveedor de facturacion electronica NEWTON (recepcion, server-side):
    /// autentica con un Auth-Token, lista los documentos electronicos recibidos y DESCARGA cada uno como
    /// {CUFE}.xml y {CUFE}_newton.pdf en una carpeta propia (configurable). No aterriza en el modulo por si
    /// solo (de eso se encarga ProcesarArchivos / una fase posterior). La config (variables de URL y token,
    /// carpeta, dias atras, bajar xml/pdf, tope, solo nuevos) va en MappingJson.</summary>
    IngestaNewton = 20,

    /// <summary>Cerrar el navegador (server-side): cierra la SESION VIVA del navegador que el modo paso a paso
    /// mantiene abierta para conservar el login, y la saca de la cache del agente. Se cablea a la accion ya
    /// existente <c>BrowserActionKind.CloseSession</c>. Util como ultimo paso de un flujo para no dejar la
    /// ventana WebView2 abierta. No navega ni inyecta; no lleva configuracion.</summary>
    CerrarNavegador = 21,

    /// <summary>Crear actividad (server-side): crea UNA tarea del concepto configurado por el registro que
    /// el flujo tiene en el contexto (p.ej. el pedido que leyo el bucle) y deja el formulario del concepto
    /// diligenciado (cabecera + grilla de items). No duplica: anota el numero de la tarea en una columna de
    /// la fila del Contenedor que tiene la clave y la omite si ya la tiene. La config (concepto, titulo,
    /// clave, contenedor/columnas, mapeo de campos y de la grilla, guarda) va en MappingJson.</summary>
    CrearActividad = 22,

    /// <summary>Notificar (server-side): avisa a uno o varios usuarios que la tarea del contexto ({{TAREA}}, la deja
    /// "Crear actividad") ya existe, por el canal elegido (campana, correo, plantilla de WhatsApp, grupo de
    /// WhatsApp o Telegram) con un mensaje-plantilla con los tokens de la tarea. Por defecto solo si la tarea es
    /// nueva en esta corrida. La config va en MappingJson (ADR-0168).</summary>
    Notificar = 23
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
