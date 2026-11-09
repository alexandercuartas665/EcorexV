using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.Common;
using Ecorex.Application.Reporting;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Analisis;

// ---- DTOs del modulo de Analisis (ADR-0158 Ola 2) ----

/// <summary>Quien opera: usuario de plataforma + si es Admin/Owner del tenant (edita cualquier libro).</summary>
public sealed record AnalisisActor(Guid? UserId, bool EsAdmin);

public sealed record AnalisisLibroResumen(Guid Id, string Nombre, string? Descripcion, AnalisisVisibilidad Visibilidad, bool EsDueno, int Hojas);

public sealed record AnalisisWidgetDto(
    Guid Id, Guid HojaId, AnalisisTipoWidget Tipo, string Titulo, string? SpecJson, string? IndicadorKey,
    string? ConfigJson, int Orden, int ColSpan, int RowSpan);

public sealed record AnalisisHojaDto(Guid Id, string Nombre, int Orden, IReadOnlyList<AnalisisWidgetDto> Widgets);

public sealed record AnalisisLibroDetalle(
    Guid Id, string Nombre, string? Descripcion, AnalisisVisibilidad Visibilidad, long Version, bool PuedeEditar,
    IReadOnlyList<AnalisisHojaDto> Hojas);

/// <summary>Widget nuevo: un spec del motor de reportes O un indicador curado (excluyentes).</summary>
public sealed record AnalisisNuevoWidget(AnalisisTipoWidget Tipo, string Titulo, ReportQuerySpec? Spec, string? IndicadorKey, int ColSpan = 1, string? ConfigJson = null);

/// <summary>Resultado de una operacion de escritura: Ok + Id creado/afectado, o el error legible.</summary>
public sealed record AnalisisOpResult(bool Ok, Guid? Id = null, string? Error = null)
{
    public static AnalisisOpResult Hecho(Guid? id = null) => new(true, id);

    public static AnalisisOpResult Falla(string error) => new(false, null, error);
}

