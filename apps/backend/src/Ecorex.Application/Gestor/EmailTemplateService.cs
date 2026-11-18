using System.Net;
using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Gestor;

/// <summary>Plantilla de correo (lectura).</summary>
public sealed record EmailTemplateDto(
    Guid Id, string Nombre, string Asunto, string CuerpoHtml, bool Activa, DateTimeOffset UpdatedAt, string? Estilo = null);

/// <summary>Alta/edicion de una plantilla de correo.</summary>
public sealed record SaveEmailTemplateRequest(string Nombre, string Asunto, string CuerpoHtml, bool Activa = true, string? Estilo = null);

/// <summary>Valores de merge de un contacto para renderizar una plantilla de correo. Los ultimos cinco
/// (ADR-0175) son opcionales: {vendedor} = asesor asignado, {telefono}, {sector}, {firma} = firma HTML del
/// asesor (Mi cuenta) y {enlace_baja} = URL publica para darse de baja de los correos.</summary>
public sealed record EmailMergeFields(
    string? Nombre, string? Empresa, string? Cargo, string? Ciudad, string? Correo,
    string? Vendedor = null, string? Telefono = null, string? Sector = null,
    string? Firma = null, string? EnlaceBaja = null);

/// <summary>Una variable de merge disponible en las plantillas de correo (catalogo para la UI y el agente).</summary>
public sealed record EmailMergeVariable(string Token, string Descripcion, string Ejemplo);

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
            .Select(t => new EmailTemplateDto(t.Id, t.Nombre, t.Asunto, t.CuerpoHtml, t.Activa, t.UpdatedAt ?? t.CreatedAt, t.Estilo))
            .ToListAsync(cancellationToken);
    }

    public async Task<EmailTemplateDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _db.EmailTemplates.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new EmailTemplateDto(t.Id, t.Nombre, t.Asunto, t.CuerpoHtml, t.Activa, t.UpdatedAt ?? t.CreatedAt, t.Estilo))
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
            Activa = request.Activa,
            Estilo = TemplateAgent.EmailDesignStyles.Normalize(request.Estilo),
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
        if (request.Estilo is not null) { entity.Estilo = TemplateAgent.EmailDesignStyles.Normalize(request.Estilo); }
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null) { return false; }
        // Soft-delete (regla 5, ADR-0175): nunca DELETE fisico. La fila queda marcada y el filtro global la
        // oculta; ademas se desactiva para que el motor no la use aunque alguien la consulte sin filtro.
        entity.IsDeleted = true;
        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.Activa = false;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static EmailTemplateDto Map(EmailTemplate e)
        => new(e.Id, e.Nombre, e.Asunto, e.CuerpoHtml, e.Activa, e.UpdatedAt ?? e.CreatedAt, e.Estilo);

    // ---- Merge ----

    /// <summary>Catalogo de variables de merge (orden de presentacion). Fuente unica para la UI, el agente y
    /// el verificador: una variable que no este aqui es "desconocida".</summary>
    public static readonly IReadOnlyList<EmailMergeVariable> Variables = new[]
    {
        new EmailMergeVariable("nombre", "Nombre del contacto", "Juan Perez"),
        new EmailMergeVariable("empresa", "Empresa del contacto (empresa padre o sector)", "ACME S.A.S"),
        new EmailMergeVariable("cargo", "Cargo del contacto", "Gerente de Compras"),
        new EmailMergeVariable("ciudad", "Ciudad del contacto", "Bogota"),
        new EmailMergeVariable("correo", "Correo del contacto", "juan.perez@acme.com"),
        new EmailMergeVariable("telefono", "Telefono del contacto", "3001234567"),
        new EmailMergeVariable("sector", "Sector del contacto", "Construccion"),
        new EmailMergeVariable("vendedor", "Asesor comercial asignado al contacto", "Maria Gomez"),
        new EmailMergeVariable("firma", "Firma HTML del asesor (se configura en Mi cuenta)", "<p>Maria Gomez<br/>Asesora comercial</p>"),
        new EmailMergeVariable("enlace_baja", "Enlace para darse de baja de los correos (obligatorio en correos comerciales)", "https://app.ecorex.co/baja/ejemplo"),
    };

    /// <summary>Datos de ejemplo para vistas previas (coinciden con <see cref="Variables"/>).</summary>
    public static readonly EmailMergeFields SampleFields = new(
        "Juan Perez", "ACME S.A.S", "Gerente de Compras", "Bogota", "juan.perez@acme.com",
        Vendedor: "Maria Gomez", Telefono: "3001234567", Sector: "Construccion",
        Firma: "<p style=\"margin:0\">Maria Gomez<br/>Asesora comercial</p>",
        EnlaceBaja: "#baja");

    private static readonly Regex TokenRegex = new(
        @"\{(nombre|empresa|cargo|ciudad|correo|telefono|sector|vendedor|firma|enlace_baja)\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Cualquier {token} con forma de variable (para detectar variables DESCONOCIDAS).</summary>
    internal static readonly Regex AnyTokenRegex = new(@"\{([a-zA-Z_][a-zA-Z0-9_]*)\}", RegexOptions.Compiled);

    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>Reemplaza las variables de merge con los datos del contacto. Valor faltante -> "".
    /// Con <paramref name="htmlEscapeValues"/>=true (cuerpo HTML) escapa los valores para evitar inyeccion,
    /// EXCEPTO {firma}, que es HTML del propio usuario del tenant y va tal cual. En texto plano (asunto,
    /// WhatsApp) la firma se pasa sin etiquetas.</summary>
    public static string RenderTemplate(string? template, EmailMergeFields fields, bool htmlEscapeValues)
    {
        if (string.IsNullOrEmpty(template)) { return string.Empty; }
        return TokenRegex.Replace(template, m =>
        {
            var key = m.Groups[1].Value.ToLowerInvariant();
            if (key == "firma")
            {
                var firma = fields.Firma ?? string.Empty;
                return htmlEscapeValues ? firma : PlainText(firma);
            }
            var value = key switch
            {
                "nombre" => fields.Nombre,
                "empresa" => fields.Empresa,
                "cargo" => fields.Cargo,
                "ciudad" => fields.Ciudad,
                "correo" => fields.Correo,
                "telefono" => fields.Telefono,
                "sector" => fields.Sector,
                "vendedor" => fields.Vendedor,
                "enlace_baja" => fields.EnlaceBaja,
                _ => null
            } ?? string.Empty;
            return htmlEscapeValues ? WebUtility.HtmlEncode(value) : value;
        });
    }

    /// <summary>Quita etiquetas HTML y decodifica entidades (para usar un fragmento HTML como texto plano).</summary>
    internal static string PlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) { return string.Empty; }
        var withBreaks = Regex.Replace(html, @"<\s*br\s*/?\s*>|</p\s*>", Environment.NewLine, RegexOptions.IgnoreCase);
        return WebUtility.HtmlDecode(TagRegex.Replace(withBreaks, string.Empty)).Trim();
    }
}
