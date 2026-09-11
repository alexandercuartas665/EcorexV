using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Application.Marketplace;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Seeders;

/// <summary>
/// Siembra plantillas de arranque en el MARKETPLACE (ADR-0097). El Super Admin autora en el tenant
/// interno "Plataforma ECOREX" (Kind=Internal, del que ya es Owner) con los editores normales y publica
/// desde las tarjetas; este seeder deja unos FORMULARIOS genericos utiles ya publicados para que la
/// galeria no nazca vacia. Idempotente por CATALOGO: solo corre si el marketplace esta VACIO, asi que
/// borrar una plantilla NO la resucita (mientras exista cualquier item). Los formularios viven en el
/// tenant interno; la copia que "cae" en un tenant cliente es independiente (el cliente es su dueno).
/// </summary>
public sealed class MarketplaceStarterSeeder
{
    private readonly IApplicationDbContext _db;
    private readonly IFormDefinitionService _forms;
    private readonly IMarketplaceService _marketplace;
    private readonly ILogger<MarketplaceStarterSeeder> _logger;

    public MarketplaceStarterSeeder(
        IApplicationDbContext db, IFormDefinitionService forms, IMarketplaceService marketplace,
        ILogger<MarketplaceStarterSeeder> logger)
    {
        _db = db;
        _forms = forms;
        _marketplace = marketplace;
        _logger = logger;
    }

    public async Task EnsureStarterFormTemplatesAsync(CancellationToken cancellationToken = default)
    {
        // Solo en un catalogo VACIO: una vez hay CUALQUIER item (sembrado o publicado a mano) no vuelve a
        // correr, asi que borrar una plantilla sembrada no la trae de vuelta.
        if (await _db.MarketplaceItems.AnyAsync(cancellationToken)) { return; }

        var platformTenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Internal, cancellationToken);
        if (platformTenant is null) { return; }
        var superAdmin = await _db.PlatformUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.PlatformRole == PlatformRole.SuperAdmin && u.Status == PlatformUserStatus.Active, cancellationToken);

        var seeded = 0;
        foreach (var tpl in Templates)
        {
            try
            {
                // Todo el trabajo de formularios corre en el contexto del tenant interno.
                using (AmbientTenantContext.Begin(platformTenant.Id))
                {
                    var defId = await BuildFormAsync(tpl, cancellationToken);
                    if (defId is null) { continue; }
                    var input = new MarketplacePublishInput(tpl.Title, tpl.Description, tpl.Category, null);
                    var pub = await _marketplace.PublishFormAsync(defId.Value, input, superAdmin?.Id, cancellationToken);
                    if (pub.Ok) { seeded++; }
                    else { _logger.LogWarning("Marketplace starter: no se pudo publicar '{Title}': {Error}", tpl.Title, pub.Error); }
                }
            }
            catch (Exception ex)
            {
                // Nunca romper el arranque por una plantilla de ejemplo.
                _logger.LogWarning(ex, "Marketplace starter: fallo al sembrar '{Title}'.", tpl.Title);
            }
        }

        if (seeded > 0)
        {
            _logger.LogInformation("Marketplace starter: {N} plantilla(s) de formulario publicadas en el tenant interno.", seeded);
        }
    }

    // Crea (o reutiliza si el codigo ya existe en el tenant interno) el formulario con su seccion y campos.
    private async Task<Guid?> BuildFormAsync(StarterTemplate tpl, CancellationToken ct)
    {
        var existing = await _db.FormDefinitions.FirstOrDefaultAsync(d => d.Code == tpl.Code, ct);
        if (existing is not null) { return existing.Id; }

        var created = await _forms.CreateAsync(new CreateFormDefinitionRequest(tpl.Code, tpl.Title, tpl.Description), ct);
        if (!created.IsOk || created.Value is null) { return null; }
        var defId = created.Value.Id;

        var container = await _forms.AddContainerAsync(defId, new SaveFormContainerRequest(tpl.SectionName), ct);
        var containerId = container.Value?.Id;

        foreach (var f in tpl.Fields)
        {
            await _forms.AddQuestionAsync(defId, new SaveFormQuestionRequest(
                containerId, f.FieldCode, f.Label, f.Type, Required: f.Required, OptionsJson: f.OptionsJson), ct);
        }
        return defId;
    }

    private sealed record StarterField(string FieldCode, string Label, FormControlType Type, bool Required = false, string? OptionsJson = null);
    private sealed record StarterTemplate(string Code, string Title, string Description, string Category, string SectionName, IReadOnlyList<StarterField> Fields);

    // ASCII (regla del repo): etiquetas sin tildes ni enie.
    private static readonly IReadOnlyList<StarterTemplate> Templates = new[]
    {
        new StarterTemplate("SOLVAC", "Solicitud de vacaciones",
            "Formulario para que un colaborador solicite sus dias de vacaciones.", "Recursos Humanos",
            "Datos de la solicitud", new[]
            {
                new StarterField("colaborador", "Nombre del colaborador", FormControlType.Text, Required: true),
                new StarterField("cargo", "Cargo", FormControlType.Text),
                new StarterField("fecha_inicio", "Fecha de inicio", FormControlType.Date, Required: true),
                new StarterField("fecha_fin", "Fecha de fin", FormControlType.Date, Required: true),
                new StarterField("dias", "Dias solicitados", FormControlType.Number, Required: true),
                new StarterField("motivo", "Motivo", FormControlType.TextArea),
            }),

        new StarterTemplate("REGVIS", "Registro de visita",
            "Control de ingreso de visitantes: quien entra, a quien visita y a que hora.", "Recepcion",
            "Datos del visitante", new[]
            {
                new StarterField("visitante", "Nombre del visitante", FormControlType.Text, Required: true),
                new StarterField("documento", "Documento de identidad", FormControlType.Text, Required: true),
                new StarterField("empresa", "Empresa", FormControlType.Text),
                new StarterField("visita_a", "A quien visita", FormControlType.Text, Required: true),
                new StarterField("ingreso", "Fecha y hora de ingreso", FormControlType.DateTime, Required: true),
                new StarterField("motivo", "Motivo de la visita", FormControlType.Text),
                new StarterField("observaciones", "Observaciones", FormControlType.TextArea),
            }),

        new StarterTemplate("ENCSAT", "Encuesta de satisfaccion",
            "Encuesta corta para medir la satisfaccion del cliente con la atencion recibida.", "Servicio al cliente",
            "Tu opinion", new[]
            {
                new StarterField("nombre", "Nombre (opcional)", FormControlType.Text),
                new StarterField("calificacion", "Como calificas la atencion?", FormControlType.Radio, Required: true,
                    OptionsJson: "[{\"id\":\"5\",\"label\":\"5 - Excelente\"},{\"id\":\"4\",\"label\":\"4 - Buena\"},{\"id\":\"3\",\"label\":\"3 - Regular\"},{\"id\":\"2\",\"label\":\"2 - Mala\"},{\"id\":\"1\",\"label\":\"1 - Muy mala\"}]"),
                new StarterField("recomienda", "Nos recomendarias?", FormControlType.Radio, Required: true,
                    OptionsJson: "[{\"id\":\"si\",\"label\":\"Si\"},{\"id\":\"no\",\"label\":\"No\"}]"),
                new StarterField("comentarios", "Comentarios y sugerencias", FormControlType.TextArea),
            }),
    };
}
