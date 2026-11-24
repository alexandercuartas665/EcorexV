using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Resuelve la tarea a la que pertenece un formulario a partir de su referencia. Las respuestas de una tarea se
/// anclan a su numero ("T00016") o, si la tarea tiene varias del mismo formulario, a "{numero}-{n}" ("T00016-2").
/// Primero se busca el numero EXACTO (asi no se rompen numeraciones que ya llevan guion, p. ej. "OT-0012"); solo
/// si no existe esa tarea se prueba quitando el sufijo "-{digitos}".
/// </summary>
public static class TaskReference
{
    /// <summary>Referencia sin el sufijo "-{digitos}" de respuestas adicionales; null si no tiene ese sufijo.</summary>
    public static string? SinSufijo(string? reference)
    {
        var r = (reference ?? string.Empty).Trim();
        var i = r.LastIndexOf('-');
        if (i <= 0 || i == r.Length - 1) { return null; }
        for (var k = i + 1; k < r.Length; k++) { if (!char.IsDigit(r[k])) { return null; } }
        return r[..i];
    }

    /// <summary>Id de la tarea de la referencia (exacta y, si no, sin el sufijo "-n"). El filtro por tenant aplica.</summary>
    public static async Task<Guid?> ResolveTaskIdAsync(IApplicationDbContext db, string? reference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference)) { return null; }
        var r = reference.Trim();
        var id = await db.TaskItems.AsNoTracking().Where(t => t.Number == r)
            .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (id is not null) { return id; }
        var baseNum = SinSufijo(r);
        if (baseNum is null) { return null; }
        return await db.TaskItems.AsNoTracking().Where(t => t.Number == baseNum)
            .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
    }
}