/// <summary>Serializacion estable de specs guardados en <c>AnalisisWidget.SpecJson</c>.</summary>
public static class AnalisisJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(ReportQuerySpec spec) => JsonSerializer.Serialize(spec, Options);

    public static ReportQuerySpec? DeserializeSpec(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReportQuerySpec>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// CRUD de libros, hojas y widgets (ADR-0158 Ola 2). Contexto EFIMERO por operacion (factory, ADR-0138)
/// para no chocar con el DbContext del circuito Blazor. Tenant-safe por el filtro global; borrado
/// logico de libro/hoja; operaciones multi-fila en transaccion; concurrencia optimista en el libro.
/// Edita el dueno del libro o un Admin; ve el dueno, un Admin, o cualquiera si el libro esta compartido.
/// </summary>
public interface IAnalisisService
{
    Task<IReadOnlyList<AnalisisLibroResumen>> ListarLibrosAsync(AnalisisActor actor, CancellationToken ct = default);

    Task<AnalisisLibroDetalle?> ObtenerLibroAsync(Guid libroId, AnalisisActor actor, CancellationToken ct = default);

    Task<AnalisisOpResult> CrearLibroAsync(string nombre, string? descripcion, AnalisisActor actor, CancellationToken ct = default);

    Task<AnalisisOpResult> RenombrarLibroAsync(Guid libroId, string nombre, string? descripcion, long version, AnalisisActor actor, CancellationToken ct = default);

    Task<AnalisisOpResult> EliminarLibroAsync(Guid libroId, AnalisisActor actor, CancellationToken ct = default);

    Task<AnalisisOpResult> CrearHojaAsync(Guid libroId, string nombre, AnalisisActor actor, CancellationToken ct = default);

    Task<AnalisisOpResult> RenombrarHojaAsync(Guid hojaId, string nombre, AnalisisActor actor, CancellationToken ct = default);

    Task<AnalisisOpResult> EliminarHojaAsync(Guid hojaId, AnalisisActor actor, CancellationToken ct = default);

    Task<AnalisisOpResult> AgregarWidgetAsync(Guid hojaId, AnalisisNuevoWidget widget, AnalisisActor actor, CancellationToken ct = default);

    Task<AnalisisOpResult> EliminarWidgetAsync(Guid widgetId, AnalisisActor actor, CancellationToken ct = default);

    /// <summary>Mueve el widget una posicion (delta -1 = antes, +1 = despues) dentro de su hoja.</summary>
    Task<AnalisisOpResult> MoverWidgetAsync(Guid widgetId, int delta, AnalisisActor actor, CancellationToken ct = default);

    /// <summary>Ancho del widget en la grilla de 4 columnas (se acota a 1..4).</summary>
    Task<AnalisisOpResult> CambiarAnchoWidgetAsync(Guid widgetId, int colSpan, AnalisisActor actor, CancellationToken ct = default);
}

public sealed class AnalisisService : IAnalisisService
{
    public const int MaxColumnas = 4;
    private const int MaxNombreLibro = 150;
    private const int MaxNombreHoja = 120;
    private const int MaxTitulo = 150;

    private readonly IApplicationDbContextFactory _factory;
    private readonly IIndicadorCuradoRegistry _curados;
    private readonly TimeProvider _time;

    public AnalisisService(IApplicationDbContextFactory factory, IIndicadorCuradoRegistry curados, TimeProvider? time = null)
    {
        _factory = factory;
        _curados = curados;
        _time = time ?? TimeProvider.System;
    }

    // ---------------- Lectura ----------------

    public async Task<IReadOnlyList<AnalisisLibroResumen>> ListarLibrosAsync(AnalisisActor actor, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var libros = await db.AnalisisLibros.AsNoTracking()
            .OrderBy(l => l.Nombre)
            .Select(l => new
            {
                l.Id,
                l.Nombre,
                l.Descripcion,
                l.Visibilidad,
                l.OwnerUserId,
                l.AllowedUserIdsJson,
                Hojas = l.Hojas.Count(h => !h.IsDeleted)
            })
            .ToListAsync(ct);

        return libros
            .Where(l => PuedeVer(l.OwnerUserId, l.Visibilidad, l.AllowedUserIdsJson, actor))
            .Select(l => new AnalisisLibroResumen(l.Id, l.Nombre, l.Descripcion, l.Visibilidad, actor.UserId is Guid u && l.OwnerUserId == u, l.Hojas))
            .ToList();
    }

    public async Task<AnalisisLibroDetalle?> ObtenerLibroAsync(Guid libroId, AnalisisActor actor, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var libro = await db.AnalisisLibros.AsNoTracking().FirstOrDefaultAsync(l => l.Id == libroId, ct);
        if (libro is null || !PuedeVer(libro.OwnerUserId, libro.Visibilidad, libro.AllowedUserIdsJson, actor))
        {
            return null;
        }

        var hojas = await db.AnalisisHojas.AsNoTracking()
            .Where(h => h.LibroId == libroId)
            .OrderBy(h => h.Orden).ThenBy(h => h.CreatedAt)
            .ToListAsync(ct);
        var hojaIds = hojas.Select(h => h.Id).ToList();
        var widgets = await db.AnalisisWidgets.AsNoTracking()
            .Where(w => hojaIds.Contains(w.HojaId))
            .OrderBy(w => w.Orden).ThenBy(w => w.CreatedAt)
            .ToListAsync(ct);

        var porHoja = widgets.GroupBy(w => w.HojaId).ToDictionary(g => g.Key, g => g.Select(ToDto).ToList());
        return new AnalisisLibroDetalle(
            libro.Id, libro.Nombre, libro.Descripcion, libro.Visibilidad, libro.Version, PuedeEditar(libro, actor),
            hojas.Select(h => new AnalisisHojaDto(h.Id, h.Nombre, h.Orden,
                porHoja.TryGetValue(h.Id, out var ws) ? ws : new List<AnalisisWidgetDto>())).ToList());
    }

    // ---------------- Libros ----------------

    public async Task<AnalisisOpResult> CrearLibroAsync(string nombre, string? descripcion, AnalisisActor actor, CancellationToken ct = default)
    {
        var n = Limpio(nombre, MaxNombreLibro);
        if (n is null)
        {
            return AnalisisOpResult.Falla("El libro necesita un nombre.");
        }

        await using var db = _factory.Create();
        await using var tx = await db.BeginTransactionAsync(ct);
        var libro = new AnalisisLibro
        {
            Nombre = n,
            Descripcion = Limpio(descripcion, 1000),
            OwnerUserId = actor.UserId,
            Visibilidad = AnalisisVisibilidad.Privado
        };
        db.AnalisisLibros.Add(libro);
        // Todo libro nace con una hoja para que la grilla tenga donde poner widgets.
        db.AnalisisHojas.Add(new AnalisisHoja { LibroId = libro.Id, Nombre = "Hoja 1", Orden = 0 });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return AnalisisOpResult.Hecho(libro.Id);
    }

    public async Task<AnalisisOpResult> RenombrarLibroAsync(Guid libroId, string nombre, string? descripcion, long version, AnalisisActor actor, CancellationToken ct = default)
    {
        var n = Limpio(nombre, MaxNombreLibro);
        if (n is null)
        {
            return AnalisisOpResult.Falla("El libro necesita un nombre.");
        }

        await using var db = _factory.Create();
        var libro = await db.AnalisisLibros.FirstOrDefaultAsync(l => l.Id == libroId, ct);
        if (libro is null)
        {
            return AnalisisOpResult.Falla("El libro no existe.");
        }

        if (!PuedeEditar(libro, actor))
        {
            return AnalisisOpResult.Falla("Solo el dueno del libro o un administrador pueden editarlo.");
        }

        if (libro.Version != version)
        {
            return AnalisisOpResult.Falla("Otro usuario modifico el libro. Recarga e intenta de nuevo.");
        }

        libro.Nombre = n;
        libro.Descripcion = Limpio(descripcion, 1000);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AnalisisOpResult.Falla("Otro usuario modifico el libro. Recarga e intenta de nuevo.");
        }

        return AnalisisOpResult.Hecho(libro.Id);
    }

    public async Task<AnalisisOpResult> EliminarLibroAsync(Guid libroId, AnalisisActor actor, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var libro = await db.AnalisisLibros.FirstOrDefaultAsync(l => l.Id == libroId, ct);
        if (libro is null)
        {
            return AnalisisOpResult.Falla("El libro no existe.");
        }

        if (!PuedeEditar(libro, actor))
        {
            return AnalisisOpResult.Falla("Solo el dueno del libro o un administrador pueden eliminarlo.");
        }

        // Soft-delete en cascada diferida: libro + sus hojas en la misma transaccion (los widgets quedan
        // colgando de hojas borradas, invisibles por el filtro; se recuperan si se restaura la hoja).
        await using var tx = await db.BeginTransactionAsync(ct);
        var now = _time.GetUtcNow();
        MarcarBorrado(libro, now);
        foreach (var h in await db.AnalisisHojas.Where(h => h.LibroId == libroId).ToListAsync(ct))
        {
            MarcarBorrado(h, now);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return AnalisisOpResult.Hecho(libroId);
    }

    // ---------------- Hojas ----------------

    public async Task<AnalisisOpResult> CrearHojaAsync(Guid libroId, string nombre, AnalisisActor actor, CancellationToken ct = default)
    {
        var n = Limpio(nombre, MaxNombreHoja);
        if (n is null)
        {
            return AnalisisOpResult.Falla("La hoja necesita un nombre.");
        }

        await using var db = _factory.Create();
        var libro = await db.AnalisisLibros.AsNoTracking().FirstOrDefaultAsync(l => l.Id == libroId, ct);
        if (libro is null)
        {
            return AnalisisOpResult.Falla("El libro no existe.");
        }

        if (!PuedeEditar(libro, actor))
        {
            return AnalisisOpResult.Falla("Solo el dueno del libro o un administrador pueden agregar hojas.");
        }

        var orden = await db.AnalisisHojas.Where(h => h.LibroId == libroId).Select(h => (int?)h.Orden).MaxAsync(ct) ?? -1;
        var hoja = new AnalisisHoja { LibroId = libroId, Nombre = n, Orden = orden + 1 };
        db.AnalisisHojas.Add(hoja);
        await db.SaveChangesAsync(ct);
        return AnalisisOpResult.Hecho(hoja.Id);
    }

    public async Task<AnalisisOpResult> RenombrarHojaAsync(Guid hojaId, string nombre, AnalisisActor actor, CancellationToken ct = default)
    {
        var n = Limpio(nombre, MaxNombreHoja);
        if (n is null)
        {
            return AnalisisOpResult.Falla("La hoja necesita un nombre.");
        }

        await using var db = _factory.Create();
        var (hoja, error) = await HojaEditableAsync(db, hojaId, actor, ct);
        if (hoja is null)
        {
            return AnalisisOpResult.Falla(error!);
        }

        hoja.Nombre = n;
        await db.SaveChangesAsync(ct);
        return AnalisisOpResult.Hecho(hoja.Id);
    }

    public async Task<AnalisisOpResult> EliminarHojaAsync(Guid hojaId, AnalisisActor actor, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var (hoja, error) = await HojaEditableAsync(db, hojaId, actor, ct);
        if (hoja is null)
        {
            return AnalisisOpResult.Falla(error!);
        }

        var quedan = await db.AnalisisHojas.CountAsync(h => h.LibroId == hoja.LibroId && h.Id != hojaId, ct);
        if (quedan == 0)
        {
            return AnalisisOpResult.Falla("El libro debe conservar al menos una hoja.");
        }

        MarcarBorrado(hoja, _time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return AnalisisOpResult.Hecho(hojaId);
    }

    // ---------------- Widgets ----------------

    public async Task<AnalisisOpResult> AgregarWidgetAsync(Guid hojaId, AnalisisNuevoWidget widget, AnalisisActor actor, CancellationToken ct = default)
    {
        var titulo = Limpio(widget.Titulo, MaxTitulo);
        if (titulo is null)
        {
            return AnalisisOpResult.Falla("El widget necesita un titulo.");
        }

        var tieneSpec = widget.Spec is not null;
        var tieneCurado = !string.IsNullOrWhiteSpace(widget.IndicadorKey);
        if (tieneSpec == tieneCurado)
        {
            return AnalisisOpResult.Falla("El widget debe tener una consulta o un indicador curado (uno solo).");
        }

        if (tieneCurado && _curados.Find(widget.IndicadorKey!) is null)
        {
            return AnalisisOpResult.Falla("El indicador curado no existe.");
        }

        await using var db = _factory.Create();
        var (hoja, error) = await HojaEditableAsync(db, hojaId, actor, ct);
        if (hoja is null)
        {
            return AnalisisOpResult.Falla(error!);
        }

        var orden = await db.AnalisisWidgets.Where(w => w.HojaId == hojaId).Select(w => (int?)w.Orden).MaxAsync(ct) ?? -1;
        var entity = new AnalisisWidget
        {
            HojaId = hojaId,
            Tipo = widget.Tipo,
            Titulo = titulo,
            SpecJson = tieneSpec ? AnalisisJson.Serialize(widget.Spec!) : null,
            IndicadorKey = tieneCurado ? widget.IndicadorKey!.Trim() : null,
            ConfigJson = widget.ConfigJson,
            Orden = orden + 1,
            ColSpan = Math.Clamp(widget.ColSpan, 1, MaxColumnas),
            RowSpan = 1
        };
        db.AnalisisWidgets.Add(entity);
        await db.SaveChangesAsync(ct);
        return AnalisisOpResult.Hecho(entity.Id);
    }

    public async Task<AnalisisOpResult> EliminarWidgetAsync(Guid widgetId, AnalisisActor actor, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var (widget, error) = await WidgetEditableAsync(db, widgetId, actor, ct);
        if (widget is null)
        {
            return AnalisisOpResult.Falla(error!);
        }

        // El widget NO es un agregado (es la configuracion de una celda): se borra en fisico.
        db.AnalisisWidgets.Remove(widget);
        await db.SaveChangesAsync(ct);
        return AnalisisOpResult.Hecho(widgetId);
    }

    public async Task<AnalisisOpResult> MoverWidgetAsync(Guid widgetId, int delta, AnalisisActor actor, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var (widget, error) = await WidgetEditableAsync(db, widgetId, actor, ct);
        if (widget is null)
        {
            return AnalisisOpResult.Falla(error!);
        }

        await using var tx = await db.BeginTransactionAsync(ct);
        var hermanos = await db.AnalisisWidgets
            .Where(w => w.HojaId == widget.HojaId)
            .OrderBy(w => w.Orden).ThenBy(w => w.CreatedAt)
            .ToListAsync(ct);
        var i = hermanos.FindIndex(w => w.Id == widgetId);
        var j = Math.Clamp(i + Math.Sign(delta), 0, hermanos.Count - 1);
        if (i != j)
        {
            (hermanos[i], hermanos[j]) = (hermanos[j], hermanos[i]);
        }

        // Renumera todo (corrige huecos o empates heredados) en un solo SaveChanges.
        for (var k = 0; k < hermanos.Count; k++)
        {
            hermanos[k].Orden = k;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return AnalisisOpResult.Hecho(widgetId);
    }

    public async Task<AnalisisOpResult> CambiarAnchoWidgetAsync(Guid widgetId, int colSpan, AnalisisActor actor, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var (widget, error) = await WidgetEditableAsync(db, widgetId, actor, ct);
        if (widget is null)
        {
            return AnalisisOpResult.Falla(error!);
        }

        widget.ColSpan = Math.Clamp(colSpan, 1, MaxColumnas);
        await db.SaveChangesAsync(ct);
        return AnalisisOpResult.Hecho(widgetId);
    }

    // ---------------- Reglas de acceso ----------------

    internal static bool PuedeVer(Guid? ownerUserId, AnalisisVisibilidad visibilidad, string? allowedJson, AnalisisActor actor)
    {
        if (actor.EsAdmin || visibilidad != AnalisisVisibilidad.Privado)
        {
            return true;
        }

        if (actor.UserId is not Guid u)
        {
            return false;
        }

        return ownerUserId == u || AllowedUsers(allowedJson).Contains(u);
    }

    internal static bool PuedeEditar(AnalisisLibro libro, AnalisisActor actor) =>
        actor.EsAdmin || (actor.UserId is Guid u && libro.OwnerUserId == u);

    private static HashSet<Guid> AllowedUsers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HashSet<Guid>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json)?.ToHashSet() ?? new HashSet<Guid>();
        }
        catch (JsonException)
        {
            return new HashSet<Guid>();
        }
    }

    private static async Task<(AnalisisHoja? Hoja, string? Error)> HojaEditableAsync(IApplicationDbContext db, Guid hojaId, AnalisisActor actor, CancellationToken ct)
    {
        var hoja = await db.AnalisisHojas.Include(h => h.Libro).FirstOrDefaultAsync(h => h.Id == hojaId, ct);
        if (hoja?.Libro is null)
        {
            return (null, "La hoja no existe.");
        }

        return PuedeEditar(hoja.Libro, actor)
            ? (hoja, null)
            : (null, "Solo el dueno del libro o un administrador pueden editarlo.");
    }

    private static async Task<(AnalisisWidget? Widget, string? Error)> WidgetEditableAsync(IApplicationDbContext db, Guid widgetId, AnalisisActor actor, CancellationToken ct)
    {
        var widget = await db.AnalisisWidgets.Include(w => w.Hoja).ThenInclude(h => h!.Libro).FirstOrDefaultAsync(w => w.Id == widgetId, ct);
        if (widget?.Hoja?.Libro is null)
        {
            return (null, "El widget no existe.");
        }

        return PuedeEditar(widget.Hoja.Libro, actor)
            ? (widget, null)
            : (null, "Solo el dueno del libro o un administrador pueden editarlo.");
    }

    private static void MarcarBorrado(Ecorex.Domain.Common.ISoftDeletable e, DateTimeOffset now)
    {
        e.IsDeleted = true;
        e.DeletedAt = now;
    }

    private static string? Limpio(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        var t = s.Trim();
        return t.Length > max ? t[..max] : t;
    }

    private static AnalisisWidgetDto ToDto(AnalisisWidget w) =>
        new(w.Id, w.HojaId, w.Tipo, w.Titulo, w.SpecJson, w.IndicadorKey, w.ConfigJson, w.Orden, w.ColSpan, w.RowSpan);
}
