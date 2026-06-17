using System.Text.Json;
using CubotNails.Application.Common;
using CubotNails.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotNails.Application.Tenancy;

/// <summary>
/// Herramienta (function calling / "MCP") de VISION: clasifica el largo del cabello de la foto que envio
/// el cliente contra las medidas del salon (modulo Medidas de cabello). La imagen sale del contexto del
/// run (AiToolRunContext): una imagen pendiente (caja de arena/emulador) o la ultima foto entrante de la
/// conversacion (WhatsApp/emulador). Solo lectura: no reserva ni cobra; devuelve el largo estimado.
/// </summary>
public interface IHairLengthToolset : IAgentToolset
{
}

public sealed class HairLengthToolset : IHairLengthToolset
{
    private readonly IHairClassifierService _classifier;
    private readonly IApplicationDbContext _db;
    private readonly IAgentAssetReader _assets;

    public HairLengthToolset(IHairClassifierService classifier, IApplicationDbContext db, IAgentAssetReader assets)
    {
        _classifier = classifier;
        _db = db;
        _assets = assets;
    }

    public string GroupKey => "medidas-cabello";
    public string GroupLabel => "Medidas de cabello (vision)";

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            "clasificar_largo_cabello",
            "Analiza la FOTO que envio el cliente y determina el LARGO de su cabello segun las medidas del salon. " +
            "Usala cuando el cliente ya envio una foto y necesitas el largo para cotizar/agendar un servicio cuyo " +
            "precio varia por largo. No requiere argumentos: toma la ultima foto disponible. Si no hay foto, te lo " +
            "indica para que se la pidas al cliente. Devuelve la medida estimada, la confianza (0-100) y el motivo.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
    };

    public async Task<AgendaToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
    {
        if (toolName != "clasificar_largo_cabello") { return Err($"Herramienta desconocida: {toolName}"); }

        // Resolver la imagen: 1) imagen pendiente del run (sandbox/emulador), 2) ultima foto entrante de la conversacion.
        string? base64 = AiToolRunContext.ImageBase64;
        string mime = AiToolRunContext.ImageMime ?? "image/jpeg";

        if (base64 is null && AiToolRunContext.ConversationId is Guid convId)
        {
            var img = await _db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == convId && m.Direction == MessageDirection.Inbound
                    && m.MediaType == MessageMediaType.Image && m.MediaUrl != null)
                .OrderByDescending(m => m.SentAt)
                .Select(m => new { m.MediaUrl, m.MediaMimeType })
                .FirstOrDefaultAsync(cancellationToken);
            if (img is not null)
            {
                base64 = await _assets.ReadBase64Async(img.MediaUrl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(img.MediaMimeType)) { mime = img.MediaMimeType!; }
            }
        }

        if (string.IsNullOrWhiteSpace(base64))
        {
            return Ok(new { ok = false, sin_foto = true, mensaje = "El cliente aun no ha enviado una foto del cabello. Pidele amablemente que te envie una foto donde se vea bien el largo." });
        }

        var res = await _classifier.ClassifyAsync(base64, mime, null, actorUserId, cancellationToken);
        if (!res.Ok)
        {
            return Ok(new { ok = false, mensaje = res.Error ?? "No se pudo analizar la foto." });
        }

        return Ok(new
        {
            ok = true,
            medida = res.CategoryName,
            categoria_id = res.CategoryId,
            confianza = res.Confidence,
            motivo = res.Rationale
        });
    }

    private static AgendaToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), BookingCreated: false);
    private static AgendaToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), BookingCreated: false);
}
