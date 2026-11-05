using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Ecorex.Agent.Tool;

/// <summary>
/// Herramienta del Agente Colmena. Dos modos:
///   uninstall  -> purga TODO rastro del agente (servicio, archivos, registro, atajos, origen de
///                 eventos, entradas ARP huerfanas y -por defecto- la boveda/identidad). Funciona
///                 aunque el agente ya no aparezca en "Programas y caracteristicas" (caso de un MSI
///                 que hizo rollback y dejo huerfanos).
///   verify     -> comprueba que la instalacion quedo bien: archivos presentes + servicio registrado
///                 + servicio en ejecucion (lo arranca con --start). Deja veredicto en un log y, si
///                 es interactivo, en un cuadro de mensaje. La usa el instalador 1.9.5 post-install.
///
/// Las rutas/nombres son el CONTRATO del instalador (ver apps/agent/installer/Product.wxs); se fijan
/// aqui a proposito para que la herramienta funcione SOLA, sin el resto del agente instalado.
/// </summary>
internal static class Program
{
    // ---- Contrato con el instalador (Product.wxs) ----
    private const string ServiceName = "EcorexAgent";
    private const string EventSourceName = "ECOREX Agente";
    private const string RunValueName = "EcorexAgenteColmena";                 // HKLM\...\Run
    private const string EcorexRegKey = @"Software\Ecorex\AgenteColmena";      // HKLM y HKCU
    private const string EcorexRegParent = @"Software\Ecorex";
    private const string VendorFolderName = "Ecorex";
    private const string InstallFolderName = "Agente Colmena";                 // %ProgramFiles%\Ecorex\Agente Colmena
    private const string DataFolderName = "Agent";                            // %ProgramData%\Ecorex\Agent
    private const string StartMenuShortcut = "Ecorex - Agente Colmena.lnk";
    private const string ArpNameNeedle = "Agente Colmena";                     // DisplayName del producto MSI

    private static readonly string[] ProcNames = { "Ecorex.Agent.Gui", "Ecorex.Agent.Service" };

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var cmd = (args.Length > 0 ? args[0] : "").Trim().ToLowerInvariant();
        bool Has(string f) => args.Any(a => string.Equals(a, f, StringComparison.OrdinalIgnoreCase));

        if (cmd is "-h" or "--help" or "/?" or "help") { PrintHelp(); return 0; }

        // Modo "mostrar resultado": NO necesita admin (solo lee el log de verificacion y lo muestra en
        // un cuadro). Lo usa el instalador como accion inmediata/impersonada tras InstallFinalize, para
        // que el usuario vea el veredicto en SU sesion (una CA deferida corre como SYSTEM en la sesion 0,
        // donde un MessageBox seria invisible).
        if (cmd is "show-result") { return ShowResult(); }

        // uninstall/verify tocan servicios, Archivos de programa y HKLM: exigen admin. Si no lo somos,
        // nos RE-LANZAMOS elevados (UAC) con los mismos argumentos. Asi un doble clic para purgar pide
        // UAC una sola vez, en vez de fallar pidiendo "ejecute como administrador". La accion deferida
        // del instalador ya corre como SYSTEM (IsAdmin=true), asi que ahi no hay UAC ni re-lanzamiento.
        if (!IsAdmin())
        {
            return RelaunchElevated(args, interactive: !Has("--quiet") && !Has("-y"));
        }

