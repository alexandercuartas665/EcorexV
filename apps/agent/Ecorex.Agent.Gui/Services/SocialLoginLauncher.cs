using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using WpfWebView2 = Microsoft.Web.WebView2.Wpf.WebView2;

namespace Ecorex.Agent.Gui.Services;

/// <summary>
/// "Modo login": abre una ventana WebView2 VISIBLE e INTERACTIVA sobre el MISMO perfil persistente que usa
/// el scraping logueado (<c>profiles/{red}</c> via <see cref="WebView2BrowserSubAgent.ProfileDirFor"/>),
/// para que el USUARIO inicie sesion a mano (usuario/clave/2FA/CAPTCHA). La ventana QUEDA ABIERTA hasta que
/// el usuario la cierra: NO auto-cierra ni borra el perfil, asi la sesion sobrevive y el scraping la reutiliza.
///
/// Es un modo INICIADO POR EL USUARIO: por eso NO pasa por la allow-list de scraping (el operador esta
/// presente y decide a que red entra). El scraping automatico SI sigue aplicando allow-list + consentimiento.
/// </summary>
public static class SocialLoginLauncher
{
    // async void: lanzador tipo evento (fire-and-forget de UI). Todo el cuerpo va protegido para que ninguna
    // excepcion escape al Dispatcher. GENERICO: abre el perfil profiles/{sessionKey} en loginUrl; la lista
    // de perfiles (nombre + URL) la administra el usuario via LoginProfileStore, ya no esta hardcodeada.
    public static async void OpenLogin(string sessionKey, string loginUrl)
    {
        var web = new WpfWebView2();
        var window = new Window
        {
            Title = $"ECOREX - Iniciar sesion: {sessionKey}",
            Width = 1024,
            Height = 760,
            Content = web,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        window.Show();
        try
        {
            var userData = WebView2BrowserSubAgent.ProfileDirFor(sessionKey);
            Directory.CreateDirectory(userData);
            var env = await CoreWebView2Environment.CreateAsync(null, userData, null);
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.Navigate(loginUrl);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"No se pudo abrir el navegador de inicio de sesion: {ex.Message}\n\n" +
                "Sugerencia: cierra cualquier scraping en curso de esta red (comparten el mismo perfil) e intenta de nuevo.",
                "ECOREX - Iniciar sesion", MessageBoxButton.OK, MessageBoxImage.Warning);
            try { window.Close(); } catch { /* ya cerrada */ }
        }
    }
}
