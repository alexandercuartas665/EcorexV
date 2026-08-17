using System.IO;
using System.Text.Json;

namespace Ecorex.Agent.Gui.Services;

/// <summary>
/// Un perfil de "modo login": Name (display) + LoginUrl (donde el usuario se loguea) + SessionKey ESTABLE
/// e inmutable (el directorio del perfil persistente profiles/{SessionKey}). Builtin = los originales que
/// no se pueden quitar.
/// </summary>
public sealed record LoginProfile(string SessionKey, string Name, string LoginUrl, bool Builtin);

/// <summary>
/// Perfiles de login ADMINISTRABLES por el usuario (antes eran 4 botones fijos). La lista se guarda en
/// %LocalAppData%\Ecorex\Agent\login-profiles.json: es nombre + URL (NO secreto), user-writable y sin
/// elevacion. Las cookies de sesion siguen en profiles/{SessionKey} (data del usuario, aparte). En el
/// primer arranque se siembran como Builtin los 4 originales con sus SessionKeys EXACTOS para no perder
/// los logins ya hechos (linkedin/facebook/instagram/tiktok).
/// </summary>
public sealed class LoginProfileStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ecorex", "Agent", "login-profiles.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // Semilla: MISMOS sessionKeys que ya usan los perfiles logueados (no se pierde la sesion).
    private static readonly LoginProfile[] Seed =
    {
        new("linkedin", "LinkedIn", "https://www.linkedin.com/login", true),
        new("facebook", "Facebook", "https://www.facebook.com/login", true),
        new("instagram", "Instagram", "https://www.instagram.com/accounts/login/", true),
        new("tiktok", "TikTok", "https://www.tiktok.com/login", true),
    };

    private List<LoginProfile> _cache = new();

    public IReadOnlyList<LoginProfile> List()
    {
        Load();
        return _cache;
    }

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<LoginProfile>>(File.ReadAllText(FilePath), Json);
                if (list is { Count: > 0 }) { _cache = list; return; }
            }
        }
        catch { /* corrupto: se re-siembra */ }
        _cache = Seed.ToList();
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_cache, Json));
        }
        catch { /* best-effort */ }
    }

    /// <summary>Agrega un perfil (Nombre + URL de login). Deriva un SessionKey estable del nombre; si
    /// colisiona, sufija -2/-3. Devuelve el perfil creado o un mensaje de error.</summary>
    public (LoginProfile? Profile, string? Error) Add(string? name, string? loginUrl)
    {
        Load();
        name = (name ?? string.Empty).Trim();
        loginUrl = (loginUrl ?? string.Empty).Trim();
        if (name.Length == 0) { return (null, "El nombre es obligatorio."); }
        if (!Uri.TryCreate(loginUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (null, "La URL de login debe ser http/https absoluta.");
        }
        var baseKey = WebView2BrowserSubAgent.SanitizeSessionKey(name);
        var key = baseKey;
        var n = 2;
        while (_cache.Any(p => string.Equals(p.SessionKey, key, StringComparison.OrdinalIgnoreCase)))
        {
            key = $"{baseKey}-{n++}";
        }
        var profile = new LoginProfile(key, name, uri.ToString(), false);
        _cache.Add(profile);
        Save();
        return (profile, null);
    }

    /// <summary>Quita un perfil por SessionKey (solo los NO Builtin). El dir profiles/{key} NO se borra
    /// (por si el usuario lo re-agrega o para no perder cookies por accidente).</summary>
    public bool Remove(string sessionKey)
    {
        Load();
        var p = _cache.FirstOrDefault(x => string.Equals(x.SessionKey, sessionKey, StringComparison.OrdinalIgnoreCase));
        if (p is null || p.Builtin) { return false; }
        _cache.Remove(p);
        Save();
        return true;
    }
}