        switch (cmd)
        {
            case "verify":
                return DoVerify(quiet: Has("--quiet"), tryStart: Has("--start"));

            case "uninstall":
            case "purge":
            case "": // doble clic (sin argumentos) -> purga interactiva
            {
                var quiet = Has("--quiet") || Has("-y");
                // Por defecto se borra TODO, incluida la identidad. --keep-identity la conserva.
                var keepIdentity = Has("--keep-identity");
                if (!quiet && !ConfirmUninstall(keepIdentity)) { Console.WriteLine("Cancelado."); Pause(); return 1; }
                var rc = DoUninstall(keepIdentity);
                if (!quiet) { Pause(); }
                return rc;
            }

            default:
                Console.Error.WriteLine($"Comando no reconocido: {cmd}");
                PrintHelp();
                return 2;
        }
    }

    // ================= UNINSTALL (purga) =================

    private static int DoUninstall(bool keepIdentity)
    {
        var r = new Report();
        Console.WriteLine("== Purga del Agente Colmena de ECOREX ==\n");

        // 1) Detener y eliminar el servicio de Windows.
        StopAndDeleteService(r);

        // 2) Matar procesos (GUI de bandeja + servicio corriendo suelto).
        KillProcesses(r);

        // 3) Intentar desinstalacion MSI limpia de cualquier producto "Agente Colmena" registrado.
        UninstallMsiProducts(r);

        // 4) Borrar la carpeta de instalacion (%ProgramFiles%\Ecorex\Agente Colmena).
        var installDir = Path.Combine(ProgramFiles(), VendorFolderName, InstallFolderName);
        DeleteDirectory(r, "Carpeta de instalacion", installDir);
        RemoveDirIfEmpty(Path.Combine(ProgramFiles(), VendorFolderName));

        // 5) Atajo del Menu Inicio.
        var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), VendorFolderName);
        DeleteFile(r, "Atajo del Menu Inicio", Path.Combine(startMenuDir, StartMenuShortcut));
        RemoveDirIfEmpty(startMenuDir);

        // 6) Autostart (llave Run de la maquina).
        DeleteRegistryValue(r, "Autostart (Run)", RegistryHive.LocalMachine,
            @"Software\Microsoft\Windows\CurrentVersion\Run", RunValueName);

        // 7) Claves propias del agente (HKLM y HKCU).
        DeleteRegistryKey(r, "Clave HKLM\\" + EcorexRegKey, RegistryHive.LocalMachine, EcorexRegKey);
        DeleteRegistryKey(r, "Clave HKCU\\" + EcorexRegKey, RegistryHive.CurrentUser, EcorexRegKey);
        RemoveRegKeyIfEmpty(RegistryHive.LocalMachine, EcorexRegParent);
        RemoveRegKeyIfEmpty(RegistryHive.CurrentUser, EcorexRegParent);

        // 8) Origen del Visor de eventos.
        DeleteRegistryKey(r, "Origen del Visor de eventos", RegistryHive.LocalMachine,
            @"SYSTEM\CurrentControlSet\Services\EventLog\Application\" + EventSourceName);

        // 9) Entradas ARP huerfanas ("Programas y caracteristicas").
        DeleteOrphanArpEntries(r);

        // 10) Identidad / boveda (%ProgramData%\Ecorex\Agent) + diag log.
        var vaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), VendorFolderName, DataFolderName);
        if (keepIdentity)
        {
            r.Add("Boveda/identidad", "CONSERVADA", vaultDir + " (--keep-identity)");
        }
        else
        {
            DeleteDirectory(r, "Boveda/identidad", vaultDir);
            RemoveDirIfEmpty(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), VendorFolderName));
        }
        var diag = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "ecorex-agent-diag.log");
        DeleteFile(r, "Diag log", diag);

        r.Print();
        var quedan = ResidualTrace(installDir, vaultDir, keepIdentity);
        if (quedan.Count == 0)
        {
            Console.WriteLine("\nLISTO: no quedan rastros del agente" + (keepIdentity ? " (se conservo la identidad)." : "."));
            return 0;
        }
        Console.WriteLine("\nATENCION: aun se detectan estos rastros (puede requerir reinicio o cerrar procesos que los bloquean):");
        foreach (var q in quedan) { Console.WriteLine("  - " + q); }
        return 1;
    }

    private static void StopAndDeleteService(Report r)
    {
        if (!ServiceExists())
        {
            r.Add("Servicio EcorexAgent", "NO EXISTE", "nada que quitar");
            return;
        }
        Sc("stop " + ServiceName, out _);
        // Esperar a que pare (hasta ~15s).
        for (var i = 0; i < 15 && ServiceRunning(); i++) { Thread.Sleep(1000); }
        var (code, _) = Sc("delete " + ServiceName, out var outp);
        // sc delete puede devolver 0 (OK) o marcar "marcado para eliminacion" si hay handles abiertos.
        r.Add("Servicio EcorexAgent", code == 0 ? "ELIMINADO" : "MARCADO", (outp ?? "").Trim());
    }

    private static void KillProcesses(Report r)
    {
        var killed = 0;
        foreach (var name in ProcNames)
        {
            foreach (var p in SafeProcessesByName(name))
            {
                try { p.Kill(entireProcessTree: true); p.WaitForExit(5000); killed++; }
                catch { /* best-effort */ }
                finally { p.Dispose(); }
            }
        }
        r.Add("Procesos del agente", killed > 0 ? "TERMINADOS" : "NINGUNO", killed + " proceso(s)");
    }

    private static void UninstallMsiProducts(Report r)
    {
        var done = 0;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var (productCode, display) in FindArpProducts(view))
            {
                var (code, _) = RunExe("msiexec.exe", $"/x {productCode} /qn /norestart", 120000);
                r.Add("Desinstalacion MSI", code is 0 or 3010 ? "OK" : "FALLO(" + code + ")", display + " " + productCode);
                if (code is 0 or 3010) { done++; }
            }
        }
        if (done == 0) { r.Add("Desinstalacion MSI", "N/A", "no hay producto MSI registrado (se limpia a mano)"); }
    }

    private static void DeleteOrphanArpEntries(Report r)
    {
        var removed = 0;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninst = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", writable: true);
            if (uninst is null) { continue; }
            foreach (var sub in uninst.GetSubKeyNames())
            {
                string? name = null;
                try { using var k = uninst.OpenSubKey(sub); name = k?.GetValue("DisplayName") as string; }
                catch { /* ignore */ }
                if (name is not null && name.Contains(ArpNameNeedle, StringComparison.OrdinalIgnoreCase))
                {
                    try { uninst.DeleteSubKeyTree(sub, throwOnMissingSubKey: false); removed++; }
                    catch { /* ignore */ }
                }
            }
        }
        r.Add("Entradas ARP huerfanas", removed > 0 ? "ELIMINADAS" : "NINGUNA", removed + " entrada(s)");
    }

    // ================= VERIFY (post-install) =================

    private static int DoVerify(bool quiet, bool tryStart)
    {
        var installDir = Path.Combine(ProgramFiles(), VendorFolderName, InstallFolderName);
        var svcExe = Path.Combine(installDir, "Ecorex.Agent.Service.exe");
        var guiExe = Path.Combine(installDir, "Ecorex.Agent.Gui.exe");
        var vaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), VendorFolderName, DataFolderName);
        var configDat = Path.Combine(vaultDir, "config.dat");

        var lines = new List<string>();
        var ok = true;

        bool svcExeOk = File.Exists(svcExe), guiExeOk = File.Exists(guiExe);
        lines.Add((svcExeOk ? "[OK]" : "[!!]") + " Servicio (exe): " + svcExe);
        lines.Add((guiExeOk ? "[OK]" : "[!!]") + " Colmena (exe) : " + guiExe);
        if (!svcExeOk || !guiExeOk) { ok = false; }

        var svcExists = ServiceExists();
        lines.Add((svcExists ? "[OK]" : "[!!]") + " Servicio EcorexAgent registrado: " + (svcExists ? "si" : "NO"));
        if (!svcExists) { ok = false; }

        if (svcExists && tryStart && !ServiceRunning())
        {
            Sc("start " + ServiceName, out _);
            for (var i = 0; i < 20 && !ServiceRunning(); i++) { Thread.Sleep(1000); }
        }
        var running = svcExists && ServiceRunning();
        lines.Add((running ? "[OK]" : "[!!]") + " Servicio EcorexAgent en ejecucion: " + (running ? "si" : "NO"));
        if (!running) { ok = false; }

        // La identidad NO es un fallo de instalacion: en una instalacion limpia se configura despues
        // por la colmena. Solo informativo.
        lines.Add((File.Exists(configDat) ? "[OK]" : "[i ]") + " Identidad (config.dat): "
            + (File.Exists(configDat) ? "presente" : "sin configurar todavia (se configura por la colmena)"));

        var verdict = ok ? "INSTALACION OK" : "INSTALACION INCOMPLETA";
        var report = "== Verificacion del Agente Colmena ==\n" + verdict + "\n" + string.Join("\n", lines);
        Console.WriteLine(report);
        WriteVerifyLog(vaultDir, report);

        if (!quiet)
        {
            var hint = ok
                ? "El agente quedo instalado y el servicio esta corriendo.\nAbra la colmena y configure la identidad (ClientId/Hub/Secreto) si falta."
                : "La instalacion NO quedo completa. Revise el detalle y, si hace falta, use la herramienta de purga y reinstale.";
            MessageBox(IntPtr.Zero, report + "\n\n" + hint, "ECOREX - Agente Colmena", ok ? MbInformation : MbWarning);
        }
        return ok ? 0 : 1;
    }

    // Lee el log que dejo 'verify' (lo escribe la CA deferida del instalador como SYSTEM) y lo muestra
    // en un cuadro, en la sesion del usuario. Sin admin: solo lee un archivo. Si no hay log (verify no
    // corrio), informa sin alarmar.
    private static int ShowResult()
    {
        var vaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), VendorFolderName, DataFolderName);
        var log = Path.Combine(vaultDir, "install-verify.log");
        string body;
        uint icon;
        try
        {
            body = File.ReadAllText(log, Encoding.UTF8);
            var ok = body.Contains("INSTALACION OK", StringComparison.Ordinal)
                     && !body.Contains("INCOMPLETA", StringComparison.Ordinal);
            icon = ok ? MbInformation : MbWarning;
            body += ok
                ? "\n\nEl agente quedo instalado y el servicio esta corriendo.\nAbra la colmena y configure la identidad (ClientId/Hub/Secreto) si falta."
                : "\n\nLa instalacion NO quedo completa. Use la herramienta de purga (Ecorex.Agent.Tool.exe uninstall) y reinstale.";
        }
        catch
        {
            body = "No se encontro el resultado de la verificacion.\nRevise el servicio 'EcorexAgent' en services.msc.";
            icon = MbWarning;
        }
        MessageBox(IntPtr.Zero, body, "ECOREX - Agente Colmena", icon);
        return 0;
    }

    private static void WriteVerifyLog(string vaultDir, string report)
    {
        try
        {
            Directory.CreateDirectory(vaultDir);
            File.WriteAllText(Path.Combine(vaultDir, "install-verify.log"),
                DateTimeOffset.Now.ToString("u") + "\n" + report + "\n", Encoding.UTF8);
        }
        catch { /* best-effort: el log es ayuda, no requisito */ }
    }

    // ================= Servicio (via sc.exe, sin dependencias) =================

    private static bool ServiceExists()
    {
        var (code, _) = Sc("query " + ServiceName, out _);
        return code == 0;
    }

    private static bool ServiceRunning()
    {
        var (code, outp) = Sc("query " + ServiceName, out _);
        return code == 0 && (outp ?? "").Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
    }

    private static (int code, string? output) Sc(string args, out string? output)
    {
        var (code, outp) = RunExe("sc.exe", args, 30000);
        output = outp;
        return (code, outp);
    }

    // ================= Registro y archivos (helpers) =================

    private static IEnumerable<(string productCode, string display)> FindArpProducts(RegistryView view)
    {
        var result = new List<(string, string)>();
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var uninst = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        if (uninst is null) { return result; }
        foreach (var sub in uninst.GetSubKeyNames())
        {
            if (!(sub.StartsWith('{') && sub.EndsWith('}'))) { continue; } // solo productos MSI (ProductCode)
            try
            {
                using var k = uninst.OpenSubKey(sub);
                var name = k?.GetValue("DisplayName") as string;
                if (name is not null && name.Contains(ArpNameNeedle, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add((sub, name));
                }
            }
            catch { /* ignore */ }
        }
        return result;
    }

    private static void DeleteDirectory(Report r, string label, string path)
    {
        if (!Directory.Exists(path)) { r.Add(label, "NO EXISTE", path); return; }
        try { Directory.Delete(path, recursive: true); r.Add(label, "ELIMINADA", path); }
        catch (Exception ex) { r.Add(label, "FALLO", path + " :: " + ex.Message); }
    }

    private static void DeleteFile(Report r, string label, string path)
    {
        if (!File.Exists(path)) { r.Add(label, "NO EXISTE", path); return; }
        try { File.Delete(path); r.Add(label, "ELIMINADO", path); }
        catch (Exception ex) { r.Add(label, "FALLO", path + " :: " + ex.Message); }
    }

    private static void DeleteRegistryValue(Report r, string label, RegistryHive hive, string key, string value)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = baseKey.OpenSubKey(key, writable: true);
            if (k?.GetValue(value) is null) { r.Add(label, "NO EXISTE", key + "\\" + value); return; }
            k.DeleteValue(value, throwOnMissingValue: false);
            r.Add(label, "ELIMINADO", key + "\\" + value);
        }
        catch (Exception ex) { r.Add(label, "FALLO", ex.Message); }
    }

    private static void DeleteRegistryKey(Report r, string label, RegistryHive hive, string key)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using (var probe = baseKey.OpenSubKey(key)) { if (probe is null) { r.Add(label, "NO EXISTE", key); return; } }
            baseKey.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
            r.Add(label, "ELIMINADA", key);
        }
        catch (Exception ex) { r.Add(label, "FALLO", ex.Message); }
    }

    private static void RemoveRegKeyIfEmpty(RegistryHive hive, string key)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = baseKey.OpenSubKey(key);
            if (k is not null && k.SubKeyCount == 0 && k.ValueCount == 0)
            {
                baseKey.DeleteSubKey(key, throwOnMissingSubKey: false);
            }
        }
        catch { /* best-effort */ }
    }

    private static void RemoveDirIfEmpty(string path)
    {
        try { if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0) { Directory.Delete(path); } }
        catch { /* best-effort */ }
    }

    private static List<string> ResidualTrace(string installDir, string vaultDir, bool keepIdentity)
    {
        var left = new List<string>();
        if (ServiceExists()) { left.Add("servicio EcorexAgent (reinicie para completar su borrado)"); }
        if (Directory.Exists(installDir)) { left.Add(installDir); }
        if (!keepIdentity && Directory.Exists(vaultDir)) { left.Add(vaultDir); }
        return left;
    }

    // ================= Utilidades =================

    private static string ProgramFiles() => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    private static bool IsAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // Re-lanza esta misma herramienta ELEVADA (verbo runas -> UAC) con los mismos argumentos y espera a
    // que termine, devolviendo su codigo de salida. Si el usuario cancela el UAC, informa y sale con 2.
    private static int RelaunchElevated(string[] args, bool interactive)
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exe))
        {
            Console.Error.WriteLine("No se pudo determinar la ruta del ejecutable para elevar.");
            return 2;
        }
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", args.Select(QuoteArg)),
            };
            using var p = Process.Start(psi);
            if (p is null) { return 2; }
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 1223 = el usuario cancelo el dialogo de UAC.
            Console.Error.WriteLine("Se requiere elevacion (UAC) y fue cancelada. Operacion no realizada.");
            if (interactive) { Pause(); }
            return 2;
        }
    }

    private static string QuoteArg(string a) => a.Contains(' ') && !a.StartsWith('"') ? "\"" + a + "\"" : a;

    private static Process[] SafeProcessesByName(string name)
    {
        try { return Process.GetProcessesByName(name); } catch { return Array.Empty<Process>(); }
    }

    private static (int code, string? output) RunExe(string file, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) { return (-1, null); }
            var so = p.StandardOutput.ReadToEnd();
            var se = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-2, "timeout"); }
            return (p.ExitCode, (so + se));
        }
        catch (Exception ex) { return (-3, ex.Message); }
    }

    private static bool ConfirmUninstall(bool keepIdentity)
    {
        Console.WriteLine("Esto eliminara el Agente Colmena de ECOREX de esta maquina:");
        Console.WriteLine("  - servicio de Windows, procesos, archivos, atajos, autostart y registro.");
        Console.WriteLine(keepIdentity
            ? "  - la IDENTIDAD (config.dat) se CONSERVA (--keep-identity)."
            : "  - la IDENTIDAD (config.dat / boveda) tambien se BORRA.");
        Console.Write("\nContinuar? (S/N): ");
        var key = Console.ReadLine();
        return key is not null && (key.Trim().StartsWith("s", StringComparison.OrdinalIgnoreCase));
    }

    private static void Pause()
    {
        Console.WriteLine("\nPresione Enter para cerrar...");
        try { Console.ReadLine(); } catch { /* sin consola interactiva */ }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
@"Ecorex - Herramienta del Agente Colmena

Uso:
  Ecorex.Agent.Tool.exe uninstall [--keep-identity] [--quiet|-y]
      Purga TODO rastro del agente (servicio, archivos, registro, atajos, origen de eventos,
      entradas ARP huerfanas y, por defecto, la boveda/identidad).
      --keep-identity  conserva %ProgramData%\Ecorex\Agent (config.dat) para no re-configurar.
      --quiet / -y     sin confirmacion ni pausa (desatendido).

  Ecorex.Agent.Tool.exe verify [--start] [--quiet]
      Verifica la instalacion (archivos + servicio + ejecucion). Deja un log en
      %ProgramData%\Ecorex\Agent\install-verify.log.
      --start          intenta arrancar el servicio si esta detenido.
      --quiet          sin cuadro de mensaje (para instalacion silenciosa).

  (Sin argumentos / doble clic) -> purga interactiva (equivale a 'uninstall').

Requiere ejecutarse como ADMINISTRADOR.");
    }

    // ---- Report ----
    private sealed class Report
    {
        private readonly List<(string step, string status, string detail)> _rows = new();
        public void Add(string step, string status, string detail) => _rows.Add((step, status, detail));
        public void Print()
        {
            foreach (var (step, status, detail) in _rows)
            {
                Console.WriteLine($"  [{status,-11}] {step}");
                if (!string.IsNullOrWhiteSpace(detail)) { Console.WriteLine($"               {detail}"); }
            }
        }
    }

    // ---- MessageBox (user32) ----
    private const uint MbInformation = 0x00000040;
    private const uint MbWarning = 0x00000030;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
