using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Application.Gestor.TemplateAgent;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Gestor;

/// <summary>Marca de correo para el editor (valores guardados + datos de contacto de la empresa).</summary>
public sealed record EmailBrandDto(
    string? LogoUrl,
    string? LogoEfectivo,
    string? ColorPrimario,
    string? ColorAcento,
    string EstiloPorDefecto,
    string? SitioWeb,
    string? PieTexto,
    string Empresa,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    string? Correo,
    string? LogoEmpresa = null);

public sealed record SaveEmailBrandRequest(
    string? LogoUrl,
    string? ColorPrimario,
    string? ColorAcento,
    string? EstiloPorDefecto,
    string? SitioWeb,
    string? PieTexto);

/// <summary>Marca resuelta para el agente: logo con URL utilizable en el correo y datos reales del pie.</summary>
public sealed record EmailBrandInfo(
    string Empresa,
    string? LogoUrl,
    string? ColorPrimario,
    string? ColorAcento,
    string EstiloPorDefecto,
    string? SitioWeb,
    string? PieTexto,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    string? Correo);

public interface IEmailBrandService
{
    Task<EmailBrandDto> GetAsync(CancellationToken cancellationToken = default);
    Task<(bool Ok, string? Error, EmailBrandDto? Brand)> SaveAsync(SaveEmailBrandRequest request, CancellationToken cancellationToken = default);
    Task<EmailBrandInfo> GetForAgentAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// MARCA DE CORREO del tenant (ADR-0177): logo, colores, estilo por defecto y datos del pie que el agente editor
/// de plantillas aplica a todos los correos. Una fila por tenant (se crea al primer guardado). Tenant-scoped por
/// el filtro global. Los datos de contacto (direccion, telefono, correo) salen de la ficha de la empresa.
/// </summary>
public sealed class EmailBrandService : IEmailBrandService
{
    private static readonly Regex Hex6 = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Regex Hex3 = new("^#[0-9a-fA-F]{3}$", RegexOptions.Compiled);

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly INotifyLinkBuilder _links;

    public EmailBrandService(IApplicationDbContext db, ITenantContext tenant, INotifyLinkBuilder links)
    {
        _db = db;
        _tenant = tenant;
        _links = links;
    }

    public async Task<EmailBrandDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var (b, t) = await LoadAsync(cancellationToken);
        return ToDto(b, t);
    }

    public async Task<EmailBrandInfo> GetForAgentAsync(CancellationToken cancellationToken = default)
    {
        var (b, t) = await LoadAsync(cancellationToken);
        var logo = string.IsNullOrWhiteSpace(b?.LogoUrl) ? t?.LogoUrl : b!.LogoUrl;
        return new EmailBrandInfo(
            t?.Name ?? string.Empty,
            PublicUrl(logo),
            b?.ColorPrimario,
            b?.ColorAcento,
            EmailDesignStyles.Normalize(b?.EstiloPorDefecto) ?? EmailDesignStyles.DefaultKey,
            b?.SitioWeb,
            b?.PieTexto,
            t?.Address,
            t?.City,
            t?.Phone,
            t?.Email);
    }

    public async Task<(bool Ok, string? Error, EmailBrandDto? Brand)> SaveAsync(SaveEmailBrandRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return (false, "Sin tenant activo.", null); }

        if (!TryColor(request.ColorPrimario, out var primario)) { return (false, "El color principal debe ser un hexadecimal como #0d2c4b.", null); }
        if (!TryColor(request.ColorAcento, out var acento)) { return (false, "El color de acento debe ser un hexadecimal como #e8833a.", null); }
        if (!TryUrl(request.SitioWeb, out var sitio)) { return (false, "El sitio web debe ser una URL como https://miempresa.com.", null); }
        var logo = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
        if (logo is not null && !(logo.StartsWith('/') || logo.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return (false, "El logo debe venir de la galeria o ser una URL https.", null);
        }
        var estilo = string.IsNullOrWhiteSpace(request.EstiloPorDefecto) ? null : EmailDesignStyles.Normalize(request.EstiloPorDefecto);
        if (!string.IsNullOrWhiteSpace(request.EstiloPorDefecto) && estilo is null) { return (false, "Estilo de diseno no valido.", null); }
        var pie = string.IsNullOrWhiteSpace(request.PieTexto) ? null : request.PieTexto.Trim();
        if (pie is { Length: > 1000 }) { pie = pie[..1000]; }

        var entity = await _db.EmailBrands.FirstOrDefaultAsync(cancellationToken);
        if (entity is null)
        {
            entity = new EmailBrand { TenantId = tenantId };
            _db.EmailBrands.Add(entity);
        }
        entity.LogoUrl = logo;
        entity.ColorPrimario = primario;
        entity.ColorAcento = acento;
        entity.EstiloPorDefecto = estilo;
        entity.SitioWeb = sitio;
        entity.PieTexto = pie;
        await _db.SaveChangesAsync(cancellationToken);

        var t = await TenantAsync(cancellationToken);
        return (true, null, ToDto(entity, t));
    }

    private async Task<(EmailBrand? Brand, TenantInfo? Tenant)> LoadAsync(CancellationToken ct)
    {
        var b = await _db.EmailBrands.AsNoTracking().FirstOrDefaultAsync(ct);
        return (b, await TenantAsync(ct));
    }

    private sealed record TenantInfo(string Name, string? LogoUrl, string? Address, string? City, string? Phone, string? Email);

    private async Task<TenantInfo?> TenantAsync(CancellationToken ct)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        return await _db.Tenants.AsNoTracking().Where(x => x.Id == tid)
            .Select(x => new TenantInfo(x.Name, x.LogoUrl, x.Address, x.City, x.Phone, x.Email))
            .FirstOrDefaultAsync(ct);
    }

    private static EmailBrandDto ToDto(EmailBrand? b, TenantInfo? t)
        => new(
            b?.LogoUrl,
            string.IsNullOrWhiteSpace(b?.LogoUrl) ? t?.LogoUrl : b!.LogoUrl,
            b?.ColorPrimario,
            b?.ColorAcento,
            EmailDesignStyles.Normalize(b?.EstiloPorDefecto) ?? EmailDesignStyles.DefaultKey,
            b?.SitioWeb,
            b?.PieTexto,
            t?.Name ?? string.Empty,
            t?.Address,
            t?.City,
            t?.Phone,
            t?.Email,
            t?.LogoUrl);

    /// <summary>URL del recurso para usar DENTRO del correo: absoluta con la base publica; sin base publica
    /// (desarrollo) queda la ruta relativa (sirve en la vista previa; el verificador avisa que no saldra en el correo).</summary>
    private string? PublicUrl(string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl)) { return null; }
        return _links.ToAbsoluteUrl(pathOrUrl) ?? pathOrUrl;
    }

    internal static bool TryColor(string? input, out string? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(input)) { return true; }
        var c = input.Trim();
        if (!c.StartsWith('#')) { c = "#" + c; }
        if (Hex3.IsMatch(c)) { c = "#" + string.Concat(c[1..].Select(ch => new string(ch, 2))); }
        if (!Hex6.IsMatch(c)) { return false; }
        color = c.ToLowerInvariant();
        return true;
    }

    internal static bool TryUrl(string? input, out string? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(input)) { return true; }
        var u = input.Trim();
        if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { u = "https://" + u; }
        if (!Uri.TryCreate(u, UriKind.Absolute, out var parsed) || string.IsNullOrWhiteSpace(parsed.Host) || !parsed.Host.Contains('.')) { return false; }
        url = u.Length > 300 ? u[..300] : u;
        return true;
    }
}
