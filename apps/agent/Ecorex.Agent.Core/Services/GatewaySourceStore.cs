using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Guarda LOCALMENTE (cifrada con DPAPI por-usuario) la cadena de conexion de la fuente SQL Server
/// que el Gateway consulta (Ola C, "credencial gestionada por el agente" - opcion b de doc 02/05:
/// la credencial de la LAN NUNCA viaja por el canal ni se guarda en el repo). Archivo:
/// %APPDATA%\Ecorex\Agent\source.dat.
/// </summary>
public sealed class GatewaySourceStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ecorex", "Agent");
    private static readonly string FilePath = Path.Combine(Dir, "source.dat");

    /// <summary>Cadena de conexion SQL Server persistida, o null si no hay.</summary>
    public string? LoadSqlServer()
    {
        try
        {
            if (!File.Exists(FilePath)) { return null; }
            var plain = Transform(File.ReadAllBytes(FilePath), encrypt: false);
            var s = Encoding.UTF8.GetString(plain);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch
        {
            return null;
        }
    }

    public void SaveSqlServer(string connectionString)
    {
        Directory.CreateDirectory(Dir);
        var cipher = Transform(Encoding.UTF8.GetBytes(connectionString), encrypt: true);
        File.WriteAllBytes(FilePath, cipher);
    }

    public void Clear()
    {
        try { if (File.Exists(FilePath)) { File.Delete(FilePath); } }
        catch { /* best-effort */ }
    }

    // ---- DPAPI (CurrentUser) via P/Invoke ----

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    private const int CryptProtectUiForbidden = 0x1;

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

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
                ? CryptProtectData(ref input, "EcorexAgentSource", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output)
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
