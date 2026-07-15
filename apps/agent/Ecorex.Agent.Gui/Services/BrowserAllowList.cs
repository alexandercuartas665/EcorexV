using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Ecorex.Agent.Gui.Services;

/// <summary>
/// Allow-list LOCAL de dominios que el sub-agente Navegador puede visitar (doc 06 s4: nada fuera de
/// la lista, aunque la nube lo pida). Se guarda cifrada con DPAPI en %APPDATA%\Ecorex\Agent\
/// browser-allow.dat (un dominio por linea). La coincidencia es por sufijo de host (ej. "example.com"
/// permite "www.example.com"). Si la lista esta VACIA, se bloquea todo (fail-closed).
/// </summary>
public sealed class BrowserAllowList
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ecorex", "Agent");
    private static readonly string FilePath = Path.Combine(Dir, "browser-allow.dat");

    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { return Array.Empty<string>(); }
            var plain = Encoding.UTF8.GetString(Transform(File.ReadAllBytes(FilePath), encrypt: false));
            return plain.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.ToLowerInvariant()).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Save(IEnumerable<string> domains)
    {
        Directory.CreateDirectory(Dir);
        var text = string.Join('\n', domains.Select(d => d.Trim().ToLowerInvariant()).Where(d => d.Length > 0).Distinct());
        File.WriteAllBytes(FilePath, Transform(Encoding.UTF8.GetBytes(text), encrypt: true));
    }

    /// <summary>true si el host (o un sufijo de dominio) esta permitido. Fail-closed si la lista esta vacia.</summary>
    public bool IsAllowed(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) { return false; }
        host = host.ToLowerInvariant();
        foreach (var d in Load())
        {
            if (host == d || host.EndsWith("." + d, StringComparison.Ordinal)) { return true; }
        }
        return false;
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
                ? CryptProtectData(ref input, "EcorexAgentBrowserAllow", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output)
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
