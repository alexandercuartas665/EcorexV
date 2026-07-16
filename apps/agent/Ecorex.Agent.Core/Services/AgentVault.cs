using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Boveda local del agente: UN solo lugar donde se decide donde viven los secretos y como se cifran.
/// Antes cada store (config, source, allow-lists, consent) traia su propia copia del P/Invoke a DPAPI
/// y de la ruta: 5 copias que podian divergir. Ahora todos pasan por aqui.
///
/// ADR-0039 (D8: el despliegue real es estacion Y servidor sin sesion):
/// - **Donde**: `%ProgramData%\Ecorex\Agent`, no `%APPDATA%`. El dueno del store es el Servicio
///   (LocalSystem), y un servicio no tiene el perfil del usuario.
/// - **Como**: DPAPI de MAQUINA (`CRYPTPROTECT_LOCAL_MACHINE`), no de usuario. Lo que cifra la
///   colmena tiene que poder abrirlo el servicio, que corre con otra identidad. Con DPAPI de usuario
///   (lo anterior) eso era imposible por construccion.
/// - **Puerta real**: con DPAPI de maquina, CUALQUIER proceso que pueda LEER el archivo puede
///   descifrarlo; la llave no cuelga del usuario. Por eso el ACL de la carpeta (SYSTEM +
///   Administradores) es la proteccion de verdad, no el cifrado. Con LocalSystem eso implica que un
///   administrador local puede llegar al secreto del tenant: aceptado por el usuario (2026-07-16).
///   Siguiente escalon si algun dia se quiere least-privilege: cuenta virtual NT SERVICE\EcorexAgent.
/// </summary>
public static class AgentVault
{
    /// <summary>Carpeta de la boveda. Comun al servicio y a la colmena.</summary>
    public static string Dir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ecorex", "Agent");

    private static string PathOf(string name) => Path.Combine(Dir, name);

    /// <summary>Lee y descifra un archivo de la boveda; null si no existe o no se puede abrir.</summary>
    public static byte[]? Read(string name)
    {
        try
        {
            var path = PathOf(name);
            if (!File.Exists(path)) { return null; }
            return Transform(File.ReadAllBytes(path), encrypt: false);
        }
        catch
        {
            // Corrupto, de otra maquina, o sin permiso de lectura: se trata como "no hay".
            return null;
        }
    }

    /// <summary>Cifra y persiste un archivo de la boveda.</summary>
    public static void Write(string name, byte[] plain)
    {
        EnsureDir();
        File.WriteAllBytes(PathOf(name), Transform(plain, encrypt: true));
    }

    public static string? ReadText(string name)
    {
        var raw = Read(name);
        return raw is null ? null : Encoding.UTF8.GetString(raw);
    }

    public static void WriteText(string name, string text) => Write(name, Encoding.UTF8.GetBytes(text));

    public static void Delete(string name)
    {
        try { if (File.Exists(PathOf(name))) { File.Delete(PathOf(name)); } }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Crea la carpeta y le pone el ACL (SYSTEM + Administradores, herencia rota). Best-effort: si el
    /// proceso no tiene privilegio para cambiar el ACL, la carpeta igual sirve y el instalador (Ola 5d)
    /// lo deja bien de forma autoritativa. NO se silencia el fallo de crear la carpeta.
    /// </summary>
    private static void EnsureDir()
    {
        var existed = Directory.Exists(Dir);
        Directory.CreateDirectory(Dir);
        if (existed) { return; }

        try
        {
            var info = new DirectoryInfo(Dir);
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            {
                acl.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(sid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }
            info.SetAccessControl(acl);
        }
        catch
        {
            // Sin privilegio para endurecer el ACL: lo hara el instalador. La boveda ya existe.
        }
    }

    // NOTA (verificado 2026-07-16): NO hay migracion automatica desde el store heredado
    // (%APPDATA%, DPAPI de usuario). Se intento y es imposible por construccion: el unico que puede
    // DESCIFRAR el archivo viejo es el usuario que lo escribio (la colmena), y ese es justamente
    // quien ya NO puede ESCRIBIR la boveda (ACL SYSTEM+Admins). El servicio puede escribirla pero no
    // puede descifrar el archivo del usuario. Quien ya tenia la colmena configurada reconfigura una
    // vez; en instalaciones nuevas el instalador (Ola 5d) escribe la boveda de entrada.

    // ---- DPAPI via P/Invoke (sin dependencia NuGet) ----

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    private const int CryptProtectUiForbidden = 0x1;
    private const int CryptProtectLocalMachine = 0x4;

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

            // El flag de maquina solo aplica al CIFRAR; al descifrar DPAPI deduce el alcance del blob.
            const int protectFlags = CryptProtectUiForbidden | CryptProtectLocalMachine;
            var ok = encrypt
                ? CryptProtectData(ref input, "EcorexAgent", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, protectFlags, ref output)
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
