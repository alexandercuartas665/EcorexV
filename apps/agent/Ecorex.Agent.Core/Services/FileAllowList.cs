namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Allow-list LOCAL de rutas raiz que el sub-agente Archivos puede tocar (doc 06 s4: least privilege;
/// nada fuera de las rutas permitidas, aunque la nube lo pida). Una ruta por linea; la persistencia
/// cifrada la gobierna <see cref="AgentVault"/> (ADR-0039). Fail-closed si esta vacia. La verificacion
/// de "dentro de una raiz" (canonicalizando, sin traversal) la hace el motor.
///
/// PERMISOS POR RAIZ (least privilege): por defecto una raiz es de SOLO LECTURA. Para permitir
/// escritura/borrado se antepone el prefijo <c>rw:</c>. Ejemplo:
/// <code>
/// C:\Datos            -> solo lectura (list/read)
/// rw:C:\Salida        -> lectura y escritura (write/delete/mkdir)
/// </code>
/// </summary>
public sealed class FileAllowList
{
    private const string FileName = "file-allow.dat";

    /// <summary>Una raiz permitida y si admite escritura.</summary>
    public sealed record Root(string Path, bool CanWrite);

    /// <summary>Raices parseadas (resuelve el prefijo <c>rw:</c>).</summary>
    public IReadOnlyList<Root> LoadRoots()
    {
        var roots = new List<Root>();
        foreach (var raw in Load())
        {
            var entry = raw.Trim();
            if (entry.Length == 0) { continue; }
            var canWrite = false;
            if (entry.StartsWith("rw:", StringComparison.OrdinalIgnoreCase))
            {
                canWrite = true;
                entry = entry[3..].Trim();
            }
            else if (entry.StartsWith("ro:", StringComparison.OrdinalIgnoreCase))
            {
                entry = entry[3..].Trim();
            }
            if (entry.Length > 0) { roots.Add(new Root(entry, canWrite)); }
        }
        return roots;
    }

    public IReadOnlyList<string> Load()
    {
        var plain = AgentVault.ReadText(FileName);
        if (string.IsNullOrEmpty(plain)) { return Array.Empty<string>(); }
        return plain.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public void Save(IEnumerable<string> roots)
    {
        var text = string.Join('\n', roots.Select(r => r.Trim()).Where(r => r.Length > 0).Distinct());
        AgentVault.WriteText(FileName, text);
    }
}
