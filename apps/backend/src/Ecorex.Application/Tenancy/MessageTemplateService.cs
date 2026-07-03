using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class MessageTemplateService : IMessageTemplateService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public MessageTemplateService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<MessageTemplateDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _db.MessageTemplates
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Category).ThenBy(t => t.SortOrder)
            .Select(t => new MessageTemplateDto(t.Id, t.Category, t.Body, t.MediaType, t.MediaUrl, t.MediaMimeType, t.SortOrder, t.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<MessageTemplateDto?> CreateAsync(CreateMessageTemplateRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }
        var category = (request.Category ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(category))
        {
            category = "saludo";
        }
        var nextOrder = await _db.MessageTemplates
            .Where(t => t.Category == category)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var entity = new MessageTemplate
        {
            TenantId = tenantId,
            Category = category,
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            Body = (request.Body ?? "").Trim(),
            MediaType = request.MediaType,
            MediaUrl = request.MediaUrl,
            MediaMimeType = request.MediaMimeType,
            SortOrder = nextOrder + 1,
            IsActive = true
        };
        _db.MessageTemplates.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<MessageTemplateDto?> UpdateAsync(Guid id, UpdateMessageTemplateRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }
        entity.Category = (request.Category ?? entity.Category).Trim().ToLowerInvariant();
        entity.Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        entity.Body = (request.Body ?? "").Trim();
        entity.MediaType = request.MediaType;
        entity.MediaUrl = request.MediaUrl;
        entity.MediaMimeType = request.MediaMimeType;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }
        _db.MessageTemplates.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return 0;
        }
        var exists = await _db.MessageTemplates.AnyAsync(cancellationToken);
        if (exists)
        {
            return 0;
        }

        var created = 0;
        foreach (var (category, items) in _defaults)
        {
            var order = 0;
            foreach (var body in items)
            {
                _db.MessageTemplates.Add(new MessageTemplate
                {
                    TenantId = tenantId,
                    Category = category,
                    Body = body,
                    MediaType = MessageMediaType.None,
                    SortOrder = order++,
                    IsActive = true
                });
                created++;
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return created;
    }

    private static MessageTemplateDto Map(MessageTemplate t) =>
        new(t.Id, t.Category, t.Body, t.MediaType, t.MediaUrl, t.MediaMimeType, t.SortOrder, t.Name);

    // Mensajes del prototipo (embudo-ventas v0). {asesor} y {destino} se rellenan al usarlos.
    private static readonly (string Category, string[] Items)[] _defaults =
    {
        ("saludo", new[]
        {
            "Hola! Buen dia. Soy {asesor} de la agencia. Vi tu solicitud de viaje a {destino}.",
            "Hola, como estas? Te escribo del area comercial. Estoy a la orden para tu viaje.",
            "Buenas tardes. Recibi tu interes en viajar a {destino}. Tienes unos minutos para revisar opciones?"
        }),
        ("info", new[]
        {
            "Para preparar tu cotizacion, me confirmas fechas exactas y cantidad de pasajeros?",
            "Los menores que edad tienen? Lo necesito para tarifas aereas y de hotel.",
            "Tienes preferencia por aerolinea o salida en algun horario especifico?",
            "Te interesa un plan con todo incluido o solo hotel + vuelos?"
        }),
        ("cotizacion", new[]
        {
            "Te comparto la cotizacion con vuelos y hotel para tu viaje a {destino}.",
            "Los precios incluyen impuestos, traslados aeropuerto-hotel y desayuno.",
            "Tambien tengo una alternativa mas economica. Te la comparto?"
        }),
        ("seguimiento", new[]
        {
            "Hola! Pudiste revisar la cotizacion que te envie?",
            "Hay algo que pueda ajustar en la propuesta para que se acomode mejor?",
            "Solo paso por aqui para recordarte que los cupos estan limitados para esas fechas.",
            "Quedo atento por si tienes alguna duda."
        }),
        ("cierre", new[]
        {
            "Perfecto! Procedo con la reserva. Te envio instrucciones de pago en un momento.",
            "Para asegurar tu cupo necesito un abono del 50%. Pagas por transferencia o PSE?",
            "Confirmado! Ya tienes tu reserva. Te enviare los vouchers 48h antes del viaje.",
            "Muchas gracias por confiar en nosotros! Cualquier cosa me escribes."
        })
    };
}
