using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ecorex.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Guardia de higiene Unicode en el PUNTO UNICO de escritura (DAL dual: aplica a PostgreSQL y SQL Server
/// por igual). Antes de cada SaveChanges, recorre las entidades agregadas/modificadas y sanea los valores
/// string que contengan un "surrogate suelto" (media mitad de un par suplente UTF-16, p.ej. un emoji
/// partido por un truncado o por texto mal decodificado de una fuente externa). Sin esto, Npgsql lanza
/// System.Text.EncoderFallbackException al codificar el parametro ("Unable to translate Unicode character
/// \uD83D ...") y la escritura revienta; en la ruta del agente eso ademas disparaba reintentos que
/// REENVIABAN el mensaje al cliente. Se neutraliza en el mismo SaveChanges (atomico), sin importar el
/// origen del dato. La logica vive en <see cref="SurrogateText"/> (Application.Common), testeable aparte.
/// </summary>
public sealed class SurrogateSanitizingInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            foreach (var property in entry.Properties)
            {
                // Solo columnas string. En entidades MODIFICADAS, solo las que de verdad cambian: asi no
                // ampliamos el conjunto de columnas del UPDATE ni pisamos nada que no se este guardando.
                if (property.CurrentValue is not string current || current.Length == 0)
                {
                    continue;
                }
                if (entry.State == EntityState.Modified && !property.IsModified)
                {
                    continue;
                }

                var clean = SurrogateText.StripLoneSurrogates(current);
                // StripLoneSurrogates devuelve la MISMA referencia si no habia nada que sanear (camino rapido).
                if (!ReferenceEquals(clean, current))
                {
                    property.CurrentValue = clean;
                }
            }
        }
    }
}
