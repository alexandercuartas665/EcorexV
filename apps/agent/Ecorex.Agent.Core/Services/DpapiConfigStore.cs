using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Persistencia local del ClientId/URL cifrada con DPAPI (por-usuario Windows). Se usa P/Invoke a
/// CryptProtectData/CryptUnprotectData para NO depender de un paquete NuGet externo. El archivo vive
/// en %APPDATA%\Ecorex\Agent\config.dat y solo lo puede descifrar el mismo usuario de Windows. Nunca
/// se guarda en el repo ni en texto plano.
/// </summary>
public sealed class DpapiConfigStore : IAgentConfigStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ecorex", "Agent");
    private static readonly string FilePath = Path.Combine(Dir, "config.dat");

    public AgentConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { return AgentConfig.Empty; }
            var cipher = File.ReadAllBytes(FilePath);
            var plain = Unprotect(cipher);
            var json = Encoding.UTF8.GetString(plain);
            return JsonSerializer.Deserialize<AgentConfig>(json) ?? AgentConfig.Empty;
        }
        catch
        {
            // Archivo corrupto o de otro usuario: se ignora (arranca sin config).
            return AgentConfig.Empty;
        }
    }

    public void Save(AgentConfig config)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(config);
        var cipher = Protect(Encoding.UTF8.GetBytes(json));
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

    private static byte[] Protect(byte[] data) => Transform(data, encrypt: true);
    private static byte[] Unprotect(byte[] data) => Transform(data, encrypt: false);

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
                ? CryptProtectData(ref input, "EcorexAgent", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output);
            if (!ok) { throw new InvalidOperationException("DPAPI fallo: " + Marshal.GetLastWin32Error()); }

            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            if (input.pbData != IntPtr.Zero) { Marshal.FreeHGlobal(input.pbData); }
            if (output.pbData != IntPtr.Zero) { NativeFree(output.pbData); }
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static void NativeFree(IntPtr ptr) => LocalFree(ptr);
}
