using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Ecorex.SuperAdmin.Services;

/// <summary>Ticket de un libro para rendear su PDF: tenant + libro (nada mas sale del proceso).</summary>
public sealed record AnalisisPdfTicket(Guid TenantId, Guid LibroId);

/// <summary>
/// Tickets de UN SOLO USO y corta vida para el PDF de un libro (ADR-0158, boton "Descargar PDF"). El
/// endpoint autorizado (cookie del usuario) comprueba que puede ver el libro y emite un ticket; el
/// Chromium interno navega a <c>/a/pdf/{token}</c>, que lo CONSUME (se borra al leerlo) y pinta el libro
/// acotado al tenant del ticket. El token vive en memoria del proceso: el Chromium corre en el mismo
/// contenedor, asi que no necesita BD ni sale a la red. Sin ticket valido la pagina no muestra nada.
/// </summary>
public interface IAnalisisPdfTickets
{
    string Emitir(Guid tenantId, Guid libroId);

    AnalisisPdfTicket? Consumir(string? token);
}

public sealed class AnalisisPdfTickets : IAnalisisPdfTickets
{
    private static readonly TimeSpan Vida = TimeSpan.FromMinutes(2);
    private readonly IMemoryCache _cache;

    public AnalisisPdfTickets(IMemoryCache cache) => _cache = cache;

    private static string Clave(string token) => "analisis-pdf:" + token;

    public string Emitir(Guid tenantId, Guid libroId)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        _cache.Set(Clave(token), new AnalisisPdfTicket(tenantId, libroId), Vida);
        return token;
    }

    public AnalisisPdfTicket? Consumir(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !_cache.TryGetValue(Clave(token), out AnalisisPdfTicket? ticket))
        {
            return null;
        }

        _cache.Remove(Clave(token)); // un solo uso: aunque se reintente la URL, ya no sirve
        return ticket;
    }
}
