using System.Text.Json;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Consentimiento LOCAL del operador para capacidades sensibles (doc 06 s4): activar el Navegador o
/// los Archivos exige que el operador lo habilite en la colmena; NO basta que la nube lo pida.
/// Fail-closed: por defecto (sin archivo, corrupto o ilegible) TODO esta deshabilitado. Se persiste
/// por <see cref="AgentVault"/> (ADR-0039).
/// </summary>
public sealed class CapabilityConsent
{
    private const string FileName = "consent.dat";

    private sealed record State(bool Browser, bool Files);

    private static State Load()
    {
        try
        {
            var json = AgentVault.ReadText(FileName);
            if (string.IsNullOrEmpty(json)) { return new State(false, false); }
            return JsonSerializer.Deserialize<State>(json) ?? new State(false, false);
        }
        catch
        {
            return new State(false, false); // fail-closed
        }
    }

    private static void Save(State state) => AgentVault.WriteText(FileName, JsonSerializer.Serialize(state));

    public bool IsBrowserEnabled() => Load().Browser;

    public bool IsFilesEnabled() => Load().Files;

    public void SetBrowser(bool enabled) => Save(Load() with { Browser = enabled });

    public void SetFiles(bool enabled) => Save(Load() with { Files = enabled });
}
