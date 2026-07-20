using System.Text;
using System.Text.Json;
using Ecorex.Contracts.Agent;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Falla al compilar un flujo: el mensaje es el que ve el operador (falta destino, falta secreto para
/// firmar, ...). Se distingue de una excepcion cualquiera para poder cerrarlo en la bitacora con un
/// motivo claro en vez de un stack trace.
/// </summary>
public sealed class ScrapeCompileException(string message) : Exception(message);

/// <summary>Un paso Extract compilado: en que accion del plan quedo su Eval, a que tabla va, y con que
/// mapeo campo-del-resultado -> columna. El runtime, cuando llegue el resultado, toma el Value de esa
/// accion (un arreglo JSON de filas) y lo ingiere en la tabla.</summary>
public sealed record ExtractBinding(int ActionIndex, Guid TargetContainerId, string? MappingJson);

/// <summary>El flujo listo para empujar: la secuencia de acciones tipadas y los enganches de ingesta.</summary>
public sealed record CompiledFlow(IReadOnlyList<BrowserAction> Actions, IReadOnlyList<ExtractBinding> Extracts);

/// <summary>
/// Traduce un <see cref="ScrapeFlow"/> configurado a la secuencia de <see cref="BrowserAction"/> que el
/// sub-agente Navegador ejecuta (doc 03 s1, Ola 3 - solo el plano DETERMINISTA; el paso Ai es Ola 4).
///
/// Tres cosas pasan aqui, en este orden, y el orden importa por seguridad:
///   1. Sustituye las variables {{VAR}} con su valor real (los secretos ya vienen descifrados).
///   2. FIRMA el JS resultante (Eval/Extract/Click/condicion de Wait) con el secreto del agente. Se
///      firma DESPUES de sustituir para que la firma cubra el JS EXACTO que se ejecuta; el agente
///      rechaza (fail-closed) cualquier JS sin firma valida.
///   3. Registra los enganches de ingesta de los pasos Extract.
///
/// Es una funcion pura (sin BD, sin red): recibe el flujo ya cargado, el diccionario de variables ya
/// descifrado y el secreto del agente; el runtime (IBrowserRunService) le da esos insumos y consume el
/// plan. Asi se puede probar la compilacion sin colmena ni agente.
/// </summary>
public static class ScrapeFlowCompiler
{
    public static CompiledFlow Compile(
        ScrapeFlow flow,
        IReadOnlyDictionary<string, string> variables,
        string correlationId,
        string? signingSecret)
        => CompileSteps(flow.Steps.OrderBy(s => s.Order), flow.ContainerId, variables, correlationId, signingSecret);

    /// <summary>Compila un SUBCONJUNTO de pasos (un segmento determinista de un flujo) a acciones. El
    /// runtime secuencial (Ola 4) lo usa para despachar de a un tramo entre pasos de IA; los indices de
    /// los ExtractBinding son relativos a las acciones de ESTE tramo.</summary>
    public static CompiledFlow CompileSteps(
        IEnumerable<ScrapeStep> orderedSteps,
        Guid? defaultContainer,
        IReadOnlyDictionary<string, string> variables,
        string correlationId,
        string? signingSecret)
    {
        var actions = new List<BrowserAction>();
        var extracts = new List<ExtractBinding>();

        foreach (var step in orderedSteps)
        {
            switch (step.Kind)
            {
                case ScrapeStepKind.Navigate:
                    {
                        var url = Substitute(step.Url, variables);
                        if (string.IsNullOrWhiteSpace(url))
                        {
                            throw new ScrapeCompileException($"El paso '{step.Name}' (Navegar) no tiene URL.");
                        }
                        actions.Add(new BrowserAction(BrowserActionKind.Navigate, Url: url));
                        break;
                    }

                case ScrapeStepKind.InjectScript:
                    {
                        var js = Substitute(RequireScript(step, "Inyectar JS"), variables)!;
                        actions.Add(EvalSigned(js, correlationId, signingSecret, step));
                        break;
                    }

                case ScrapeStepKind.Extract:
                    {
                        var js = Substitute(RequireScript(step, "Extraer"), variables)!;
                        var container = step.TargetContainerId ?? defaultContainer
                            ?? throw new ScrapeCompileException(
                                $"El paso '{step.Name}' (Extraer) no tiene tabla destino (ni el flujo tampoco).");
                        actions.Add(EvalSigned(js, correlationId, signingSecret, step));
                        // El Eval que acabamos de agregar es el que devuelve las filas: su indice es el ancla.
                        extracts.Add(new ExtractBinding(actions.Count - 1, container, step.MappingJson));
                        break;
                    }

                case ScrapeStepKind.Wait:
                    {
                        // Espera por selector (condicion JS firmada) y/o por milisegundos. Al menos uno.
                        string? condition = null;
                        if (!string.IsNullOrWhiteSpace(step.Selector))
                        {
                            condition = $"!!document.querySelector({JsString(step.Selector!)})";
                        }
                        if (condition is null && step.WaitMs is not > 0)
                        {
                            throw new ScrapeCompileException(
                                $"El paso '{step.Name}' (Esperar) no tiene ni selector ni milisegundos.");
                        }
                        var sig = condition is null ? null : Sign(condition, correlationId, signingSecret, step);
                        actions.Add(new BrowserAction(BrowserActionKind.Wait, WaitMs: step.WaitMs, ConditionScript: condition, Signature: sig));
                        break;
                    }

                case ScrapeStepKind.Click:
                    {
                        if (string.IsNullOrWhiteSpace(step.Selector))
                        {
                            throw new ScrapeCompileException($"El paso '{step.Name}' (Clic) no tiene selector.");
                        }
                        // El Navegador consume un guion MouseBot: un arreglo de {action, selector}.
                        var scriptJson = JsonSerializer.Serialize(new[] { new { action = "click", selector = step.Selector } });
                        var sig = Sign(scriptJson, correlationId, signingSecret, step);
                        actions.Add(new BrowserAction(BrowserActionKind.Mouse, ScriptJson: scriptJson, Signature: sig));
                        break;
                    }

                case ScrapeStepKind.Screenshot:
                    actions.Add(new BrowserAction(BrowserActionKind.Screenshot, Screenshot: true));
                    break;

                case ScrapeStepKind.Ai:
                    // La orquestacion del paso de IA es Ola 4 (doc 03 s2): no es una accion tipada, es un
                    // bucle agente<->navegador por el MCP local. Aqui NO se compila; se rechaza claro.
                    throw new ScrapeCompileException(
                        $"El paso '{step.Name}' es de IA; su ejecucion llega en una ola posterior (Ola 4).");

                default:
                    throw new ScrapeCompileException($"Tipo de paso no soportado: {step.Kind}.");
            }

            // "Espera tras el paso" (el TIEMPO del legacy): para los pasos que no son Wait, si el
            // operador pidio una pausa, se agrega una espera simple (sin condicion, no necesita firma).
            if (step.Kind != ScrapeStepKind.Wait && step.WaitMs is > 0)
            {
                actions.Add(new BrowserAction(BrowserActionKind.Wait, WaitMs: step.WaitMs));
            }
        }

        return new CompiledFlow(actions, extracts);
    }

    private static BrowserAction EvalSigned(string js, string correlationId, string? secret, ScrapeStep step)
    {
        // El JS del operador es un CUERPO de funcion: escribe `return [...]` para devolver las filas.
        // El Navegador lo corre con WebView2 ExecuteScriptAsync, donde un `return` en el nivel superior es
        // error de sintaxis; por eso se envuelve en un IIFE ANTES de firmar (la firma cubre el JS exacto
        // que se ejecuta). El salto de linea final evita que un comentario `//` al final se coma el cierre.
        var body = "(function(){\n" + js + "\n})()";
        return new(BrowserActionKind.Eval, Script: body, Signature: Sign(body, correlationId, secret, step));
    }

    private static string RequireScript(ScrapeStep step, string kindLabel)
    {
        if (string.IsNullOrWhiteSpace(step.Script))
        {
            throw new ScrapeCompileException($"El paso '{step.Name}' ({kindLabel}) no tiene script.");
        }
        return step.Script!;
    }

    /// <summary>Firma el JS EXACTO que se va a ejecutar. Sin secreto no se puede firmar y el agente lo
    /// rechazaria: se corta aqui con un mensaje que el operador entiende.</summary>
    private static string Sign(string payload, string correlationId, string? secret, ScrapeStep step)
    {
        if (string.IsNullOrEmpty(secret))
        {
            throw new ScrapeCompileException(
                $"El paso '{step.Name}' inyecta JS pero el agente asignado no tiene secreto para firmarlo. " +
                "Regenera el secreto del cliente (se muestra una sola vez).");
        }
        return AgentSign.SignJs(secret, correlationId, payload);
    }

    /// <summary>Reemplaza {{Nombre}} por su valor. Lo que no tenga variable definida se deja tal cual
    /// (no se inventa un valor vacio: es mas util que el operador vea el placeholder sin resolver).</summary>
    private static string? Substitute(string? input, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(input) || variables.Count == 0 || !input.Contains("{{")) { return input; }
        var sb = new StringBuilder(input);
        foreach (var (name, value) in variables)
        {
            sb.Replace("{{" + name + "}}", value);
        }
        return sb.ToString();
    }

    private static string JsString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
