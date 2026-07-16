using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Consentimiento LOCAL del operador para capacidades sensibles (doc 06 s4): activar el Navegador o
/// los Archivos exige que el operador lo habilite en la colmena; NO basta que la nube lo pida.
/// Fail-closed: por defecto (sin archivo) TODO esta deshabilitado. Se guarda cifrado con DPAPI en
/// %APPDATA%\Ecorex\Agent\consent.dat.
/// </summary>
public sealed class CapabilityConsent
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ecorex", "Agent");
    private static readonly string FilePath = Path.Combine(Dir, "consent.dat");

    private sealed record State(bool Browser, bool Files);

    private State Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { return new State(false, false); }
            var json = Encoding.UTF8.GetString(Transform(File.ReadAllBytes(FilePath), encrypt: false));
            return JsonSerializer.Deserialize<State>(json) ?? new State(false, false);
        }
        catch
        {
            return new State(false, false); // fail-closed
        }
    }

    private void Save(State state)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(FilePath, Transform(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state)), encrypt: true));
    }

    public bool IsBrowserEnabled() => Load().Browser;

    public bool IsFilesEnabled() => Load().Files;

    public void SetBrowser(bool enabled) => Save(Load() with { Browser = enabled });

    public void SetFiles(bool enabled) => Save(Load() with { Files = enabled });

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
                ? CryptProtectData(ref input, "EcorexAgentConsent", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output)
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
