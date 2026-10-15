using System.Net;
using System.Net.Http;
using System.Text;
using Ecorex.Application.Automatizaciones.ConciliacionDian;
using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Implementacion de <see cref="INewtonEventSender"/>: hace el POST real a la API de NEWTON para radicar un
/// evento RADIAN. Resuelve las credenciales (URL + Auth-Token) de las variables del dron del tenant
/// (NEWTON_URL / NEWTON_TOKEN, esta ultima secreta y cifrada). Scoped (una instancia por request): cachea las
/// credenciales dentro de la misma operacion. ACTO LEGAL IRREVERSIBLE: el gateo (confirmacion) es del que llama.
/// </summary>
public sealed class NewtonEventSender(
    IApplicationDbContext db, ISecretProtector protector, ILogger<NewtonEventSender> log) : INewtonEventSender
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private (string Url, string Token)? _creds;

    private async Task<(string Url, string Token)?> GetCredsAsync(CancellationToken ct)
    {
        if (_creds is { } cached) { return cached; }
        var vars = await db.ScrapeVariables
            .Where(v => v.Name == "NEWTON_URL" || v.Name == "NEWTON_TOKEN")
            .Select(v => new { v.Name, v.ValueEncrypted, v.IsSecret })
            .ToListAsync(ct);

        string Read(string name)
        {
            var v = vars.FirstOrDefault(x => x.Name == name);
            if (v is null || string.IsNullOrEmpty(v.ValueEncrypted)) { return ""; }
            if (!v.IsSecret) { return v.ValueEncrypted; }
            try { return protector.Unprotect(v.ValueEncrypted); } catch { return ""; }
        }

        var url = Read("NEWTON_URL").Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url)) { url = "https://facturacion.eycproveedores.com/api"; }
        var token = Read("NEWTON_TOKEN").Trim();
        if (string.IsNullOrWhiteSpace(token)) { return null; }
        _creds = (url, token);
        return _creds;
    }

    public async Task<(bool Ok, string? Error)> SendEventAsync(string eventId, string tipoEvento, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventId)) { return (false, "eventId vacio"); }
        var creds = await GetCredsAsync(ct);
        if (creds is not { } c) { return (false, "Falta NEWTON_TOKEN en la config del dron."); }
        try
        {
            var url = $"{c.Url}/documentos-electronicos/{eventId}/event/{tipoEvento}";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("Auth-Token", c.Token);
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created) { return (true, null); }
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (false, $"{(int)resp.StatusCode} {(body.Length > 160 ? body[..160] : body)}");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "NEWTON SendEvent fallo (eventId={EventId}, tipo={Tipo})", eventId, tipoEvento);
            return (false, ex.Message);
        }
    }
}
