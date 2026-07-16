using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Allow-list LOCAL de rutas raiz que el sub-agente Archivos puede tocar (doc 06 s4: least privilege;
/// nada fuera de las rutas permitidas, aunque la nube lo pida). Se guarda cifrada con DPAPI en
/// %APPDATA%\Ecorex\Agent\file-allow.dat (una ruta por linea). Fail-closed si esta vacia. La
/// verificacion de "dentro de una raiz" (canonicalizando, sin traversal) la hace el motor.
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

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ecorex", "Agent");
    private static readonly string FilePath = Path.Combine(Dir, "file-allow.dat");

    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { return Array.Empty<string>(); }
            var plain = Encoding.UTF8.GetString(Transform(File.ReadAllBytes(FilePath), encrypt: false));
            return plain.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Save(IEnumerable<string> roots)
    {
        Directory.CreateDirectory(Dir);
        var text = string.Join('\n', roots.Select(r => r.Trim()).Where(r => r.Length > 0).Distinct());
        File.WriteAllBytes(FilePath, Transform(Encoding.UTF8.GetBytes(text), encrypt: true));
    }

    // ---- DPAPI (CurrentUser) via P/Invoke ----

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int cbData; public IntPtr pbData; }

    private const int CryptProtectUiForbidden = 0x1;

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static byte[] Transform(byte[] data, bool encrypt)
    {
        var input = new DataBlob();
        var output = new DataBlob();
        try
        {
            input.pbData = Marshal.AllocHGlobal(data.Length);
            input.cbData = data.Length;
            Marshal.Copy(data, 0, input.pbData, data.Length);
            var ok = encrypt
                ? CryptProtectData(ref input, "EcorexAgentFileAllow", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output);
            if (!ok) { throw new InvalidOperationException("DPAPI fallo: " + Marshal.GetLastWin32Error()); }
            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            if (input.pbData != IntPtr.Zero) { Marshal.FreeHGlobal(input.pbData); }
            if (output.pbData != IntPtr.Zero) { LocalFree(output.pbData); }
        }
    }
}
