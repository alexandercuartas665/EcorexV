using System.Diagnostics;
using System.Text.RegularExpressions;
using Ecorex.Application.Tenancy;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Tunel de desarrollo basado en cloudflared (quick tunnel, sin cuenta). Lanza el binario,
/// captura la URL publica trycloudflare.com y la expone. Singleton.
/// </summary>
public sealed class CloudflaredTunnel : IDevTunnel, IDisposable
{
    private static readonly Regex UrlRegex = new(@"https://[a-zA-Z0-9-]+\.trycloudflare\.com", RegexOptions.Compiled);
    private readonly object _lock = new();
    private Process? _process;
    private string? _publicUrl;

    public bool IsRunning => _process is { HasExited: false };
    public string? PublicUrl => _publicUrl;

    public async Task<string?> StartAsync(int port, CancellationToken cancellationToken = default)
    {
        Stop();

        var exe = ResolveCloudflared();
        if (exe is null) { return null; }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"tunnel --url http://localhost:{port}",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        void OnData(object? _, DataReceivedEventArgs e)
        {
            if (e.Data is null) { return; }
            var m = UrlRegex.Match(e.Data);
            if (m.Success) { tcs.TrySetResult(m.Value); }
        }
        proc.OutputDataReceived += OnData;
        proc.ErrorDataReceived += OnData;

        lock (_lock)
        {
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            _process = proc;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using (linked.Token.Register(() => tcs.TrySetResult(null)))
        {
            _publicUrl = await tcs.Task;
        }
        return _publicUrl;
    }

    public void Stop()
    {
        lock (_lock)
        {
            try { if (_process is { HasExited: false }) { _process.Kill(entireProcessTree: true); } }
            catch { /* ya termino */ }
            _process?.Dispose();
            _process = null;
            _publicUrl = null;
        }
    }

    private static string? ResolveCloudflared()
    {
        var overridePath = Environment.GetEnvironmentVariable("ECOREX_CLOUDFLARED_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)) { return overridePath; }

        // PATH
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared");
                if (File.Exists(candidate)) { return candidate; }
            }
            catch { /* dir invalido */ }
        }

        // winget Packages (Windows)
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var pkgRoot = Path.Combine(local, "Microsoft", "WinGet", "Packages");
            try
            {
                if (Directory.Exists(pkgRoot))
                {
                    var found = Directory.EnumerateFiles(pkgRoot, "cloudflared.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (found is not null) { return found; }
                }
            }
            catch { /* sin permisos */ }
        }
        return null;
    }

    public void Dispose() => Stop();
}
