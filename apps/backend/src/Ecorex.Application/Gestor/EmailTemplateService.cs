using System.Net;
using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Gestor;

/// <summary>Plantilla de correo (lectura).</summary>
public sealed record EmailTemplateDto(
    Guid Id, string Nombre, string Asunto, string CuerpoHtml, bool Activa, DateTimeOffset UpdatedAt);

/// <summary>Alta/edicion de una plantilla de correo.</summary>
public sealed record SaveEmailTemplateRequest(string Nombre, string Asunto, string CuerpoHtml, bool Activa = true);

/// <summary>Valores de merge de un contacto para renderizar una plantilla de correo.</summary>
public sealed record EmailMergeFields(string? Nombre, string? Empresa, string? Cargo, string? Ciudad, string? Correo);

/// <summary>
/// CRUD de plantillas de correo (tenant-scoped) del motor de acciones por filtro (ADR-0056, paso E-mail),
/// mas el helper de MERGE que reemplaza las variables {nombre},{empresa},{cargo},{ciudad},{correo} con los
/// datos del contacto. En el cuerpo (HTML) las variables se escapan para evitar inyeccion; en el asunto
/// (texto plano) no. Valor faltante -> cadena vacia.
/// </summary>
public interface IEmailTemplateService
{
    Task<IReadOnlyList<EmailTemplateDto>> ListAsync(bool soloActivas = false, CancellationToken cancellationToken = default);
    Task<EmailTemplateDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EmailTemplateDto?> CreateAsync(SaveEmailTemplateRequest request, CancellationToken cancellationToken = default);
    Task<EmailTemplateDto?> UpdateAsync(Guid id, SaveEmailTemplateRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class EmailTemplateService : IEmailTemplateService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public EmailTemplateService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<EmailTemplateDto>> ListAsync(bool soloActivas = false, CancellationToken cancellationToken = default)
    {
        var q = _db.EmailTemplates.AsNoTracking();
        if (soloActivas) { q = q.Where(t => t.Activa); }
        return await q
            .OrderByDescending(t => t.Activa).ThenBy(t => t.Nombre)
            .Select(t => new EmailTemplateDto(t.Id, t.Nombre, t.Asunto, t.CuerpoHtml, t.Activa, t.UpdatedAt ?? t.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<EmailTemplateDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _db.EmailTemplates.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new EmailTemplateDto(t.Id, t.Nombre, t.Asunto, t.CuerpoHtml, t.Activa, t.UpdatedAt ?? t.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<EmailTemplateDto?> CreateAsync(SaveEmailTemplateRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var nombre = (request.Nombre ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nombre)) { return null; }

        var entity = new EmailTemplate
        {
            TenantId = tenantId,
            Nombre = nombre,
            Asunto = (request.Asunto ?? string.Empty).Trim(),
            CuerpoHtml = request.CuerpoHtml ?? string.Empty,
            Activa = request.Activa
        };
        _db.EmailTemplates.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<EmailTemplateDto?> UpdateAsync(Guid id, SaveEmailTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _db.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null) { return null; }
        var nombre = (request.Nombre ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(nombre)) { entity.Nombre = nombre; }
        entity.Asunto = (request.Asunto ?? string.Empty).Trim();
        entity.CuerpoHtml = request.CuerpoHtml ?? string.Empty;
        entity.Activa = request.Activa;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null) { return false; }
        _db.EmailTemplates.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static EmailTemplateDto Map(EmailTemplate e)
        => new(e.Id, e.Nombre, e.Asunto, e.CuerpoHtml, e.Activa, e.UpdatedAt ?? e.CreatedAt);

    // ---- Merge ----

    private static readonly Regex TokenRegex = new(
        @"\{(nombre|empresa|cargo|ciudad|correo)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Reemplaza las variables de merge con los datos del contacto. Valor faltante -> "".
    /// Con <paramref name="htmlEscapeValues"/>=true (cuerpo HTML) escapa los valores para evitar inyeccion.</summary>
    public static string RenderTemplate(string? template, EmailMergeFields fields, bool htmlEscapeValues)
    {
        if (string.IsNullOrEmpty(template)) { return string.Empty; }
        return TokenRegex.Replace(template, m =>
        {
            var key = m.Groups[1].Value.ToLowerInvariant();
            var value = key switch
            {
                "nombre" => fields.Nombre,
                "empresa" => fields.Empresa,
                "cargo" => fields.Cargo,
                "ciudad" => fields.Ciudad,
                "correo" => fields.Correo,
                _ => null
            } ?? string.Empty;
            return htmlEscapeValues ? WebUtility.HtmlEncode(value) : value;
        });
    }
}
