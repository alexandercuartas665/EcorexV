using System.IO;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Gui.Services;

/// <summary>
/// Sub-agente Archivos (doc 06 s3.2): ejecuta acciones TIPADAS de archivos/directorios (List/Read/
/// Write/Delete/Exists/MakeDir). Seguridad (doc 06 s4): TODO se acota a las rutas raiz de la
/// <see cref="FileAllowList"/> LOCAL -canonicalizando la ruta para impedir traversal (`..`); nada
/// fuera de las raices. No es un shell generico. `Read` tiene tope de tamano.
/// </summary>
public sealed class FileSubAgent
{
    private const long MaxReadBytes = 1_048_576; // 1 MB

    private readonly FileAllowList _allow = new();

    public bool IsAllowed(string? path) => TryResolve(path, out _);

    public Task<FileResultMsg> ExecuteAsync(FileRequestMsg req)
    {
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
        if (!TryResolve(a.Path, out var full))
        {
            return Fail(index, a, $"Ruta fuera de la allow-list local: {a.Path}");
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

            case FileActionKind.Write:
            {
                var parent = Path.GetDirectoryName(full);
                if (parent is null || !TryResolve(parent, out _))
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

    /// <summary>Canonicaliza la ruta y verifica que caiga DENTRO de alguna raiz permitida.</summary>
    private bool TryResolve(string? path, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) { return false; }
        try { full = Path.GetFullPath(path); } catch { return false; }

        foreach (var root in _allow.Load())
        {
            string r;
            try { r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); } catch { continue; }
            if (full.Equals(r, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static FileActionResult Fail(int index, FileAction a, string error)
        => new(index, a.Kind, Ok: false, Error: error);
}
