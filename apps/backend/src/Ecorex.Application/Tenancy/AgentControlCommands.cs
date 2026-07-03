using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Comandos de control que un asesor puede enviar en una conversacion para "tomar el control": el
/// comando "Manejo_asesor" agrega el numero del contacto a la lista negra del tenant, asi el agente
/// deja de responderle y el humano atiende.
/// </summary>
public static class AgentControlCommands
{
    /// <summary>Comando exacto (case-insensitive) para mandar el numero del contacto a la lista negra.</summary>
    public const string TakeControl = "Manejo_asesor";

    public static bool IsTakeControl(string? body)
        => !string.IsNullOrWhiteSpace(body) && string.Equals(body.Trim(), TakeControl, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Agrega el numero a la lista negra global del tenant (si no estaba). Usa IgnoreQueryFilters
    /// (puede llamarse sin tenant en contexto); el tenant se resuelve desde la linea. True si quedo bloqueado.
    /// </summary>
    public static async Task<bool> BlockNumberForLineAsync(IApplicationDbContext db, Guid lineId, string? phone, CancellationToken ct = default)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        if (lineId == Guid.Empty || digits.Length == 0) { return false; }

        var tenantId = await db.WhatsAppLines.IgnoreQueryFilters()
            .Where(l => l.Id == lineId).Select(l => (Guid?)l.TenantId).FirstOrDefaultAsync(ct);
        if (tenantId is null) { return false; }

        var existing = await db.TenantBlockedNumbers.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId.Value).Select(b => b.Phone).ToListAsync(ct);
        if (existing.Any(e => e == digits || digits.EndsWith(e, StringComparison.Ordinal) || e.EndsWith(digits, StringComparison.Ordinal)))
        {
            return true;
        }

        db.TenantBlockedNumbers.Add(new TenantBlockedNumber
        {
            TenantId = tenantId.Value,
            Phone = digits,
            Note = "Agregado por comando Manejo_asesor"
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// True si el numero del contacto coincide con alguno de la lista negra (comparacion por digitos,
    /// tolera codigo de pais). Minimo 7 digitos por entrada para no bloquear por fragmentos cortos.
    /// </summary>
    public static bool IsBlocked(string? contactPhone, IEnumerable<string> blockedPhones)
    {
        if (string.IsNullOrWhiteSpace(contactPhone)) { return false; }
        var contact = new string(contactPhone.Where(char.IsDigit).ToArray());
        if (contact.Length == 0) { return false; }
        foreach (var raw in blockedPhones)
        {
            var blocked = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
            if (blocked.Length < 7) { continue; }
            if (contact == blocked
                || contact.EndsWith(blocked, StringComparison.Ordinal)
                || blocked.EndsWith(contact, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
