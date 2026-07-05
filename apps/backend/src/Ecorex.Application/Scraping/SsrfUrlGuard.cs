using System.Net;
using System.Net.Sockets;

namespace Ecorex.Application.Scraping;

/// <summary>Veredicto del guard SSRF sobre una URL (ADR-0025).</summary>
/// <param name="Allowed">true solo si la URL es segura de consultar desde el servidor.</param>
/// <param name="Reason">Motivo del rechazo (null si Allowed).</param>
/// <param name="Uri">URI parseada (null si ni siquiera parsea).</param>
/// <param name="IsLoopback">true si TODAS las direcciones resueltas son loopback (solo puede ser Allowed con AllowLoopback).</param>
public sealed record SsrfCheckResult(bool Allowed, string? Reason, Uri? Uri, bool IsLoopback)
{
    public static SsrfCheckResult Ok(Uri uri, bool isLoopback) => new(true, null, uri, isLoopback);
    public static SsrfCheckResult Blocked(string reason, Uri? uri = null) => new(false, reason, uri, false);
}

/// <summary>
/// Validador anti-SSRF ESTRICTO (ADR-0025, innegociable en un SaaS multi-tenant): un tenant
/// no puede usar el ejecutor de extraccion para alcanzar la red interna del servidor.
/// Reglas: solo http/https absolutas, sin credenciales embebidas, solo puertos por defecto
/// (80/443), y se RESUELVE el DNS validando CADA direccion resultante contra los rangos
/// privados/loopback/link-local/CGNAT/multicast (IPv4 e IPv6, incluidas IPv4-mapeadas).
/// Unica excepcion: loopback con opts.AllowLoopback=true (Development, endpoint demo propio),
/// caso en el que se admite cualquier puerto porque la app dev escucha en puertos altos.
/// Limite documentado (TODO ADR-0025): no se fija la IP validada para la conexion posterior,
/// asi que un DNS con TTL 0 podria re-resolver distinto entre validacion y GET (rebinding).
/// </summary>
public static class SsrfUrlGuard
{
    /// <summary>Resolutor DNS inyectable (tests). El default usa Dns.GetHostAddressesAsync.</summary>
    public delegate Task<IPAddress[]> DnsResolver(string host, CancellationToken cancellationToken);

    public static async Task<SsrfCheckResult> ValidateAsync(
        string? url,
        ScrapeGuardOptions options,
        DnsResolver? resolver = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return SsrfCheckResult.Blocked("La URL no es una direccion absoluta valida.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return SsrfCheckResult.Blocked($"Esquema no permitido: {uri.Scheme} (solo http y https).", uri);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return SsrfCheckResult.Blocked("URLs con credenciales embebidas (user@host) no estan permitidas.", uri);
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            return SsrfCheckResult.Blocked("La URL no tiene host.", uri);
        }

        // Resolucion: literal IP directo; hostname via DNS. Sin direcciones = bloqueada.
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                resolver ??= static (host, ct) => Dns.GetHostAddressesAsync(host, ct);
                addresses = await resolver(uri.Host, cancellationToken);
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                return SsrfCheckResult.Blocked($"No se pudo resolver el host {uri.Host}.", uri);
            }
        }

        if (addresses.Length == 0)
        {
            return SsrfCheckResult.Blocked($"El host {uri.Host} no resuelve a ninguna direccion.", uri);
        }

        // TODAS las direcciones deben ser seguras (un host con A publico + A privado es ataque).
        var allLoopback = addresses.All(IsLoopback);
        if (allLoopback)
        {
            if (!options.AllowLoopback)
            {
                return SsrfCheckResult.Blocked(
                    "Direcciones loopback bloqueadas (solo el endpoint demo en Development).", uri);
            }
            // Loopback permitido (dev): cualquier puerto, la app local escucha en puertos altos.
            return SsrfCheckResult.Ok(uri, isLoopback: true);
        }

        foreach (var address in addresses)
        {
            if (IsBlockedAddress(address))
            {
                return SsrfCheckResult.Blocked(
                    $"El host {uri.Host} resuelve a una direccion privada o reservada ({address}).", uri);
            }
        }

        // Puertos: solo los por defecto del esquema (80/443). "Puertos raros" = superficie
        // de ataque a servicios internos expuestos en la IP publica del propio datacenter.
        if (!uri.IsDefaultPort)
        {
            return SsrfCheckResult.Blocked(
                $"Puerto {uri.Port} no permitido (solo 80/443 en URLs publicas).", uri);
        }

        return SsrfCheckResult.Ok(uri, isLoopback: false);
    }

    /// <summary>true si la IP es loopback (127.0.0.0/8 o ::1), normalizando IPv4-mapeadas.</summary>
    public static bool IsLoopback(IPAddress address) => IPAddress.IsLoopback(Normalize(address));

    /// <summary>
    /// true si la direccion NO debe alcanzarse desde el servidor: loopback, privadas RFC1918,
    /// link-local (169.254/16 incluida la metadata 169.254.169.254, fe80::/10), CGNAT
    /// 100.64/10, 0.0.0.0/8, reservadas 240/4, broadcast, multicast, IPv6 unique-local
    /// fc00::/7, site-local deprecada fec0::/10, unspecified y Teredo/6to4 con payload privado no
    /// se descomponen (se bloquean por rango publico especial cuando aplica).
    /// </summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        var ip = Normalize(address);

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] switch
            {
                0 => true,                                    // 0.0.0.0/8
                10 => true,                                   // 10.0.0.0/8
                100 when b[1] >= 64 && b[1] <= 127 => true,   // 100.64.0.0/10 (CGNAT)
                127 => true,                                  // loopback (redundante, claridad)
                169 when b[1] == 254 => true,                 // 169.254.0.0/16 (link-local / metadata)
                172 when b[1] >= 16 && b[1] <= 31 => true,    // 172.16.0.0/12
                192 when b[1] == 0 && b[2] == 0 => true,      // 192.0.0.0/24 (IETF)
                192 when b[1] == 0 && b[2] == 2 => true,      // 192.0.2.0/24 (TEST-NET-1)
                192 when b[1] == 168 => true,                 // 192.168.0.0/16
                198 when b[1] >= 18 && b[1] <= 19 => true,    // 198.18.0.0/15 (benchmarking)
                >= 224 => true,                               // multicast 224/4 + reservado 240/4 + broadcast
                _ => false
            };
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.IPv6Any))
            {
                return true;
            }
            var b = ip.GetAddressBytes();
            return ip.IsIPv6LinkLocal            // fe80::/10
                || ip.IsIPv6SiteLocal            // fec0::/10 (deprecada)
                || ip.IsIPv6Multicast            // ff00::/8
                || (b[0] & 0xFE) == 0xFC;        // fc00::/7 (unique local)
        }

        // Familias exoticas: bloquear por defecto (fail-closed).
        return true;
    }

    /// <summary>IPv4 mapeada en IPv6 (::ffff:10.0.0.1) evaluada como su IPv4 real.</summary>
    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
