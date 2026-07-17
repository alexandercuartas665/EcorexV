using System.Text.Json;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Ipc;

/// <summary>
/// Canal LOCAL entre el Servicio (dueno de identidad, canal y boveda) y la colmena WPF, que es su
/// CLIENTE (ADR-0039 s3). Named pipe, solo maquina local; nada de red.
///
/// Para que existe:
/// - la colmena PINTA el estado real (no el suyo: ella ya no conecta al hub ni abre la boveda);
/// - la colmena CONFIGURA (identidad, allow-lists, consentimiento) y **persiste el servicio**;
/// - el servicio DELEGA el Navegador a la colmena, que es quien tiene escritorio (sesion 0 no sirve).
///
/// SEGURIDAD (decidida en la Ola 5c). El servicio corre como **LocalSystem**, asi que el pipe es una
/// superficie privilegiada: quien pueda ensanchar la allow-list de Archivos le estaria abriendo a la
/// nube la lectura de TODO el disco con permisos de SYSTEM. De ahi la division:
/// - **Lectura de estado y servir el Navegador**: cualquier usuario interactivo (es el operador, y no
///   viaja ningun secreto).
/// - **MUTAR (identidad / allow-lists / consentimiento)**: solo **Administradores**. El servicio
///   impersona al cliente del pipe y lo comprueba; un no-admin recibe un NO explicito.
/// El **secreto del tenant NUNCA viaja por el pipe** en direccion al cliente: se puede escribir, jamas
/// leer. La colmena no ve la credencial maestra (mismo principio de doc 06 s3.1 con los sub-agentes).
/// </summary>
public static class AgentIpc
{
    /// <summary>Nombre del named pipe local (`\\.\pipe\ecorex-agent`).</summary>
    public const string PipeName = "ecorex-agent";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Sobre de un mensaje. `Payload` es el JSON del cuerpo segun `Kind`.</summary>
    public sealed record Envelope(string Kind, string? Id = null, string? Payload = null);

    public static class Kinds
    {
        // colmena -> servicio
        public const string Hello = "hello";
        public const string GetState = "get-state";
        public const string SetConfig = "set-config";
        public const string SetBrowserAllow = "set-browser-allow";
        public const string SetFileAllow = "set-file-allow";
        public const string SetConsent = "set-consent";
        public const string BrowserResult = "browser-res";

        // servicio -> colmena
        public const string State = "state";
        public const string Ack = "ack";
        public const string RequestStarted = "req-started";
        public const string RequestFinished = "req-finished";
        public const string BrowserRequest = "browser-req";
    }

    /// <summary>Lo que la colmena declara al conectarse.</summary>
    public sealed record HelloMsg(bool CanServeBrowser);

    /// <summary>
    /// Estado que el servicio publica a la colmena. **Sin el secreto**, a proposito: se escribe pero
    /// no se lee (<see cref="HasSecret"/> solo dice si hay uno configurado).
    /// </summary>
    public sealed record StateMsg(
        ConnectionState Connection,
        string ClientId,
        string HubUrl,
        bool HasSecret,
        string? LastError,
        bool BrowserEnabled,
        bool FilesEnabled,
        IReadOnlyList<string> BrowserAllow,
        IReadOnlyList<string> FileAllow);

    public sealed record SetConfigMsg(string ClientId, string HubUrl, string? Secret);

    public sealed record SetAllowMsg(IReadOnlyList<string> Items);

    public sealed record SetConsentMsg(bool Browser, bool Files);

    /// <summary>Respuesta a una mutacion. `Ok=false` + motivo (ej. "exige administrador").</summary>
    public sealed record AckMsg(bool Ok, string? Error = null);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static T? Deserialize<T>(string? json) =>
        string.IsNullOrEmpty(json) ? default : JsonSerializer.Deserialize<T>(json, Json);
}
