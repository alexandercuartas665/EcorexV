using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Common;

/// <summary>
/// El IApplicationDbContext de un circuito Blazor vive lo que dura la pestana. Si una entidad ya quedo RASTREADA en
/// el y otra pantalla (otro circuito) la cambia en BD, volver a consultarla devuelve la copia VIEJA (EF no pisa los
/// valores de una entidad rastreada). Peor: al asignarle un valor igual al viejo, EF no detecta cambio y NO escribe.
/// Caso real: tras "Rechazar" (DEVOLVER_CONCILIADOR) el responsable quedaba NULL en BD, pero el conciliador aun tenia
/// el usuario en memoria; al reasignar el MISMO usuario no se guardaba nada.
/// <see cref="RecargarAsync"/> refresca la entidad desde BD justo despues de cargarla y ANTES de modificarla.
/// </summary>
public static class DbContextRecargaExtensions
{
    /// <summary>Recarga desde BD una entidad rastreada (sin efecto si es null, nueva o no rastreada, o si el
    /// contexto no es un DbContext de EF, p. ej. un doble de pruebas).</summary>
    public static async Task RecargarAsync(this IApplicationDbContext db, object? entity, CancellationToken ct = default)
    {
        if (entity is null || db is not DbContext ctx) { return; }
        var entry = ctx.Entry(entity);
        if (entry.State is EntityState.Detached or EntityState.Added) { return; }
        await entry.ReloadAsync(ct);
    }
}
