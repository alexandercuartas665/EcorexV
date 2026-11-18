using Ecorex.Domain.Enums;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Clasifica el resultado de cada paso dentro de una corrida completa "paso a paso" (Ejecutar ahora /
/// programacion) y arma el detalle de cierre (2026-11-16).
/// <para>Caso real: DRON PLATAFORMAS INGENIOS de SOLDARCO con el dominio fuera de la allow-list del agente.
/// Los 8 pasos que abrian o leian el portal fallaron ("Dominio no permitido por la allow-list local",
/// "Inyeccion de JS no permitida en este dominio") y aun asi la corrida cerraba Ok con "sin filas en: ...":
/// el bloqueo se veia como una corrida sana.</para>
/// <para>BENIGNO (se anota "sin filas" y la corrida sigue Ok): pasos guarda/driver que no encuentran filas
/// (Exploracion/Variable), esperas agotadas (p.ej. "Esperar fin de descargas" del DRON DIAN, cuyas descargas
/// siguen en 2do plano) y demas Ok=false de pasos intermedios.</para>
/// <para>DURO (la corrida cierra en Error): la navegacion fallida (sin pagina lo demas no sirve), el rechazo
/// por politica del agente (allow-list de dominios / inyeccion de JS bloqueada) y una excepcion del runtime.</para>
/// </summary>
public static class FlowStepOutcome
{
    private const int MaxErrorChars = 160;

    // Textos con que la colmena rechaza una orden por su politica local (allow-list de dominios / JS).
    private static readonly string[] PolicyMarkers =
    {
        "allow-list",
        "no permitida en este dominio",
        "no permitido en este dominio",
    };

    /// <summary>True si el error es un rechazo por la politica local del agente (dominio o JS no permitido).</summary>
    public static bool IsPolicyRejection(string? error) =>
        !string.IsNullOrWhiteSpace(error)
        && PolicyMarkers.Any(m => error.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>True si el fallo del paso debe dejar la corrida en Error (ver reglas en el resumen de la clase).
    /// El offline y el OTP tienen su propio camino (abortan antes) y no pasan por aqui.</summary>
    public static bool IsHardFailure(ScrapeStepKind kind, StepRunResult r, bool threw)
    {
        if (r.Ok || r.Offline) { return false; }
        return threw || kind == ScrapeStepKind.Navigate || IsPolicyRejection(r.Error ?? r.Detail);
    }

    /// <summary>Nota corta "paso: error" para el detalle de cierre.</summary>
    public static string HardNote(string stepName, StepRunResult r)
    {
        var err = (r.Error ?? r.Detail ?? "error sin detalle").Trim();
        if (err.Length > MaxErrorChars) { err = err[..(MaxErrorChars - 3)] + "..."; }
        return $"{stepName}: {err}";
    }

    /// <summary>Detalle de cierre. Sin errores duros es el texto de siempre; con errores duros los pone
    /// primero ("N paso(s) con error: ...") para que se vean en la bitacora y en el flujo.</summary>
    public static string Summarize(int stepCount, IReadOnlyList<string> hardErrors, IReadOnlyList<string> sinFilas)
    {
        var filas = sinFilas.Count > 0 ? $"; sin filas en: {string.Join(", ", sinFilas)}" : "";
        if (hardErrors.Count == 0)
        {
            return $"Flujo ejecutado paso a paso ({stepCount} pasos){filas}.";
        }
        return $"{hardErrors.Count} paso(s) con error: {string.Join("; ", hardErrors)}"
            + $" (flujo de {stepCount} pasos{filas}).";
    }
}
