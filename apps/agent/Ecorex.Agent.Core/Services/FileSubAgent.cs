using System.IO;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Sub-agente Archivos (doc 06 s3.2): ejecuta acciones TIPADAS de archivos/directorios (List/Read/
/// Write/Delete/Exists/MakeDir). Seguridad (doc 06 s4): TODO se acota a las rutas raiz de la
/// <see cref="FileAllowList"/> LOCAL -canonicalizando la ruta para impedir traversal (`..`); nada
/// fuera de las raices. No es un shell generico. `Read` tiene tope de tamano.
/// </summary>
public sealed class FileSubAgent
{
    private const long MaxReadBytes = 1_048_576;   // 1 MB (texto)
    private const long MaxBinaryBytes = 5_242_880; // 5 MB (binario -> base64)

    private readonly FileAllowList _allow = new();
    private readonly CapabilityConsent _consent = new();

    public bool IsAllowed(string? path) => TryResolve(path, out _, out _);

    public Task<FileResultMsg> ExecuteAsync(FileRequestMsg req)
    {
        // Consentimiento local (doc 06 s4): sin habilitar por el operador, no se tocan archivos.
        if (!_consent.IsFilesEnabled())
        {
            var blocked = req.Actions
                .Select((a, i) => new FileActionResult(i, a.Kind, Ok: false, Error: "Archivos no habilitado por el operador en la colmena."))
                .ToList();
            return Task.FromResult(new FileResultMsg(req.CorrelationId, false, blocked, "Archivos no habilitado por el operador."));
        }

        var results = new List<FileActionResult>(req.Actions.Count);
        for (var i = 0; i < req.Actions.Count; i++)
        {
            var action = req.Actions[i];
            try
            {
                results.Add(RunAction(i, action));
            }
            catch (Exception ex)
            {
                results.Add(new FileActionResult(i, action.Kind, Ok: false, Error: ex.Message));
            }
        }
        return Task.FromResult(new FileResultMsg(req.CorrelationId, results.All(r => r.Ok), results));
    }

    private FileActionResult RunAction(int index, FileAction a)
    {
        if (!TryResolve(a.Path, out var full, out var canWrite))
        {
            return Fail(index, a, $"Ruta fuera de la allow-list local: {a.Path}");
        }

        // Least privilege (doc 06 s4): una raiz es de SOLO LECTURA salvo que se marque con "rw:".
        if (a.Kind is FileActionKind.Write or FileActionKind.Delete or FileActionKind.MakeDir && !canWrite)
        {
            return Fail(index, a, $"Raiz de solo lectura: {a.Kind} exige una raiz marcada 'rw:' en la allow-list.");
        }

        switch (a.Kind)
        {
            case FileActionKind.List:
            {
                if (!Directory.Exists(full)) { return Fail(index, a, "El directorio no existe."); }
                var entries = new List<FileEntry>();
                foreach (var d in Directory.GetDirectories(full))
                {
                    entries.Add(new FileEntry(Path.GetFileName(d), IsDirectory: true, Size: 0));
                }
                foreach (var f in Directory.GetFiles(full))
                {
                    var fi = new FileInfo(f);
                    entries.Add(new FileEntry(fi.Name, IsDirectory: false, fi.Length));
                }
                return new FileActionResult(index, a.Kind, Ok: true, Entries: entries);
            }

            case FileActionKind.Read:
            {
                if (!File.Exists(full)) { return Fail(index, a, "El archivo no existe."); }
                var len = new FileInfo(full).Length;
                if (len > MaxReadBytes) { return Fail(index, a, $"Archivo demasiado grande ({len} bytes > {MaxReadBytes})."); }
                return new FileActionResult(index, a.Kind, Ok: true, Value: File.ReadAllText(full));
            }

            case FileActionKind.ReadBytes:
            {
                if (!File.Exists(full)) { return Fail(index, a, "El archivo no existe."); }
                var blen = new FileInfo(full).Length;
                if (blen > MaxBinaryBytes) { return Fail(index, a, $"Archivo demasiado grande ({blen} bytes > {MaxBinaryBytes})."); }
                return new FileActionResult(index, a.Kind, Ok: true, Value: Convert.ToBase64String(File.ReadAllBytes(full)));
            }

            case FileActionKind.Write:
            {
                var parent = Path.GetDirectoryName(full);
                if (parent is null || !TryResolve(parent, out _, out _))
                {
                    return Fail(index, a, "La carpeta destino esta fuera de la allow-list.");
                }
                File.WriteAllText(full, a.Content ?? string.Empty);
                return new FileActionResult(index, a.Kind, Ok: true, Value: $"{(a.Content ?? string.Empty).Length} chars escritos");
            }

            case FileActionKind.Delete:
            {
                if (!File.Exists(full)) { return Fail(index, a, "El archivo no existe."); }
                File.Delete(full);
                return new FileActionResult(index, a.Kind, Ok: true, Value: "borrado");
            }

            case FileActionKind.Exists:
            {
                var kind = Directory.Exists(full) ? "dir" : File.Exists(full) ? "file" : "none";
                return new FileActionResult(index, a.Kind, Ok: true, Value: kind);
            }

            case FileActionKind.MakeDir:
            {
                Directory.CreateDirectory(full);
                return new FileActionResult(index, a.Kind, Ok: true, Value: "creado");
            }

            default:
                return Fail(index, a, "Accion no soportada.");
        }
    }

    /// <summary>
    /// Canonicaliza la ruta y verifica que caiga DENTRO de alguna raiz permitida. Devuelve tambien si
    /// esa raiz admite escritura (prefijo "rw:"). Si la ruta cae en varias raices, gana la que permita
    /// escritura.
    /// </summary>
    private bool TryResolve(string? path, out string full, out bool canWrite)
    {
        full = string.Empty;
        canWrite = false;
        if (string.IsNullOrWhiteSpace(path)) { return false; }
        try { full = Path.GetFullPath(path); } catch { return false; }

        var found = false;
        foreach (var root in _allow.LoadRoots())
        {
            string r;
            try { r = Path.GetFullPath(root.Path).TrimEnd(Path.DirectorySeparatorChar); } catch { continue; }
            if (full.Equals(r, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                if (root.CanWrite) { canWrite = true; }
            }
        }
        return found;
    }

    private static FileActionResult Fail(int index, FileAction a, string error)
        => new(index, a.Kind, Ok: false, Error: error);
}
