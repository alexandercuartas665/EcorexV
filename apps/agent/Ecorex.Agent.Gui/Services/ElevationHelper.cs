using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Ecorex.Agent.Gui.Services;

/// <summary>
/// Elevacion por UAC para GUARDAR la identidad del agente. La boveda (ADR-0039) es machine-scope con
/// ACL SYSTEM+Administradores: cambiar la identidad EXIGE elevacion (no se debilita). La colmena se
/// auto-lanza tras el MSI SIN elevar, asi que su conexion por pipe no es de administrador y el
/// servicio la rechaza. En vez de fallar, esta clase relanza el propio Gui.exe ELEVADO con
/// <c>--save-config</c> (que escribe la boveda directo y sale): el usuario ve UN prompt de UAC y el
/// servicio recoge el cambio por su FileSystemWatcher. Ver ADR-0050.
/// </summary>
public static class ElevationHelper
{
    /// <summary>true si el proceso actual ya corre con token de administrador (no hace falta relanzar).</summary>
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false; // fail-closed: si no se puede saber, se asume no elevado y se pedira UAC
        }
    }

    /// <summary>
    /// Relanza este mismo ejecutable ELEVADO (Verb=runas) con <c>--save-config</c> para escribir la
    /// boveda. Devuelve el resultado del intento (aceptado / cancelado / error), para que la colmena
    /// lo refleje en su mini-log. El secreto vacio significa "conserva el actual" (lo resuelve
    /// --save-config leyendo la boveda), asi el secreto solo viaja por linea de comandos cuando es NUEVO.
    /// </summary>
    public static ElevationResult SaveConfigElevated(string clientId, string hubUrl, string secret)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return new ElevationResult(false, "No se pudo determinar la ruta del ejecutable.");
        }

        var args = new List<string> { "--save-config", clientId, hubUrl };
        if (!string.IsNullOrEmpty(secret)) { args.Add(secret); }

        return RunElevated(exe, args);
    }

    /// <summary>
    /// Guarda una capacidad (allow-list + consentimiento) ELEVADA, con un UNICO prompt de UAC, via
    /// <c>--save-caps &lt;kind&gt; &lt;0|1&gt; &lt;entries&gt;</c>. Es el equivalente de <see cref="SaveConfigElevated"/>
    /// para el flyout de Navegador/Archivos: sin esto, el flyout intentaba escribir por el pipe (conexion
    /// no-admin) y el Servicio lo rechazaba, por lo que la lista NO se persistia.
    /// </summary>
    public static ElevationResult SaveCapabilityElevated(string kind, bool enabled, IEnumerable<string> entries)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return new ElevationResult(false, "No se pudo determinar la ruta del ejecutable.");
        }
        var joined = string.Join(",", entries.Select(e => e.Trim()).Where(e => e.Length > 0));
        return RunElevated(exe, new List<string> { "--save-caps", kind, enabled ? "1" : "0", joined });
    }

    /// <summary>Relanza el propio exe ELEVADO (Verb=runas) con los argumentos dados y espera a que termine.</summary>
    private static ElevationResult RunElevated(string exe, List<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true, // requerido para Verb=runas (dispara el prompt de UAC)
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var a in args) { psi.ArgumentList.Add(a); }

        try
        {
            using var p = Process.Start(psi);
            p?.WaitForExit(15000); // la escritura de la boveda es instantanea; no colgar la UI
            return new ElevationResult(true, null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: el usuario rechazo el UAC
        {
            return new ElevationResult(false, "Se cancelo la confirmacion de administrador (UAC).");
        }
        catch (Exception ex)
        {
            return new ElevationResult(false, ex.Message);
        }
    }
}

/// <summary>Resultado del intento de guardado elevado: si se ejecuto y, si no, el motivo.</summary>
public readonly record struct ElevationResult(bool Ok, string? Error);
