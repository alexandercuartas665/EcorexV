using Microsoft.Extensions.Logging;

namespace Ecorex.Agent.Service;

/// <summary>
/// Proveedor de log a ARCHIVO minimo (sin NuGet). El servicio corre headless/elevado: arrancado
/// suelto (Start-Process) su consola no se ve, y como servicio Windows el log va al Visor de eventos.
/// Ninguno de los dos es comodo para diagnosticar la CONEXION en el equipo del cliente. Este sink deja
/// SIEMPRE una copia en un archivo de ruta fija y mundo-legible (Documentos publicos), para que el
/// operador -o soporte- lea de un vistazo por que el canal quedo Offline (config vacia, secreto
/// rechazado, URL del hub mal escrita). No reemplaza a los otros sinks; los acompana.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>Ruta fija del log de diagnostico: <c>%PUBLIC%\Documents\ecorex-agent-diag.log</c>.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "ecorex-agent-diag.log");

    private readonly string _path;
    private readonly object _gate = new();

    public FileLoggerProvider(string path)
    {
        _path = path;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); }
            File.AppendAllText(_path,
                $"{Environment.NewLine}{DateTimeOffset.Now:O} === Agente ECOREX: arranque, log de diagnostico iniciado ==={Environment.NewLine}");
        }
        catch { /* si no se puede escribir, los demas sinks siguen; nunca tumba el arranque */ }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, Write);

    private void Write(string line)
    {
        lock (_gate)
        {
            try { File.AppendAllText(_path, line + Environment.NewLine); } catch { /* best-effort */ }
        }
    }

    public void Dispose() { }

    private sealed class FileLogger(string category, Action<string> write) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) { return; }
            var msg = formatter(state, exception);
            var shortCat = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
            var line = $"{DateTimeOffset.Now:HH:mm:ss.fff} [{logLevel}] {shortCat}: {msg}";
            if (exception is not null) { line += Environment.NewLine + exception; }
            write(line);
        }
    }
}
