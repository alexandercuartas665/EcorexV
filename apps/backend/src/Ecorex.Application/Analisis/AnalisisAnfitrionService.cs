using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Analisis;

/// <summary>
/// Que muestran los modulos ANFITRIONES del componente embebible (ADR-0158 D9), guardado por tenant en
/// <c>TenantConfiguration</c>: la hoja de indicadores de la ficha del tercero (filtrada por ese tercero) y
/// los widgets fijados en el Inicio. Solo un Admin del tenant lo cambia (afecta a todos los usuarios).
/// Contexto EFIMERO por operacion (ADR-0138).
/// </summary>
public interface IAnalisisAnfitrionService
{
    Task<Guid?> HojaFichaTerceroAsync(CancellationToken ct = default);

    Task<AnalisisOpResult> GuardarHojaFichaTerceroAsync(Guid? hojaId, AnalisisActor actor, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> WidgetsInicioAsync(CancellationToken ct = default);

    Task<AnalisisOpResult> FijarEnInicioAsync(Guid widgetId, bool fijar, AnalisisActor actor, CancellationToken ct = default);
}

public sealed class AnalisisAnfitrionService : IAnalisisAnfitrionService
{
    public const string KeyFichaTercero = "analisis.ficha_tercero.hoja";
    public const string KeyInicio = "analisis.inicio.widgets";
    public const int MaxWidgetsInicio = 8;

    private readonly IApplicationDbContextFactory _factory;

    public AnalisisAnfitrionService(IApplicationDbContextFactory factory) => _factory = factory;

    public async Task<Guid?> HojaFichaTerceroAsync(CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var raw = await LeerAsync(db, KeyFichaTercero, ct);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public async Task<AnalisisOpResult> GuardarHojaFichaTerceroAsync(Guid? hojaId, AnalisisActor actor, CancellationToken ct = default)
    {
        if (!actor.EsAdmin)
        {
            return AnalisisOpResult.Falla("Solo un administrador puede elegir la hoja de la ficha del tercero.");
        }

        await using var db = _factory.Create();
        if (hojaId is Guid h && !await db.AnalisisHojas.AnyAsync(x => x.Id == h, ct))
        {
            return AnalisisOpResult.Falla("La hoja no existe.");
        }

        await EscribirAsync(db, KeyFichaTercero, hojaId?.ToString(), ct);
        return AnalisisOpResult.Hecho(hojaId);
    }

    public async Task<IReadOnlyList<Guid>> WidgetsInicioAsync(CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        return ParseIds(await LeerAsync(db, KeyInicio, ct));
    }

    public async Task<AnalisisOpResult> FijarEnInicioAsync(Guid widgetId, bool fijar, AnalisisActor actor, CancellationToken ct = default)
    {
        if (!actor.EsAdmin)
        {
            return AnalisisOpResult.Falla("Solo un administrador puede fijar widgets en el Inicio.");
        }

        await using var db = _factory.Create();
        var ids = ParseIds(await LeerAsync(db, KeyInicio, ct)).ToList();
        if (fijar)
        {
            if (!await db.AnalisisWidgets.AnyAsync(w => w.Id == widgetId, ct))
            {
                return AnalisisOpResult.Falla("El widget no existe.");
            }

            if (!ids.Contains(widgetId))
            {
                if (ids.Count >= MaxWidgetsInicio)
                {
                    return AnalisisOpResult.Falla($"El Inicio admite hasta {MaxWidgetsInicio} widgets fijados.");
                }

                ids.Add(widgetId);
            }
        }
        else
        {
            ids.Remove(widgetId);
        }

        await EscribirAsync(db, KeyInicio, ids.Count == 0 ? null : JsonSerializer.Serialize(ids), ct);
        return AnalisisOpResult.Hecho(widgetId);
    }

    internal static IReadOnlyList<Guid> ParseIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<Guid>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json)?.Distinct().ToList() ?? new List<Guid>();
        }
        catch (JsonException)
        {
            return Array.Empty<Guid>();
        }
    }

    private static Task<string?> LeerAsync(IApplicationDbContext db, string key, CancellationToken ct) =>
        db.TenantConfigurations.AsNoTracking().Where(c => c.ConfigKey == key).Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);

    private static async Task EscribirAsync(IApplicationDbContext db, string key, string? valor, CancellationToken ct)
    {
        var fila = await db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == key, ct);
        if (fila is null)
        {
            if (valor is null)
            {
                return;
            }

            db.TenantConfigurations.Add(new TenantConfiguration { ConfigKey = key, ConfigValue = valor });
        }
        else
        {
            fila.ConfigValue = valor;
        }

        await db.SaveChangesAsync(ct);
    }
}
