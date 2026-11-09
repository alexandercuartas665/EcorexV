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
    string? ConfigJson, int Orden, int ColSpan, int RowSpan, string? ContextBindingsJson = null);

public sealed record AnalisisHojaDto(Guid Id, string Nombre, int Orden, IReadOnlyList<AnalisisWidgetDto> Widgets, string? FiltrosJson = null)
{
    public HojaFiltros Filtros => HojaFiltros.Leer(FiltrosJson);
}

public sealed record AnalisisLibroDetalle(
    Guid Id, string Nombre, string? Descripcion, AnalisisVisibilidad Visibilidad, long Version, bool PuedeEditar,
    IReadOnlyList<AnalisisHojaDto> Hojas);

/// <summary>Widget nuevo: un spec del motor de reportes O un indicador curado (excluyentes).</summary>
public sealed record AnalisisNuevoWidget(AnalisisTipoWidget Tipo, string Titulo, ReportQuerySpec? Spec, string? IndicadorKey, int ColSpan = 1, string? ConfigJson = null, string? ContextBindingsJson = null);

/// <summary>Posicion de un widget en el layout de su hoja (el orden lo da la posicion en la lista).</summary>
public sealed record AnalisisLayoutItem(Guid WidgetId, int ColSpan);

/// <summary>Referencia a una hoja para los selectores de los modulos anfitriones.</summary>
public sealed record AnalisisHojaRef(Guid HojaId, Guid LibroId, string Libro, string Hoja)
{
    public string Etiqueta => $"{Libro} / {Hoja}";
}

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

    /// <summary>Reemplaza la definicion del widget (tipo, titulo, consulta o curado, presentacion) conservando su posicion.</summary>
    Task<AnalisisOpResult> ActualizarWidgetAsync(Guid widgetId, AnalisisNuevoWidget widget, AnalisisActor actor, CancellationToken ct = default);

    /// <summary>Guarda los filtros globales de la hoja (rango de fechas, vendedor, tablero).</summary>
    Task<AnalisisOpResult> GuardarFiltrosHojaAsync(Guid hojaId, HojaFiltros filtros, AnalisisActor actor, CancellationToken ct = default);

    /// <summary>Orden + ancho de TODOS los widgets de la hoja en una transaccion (arrastrar / redimensionar).</summary>
    Task<AnalisisOpResult> GuardarLayoutAsync(Guid hojaId, IReadOnlyList<AnalisisLayoutItem> layout, AnalisisActor actor, CancellationToken ct = default);

    /// <summary>Copia el widget justo despues del original.</summary>
    Task<AnalisisOpResult> DuplicarWidgetAsync(Guid widgetId, AnalisisActor actor, CancellationToken ct = default);

    /// <summary>Copia la hoja (filtros + widgets) al final del libro.</summary>
    Task<AnalisisOpResult> DuplicarHojaAsync(Guid hojaId, AnalisisActor actor, CancellationToken ct = default);

    /// <summary>Una hoja suelta para el componente embebible (null si no existe o no la puede ver).</summary>
    Task<AnalisisHojaDto?> ObtenerHojaAsync(Guid hojaId, AnalisisActor actor, CancellationToken ct = default);

    /// <summary>Widgets sueltos visibles para el actor, en el orden pedido (los que no puede ver se omiten).</summary>
    Task<IReadOnlyList<AnalisisWidgetDto>> ObtenerWidgetsAsync(IReadOnlyList<Guid> widgetIds, AnalisisActor actor, CancellationToken ct = default);

    /// <summary>Hojas de los libros visibles, para elegir cual mostrar en un modulo anfitrion. Con
    /// <paramref name="soloEditables"/> solo las de libros que el actor puede editar (crear un indicador
    /// desde otro modulo).</summary>
    Task<IReadOnlyList<AnalisisHojaRef>> ListarHojasAsync(AnalisisActor actor, CancellationToken ct = default, bool soloEditables = false);

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
                porHoja.TryGetValue(h.Id, out var ws) ? ws : new List<AnalisisWidgetDto>(), h.FiltrosJson)).ToList());
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
        if (ValidarWidget(widget) is string invalido)
        {
            return AnalisisOpResult.Falla(invalido);
        }

        await using var db = _factory.Create();
        var (hoja, error) = await HojaEditableAsync(db, hojaId, actor, ct);
        if (hoja is null)
        {
            return AnalisisOpResult.Falla(error!);
        }

        var orden = await db.AnalisisWidgets.Where(w => w.HojaId == hojaId).Select(w => (int?)w.Orden).MaxAsync(ct) ?? -1;
        var entity = new AnalisisWidget { HojaId = hojaId, Orden = orden + 1, RowSpan = 1 };
        Aplicar(entity, widget);
        db.AnalisisWidgets.Add(entity);
        await db.SaveChangesAsync(ct);
        return AnalisisOpResult.Hecho(entity.Id);
    }

    public async Task<AnalisisOpResult> ActualizarWidgetAsync(Guid widgetId, AnalisisNuevoWidget widget, AnalisisActor actor, CancellationToken ct = default)
    {
        if (ValidarWidget(widget) is string invalido)
        {
            return AnalisisOpResult.Falla(invalido);
        }

        await using var db = _factory.Create();
        var (entity, error) = await WidgetEditableAsync(db, widgetId, actor, ct);
        if (entity is null)
        {
            return AnalisisOpResult.Falla(error!);
        }

        Aplicar(entity, widget);
        await db.SaveChangesAsync(ct);
        return AnalisisOpResult.Hecho(widgetId);
    }

    public async Task<AnalisisOpResult> GuardarFiltrosHojaAsync(Guid hojaId, HojaFiltros filtros, AnalisisActor actor, CancellationToken ct = default)
    {
        if (filtros.Desde is DateOnly d && filtros.Hasta is DateOnly h && d > h)
        {
            return AnalisisOpResult.Falla("La fecha inicial no puede ser posterior a la final.");
        }

        await using var db = _factory.Create();
        var (hoja, error) = await HojaEditableAsync(db, hojaId, actor, ct);
        if (hoja is null)
        {
            return AnalisisOpResult.Falla(error!);
        }

        hoja.FiltrosJson = filtros.ToJson();
        await db.SaveChangesAsync(ct);
        return AnalisisOpResult.Hecho(hojaId);
    }

    private string? ValidarWidget(AnalisisNuevoWidget widget)
    {
        if (Limpio(widget.Titulo, MaxTitulo) is null)
        {
            return "El widget necesita un titulo.";
        }

        var tieneSpec = widget.Spec is not null;
        var tieneCurado = !string.IsNullOrWhiteSpace(widget.IndicadorKey);
        if (tieneSpec == tieneCurado)
        {
            return "El widget debe tener una consulta o un indicador curado (uno solo).";
        }

        if (tieneCurado && _curados.Find(widget.IndicadorKey!) is null)
        {
            return "El indicador curado no existe.";
        }

        return null;
    }

    private static void Aplicar(AnalisisWidget entity, AnalisisNuevoWidget widget)
    {
        entity.Tipo = widget.Tipo;
        entity.Titulo = Limpio(widget.Titulo, MaxTitulo)!;
        entity.SpecJson = widget.Spec is not null ? AnalisisJson.Serialize(widget.Spec) : null;
        entity.IndicadorKey = widget.Spec is null ? widget.IndicadorKey!.Trim() : null;
        entity.ConfigJson = widget.ConfigJson;
        entity.ContextBindingsJson = widget.ContextBindingsJson;
        entity.ColSpan = Math.Clamp(widget.ColSpan, 1, MaxColumnas);
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

    public async Task<AnalisisOpResult> GuardarLayoutAsync(Guid hojaId, IReadOnlyList<AnalisisLayoutItem> layout, AnalisisActor actor, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var (hoja, error) = await HojaEditableAsync(db, hojaId, actor, ct);
        if (hoja is null)
        {
            return AnalisisOpResult.Falla(error!);
        }

        await using var tx = await db.BeginTransactionAsync(ct);
        var widgets = await db.AnalisisWidgets.Where(w => w.HojaId == hojaId).ToListAsync(ct);
        var porId = widgets.ToDictionary(w => w.Id);
        if (layout.Count != widgets.Count || layout.Select(l => l.WidgetId).Distinct().Count() != layout.Count
            || layout.Any(l => !porId.ContainsKey(l.WidgetId)))
        {
            return AnalisisOpResult.Falla("La hoja cambio mientras la organizabas. Recarga e intenta de nuevo.");
        }

        for (var i = 0; i < layout.Count; i++)
        {
            var w = porId[layout[i].WidgetId];
            w.Orden = i;
            w.ColSpan = Math.Clamp(layout[i].ColSpan, 1, MaxColumnas);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return AnalisisOpResult.Hecho(hojaId);
    }

    public async Task<AnalisisOpResult> DuplicarWidgetAsync(Guid widgetId, AnalisisActor actor, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var (original, error) = await WidgetEditableAsync(db, widgetId, actor, ct);
        if (original is null)
        {
            return AnalisisOpResult.Falla(error!);
        }

        await using var tx = await db.BeginTransactionAsync(ct);
        var hermanos = await db.AnalisisWidgets.Where(w => w.HojaId == original.HojaId)
            .OrderBy(w => w.Orden).ThenBy(w => w.CreatedAt).ToListAsync(ct);
        var copia = CopiarWidget(original, original.HojaId, Limpio(original.Titulo + " (copia)", MaxTitulo)!);
        hermanos.Insert(hermanos.FindIndex(w => w.Id == widgetId) + 1, copia);
        for (var k = 0; k < hermanos.Count; k++)
        {
            hermanos[k].Orden = k;
        }

        db.AnalisisWidgets.Add(copia);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return AnalisisOpResult.Hecho(copia.Id);
    }

    public async Task<AnalisisOpResult> DuplicarHojaAsync(Guid hojaId, AnalisisActor actor, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var (hoja, error) = await HojaEditableAsync(db, hojaId, actor, ct);
        if (hoja is null)
        {
            return AnalisisOpResult.Falla(error!);
        }

        await using var tx = await db.BeginTransactionAsync(ct);
        var orden = await db.AnalisisHojas.Where(h => h.LibroId == hoja.LibroId).Select(h => (int?)h.Orden).MaxAsync(ct) ?? -1;
        var nueva = new AnalisisHoja
        {
            LibroId = hoja.LibroId,
            Nombre = Limpio(hoja.Nombre + " (copia)", MaxNombreHoja)!,
            Orden = orden + 1,
            FiltrosJson = hoja.FiltrosJson
        };
        db.AnalisisHojas.Add(nueva);
        var widgets = await db.AnalisisWidgets.AsNoTracking().Where(w => w.HojaId == hojaId).ToListAsync(ct);
        foreach (var w in widgets)
        {
            db.AnalisisWidgets.Add(CopiarWidget(w, nueva.Id, w.Titulo));
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return AnalisisOpResult.Hecho(nueva.Id);
    }

    public async Task<AnalisisHojaDto?> ObtenerHojaAsync(Guid hojaId, AnalisisActor actor, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var hoja = await db.AnalisisHojas.AsNoTracking().Include(h => h.Libro).FirstOrDefaultAsync(h => h.Id == hojaId, ct);
        if (hoja?.Libro is not { } libro || !PuedeVer(libro.OwnerUserId, libro.Visibilidad, libro.AllowedUserIdsJson, actor))
        {
            return null;
        }

        var widgets = await db.AnalisisWidgets.AsNoTracking().Where(w => w.HojaId == hojaId)
            .OrderBy(w => w.Orden).ThenBy(w => w.CreatedAt).ToListAsync(ct);
        return new AnalisisHojaDto(hoja.Id, hoja.Nombre, hoja.Orden, widgets.Select(ToDto).ToList(), hoja.FiltrosJson);
    }

    public async Task<IReadOnlyList<AnalisisWidgetDto>> ObtenerWidgetsAsync(IReadOnlyList<Guid> widgetIds, AnalisisActor actor, CancellationToken ct = default)
    {
        if (widgetIds.Count == 0)
        {
            return Array.Empty<AnalisisWidgetDto>();
        }

        await using var db = _factory.Create();
        var ids = widgetIds.Distinct().ToList();
        // Hoja y libro con filtro global (tenant + soft-delete): un widget de hoja o libro borrado no sale.
        var filas = await db.AnalisisWidgets.AsNoTracking()
            .Where(w => ids.Contains(w.Id) && w.Hoja != null && w.Hoja.Libro != null)
            .Select(w => new { Widget = w, w.Hoja!.Libro!.OwnerUserId, w.Hoja.Libro.Visibilidad, w.Hoja.Libro.AllowedUserIdsJson })
            .ToListAsync(ct);
        var visibles = filas
            .Where(f => PuedeVer(f.OwnerUserId, f.Visibilidad, f.AllowedUserIdsJson, actor))
            .ToDictionary(f => f.Widget.Id, f => ToDto(f.Widget));
        return ids.Where(visibles.ContainsKey).Select(id => visibles[id]).ToList();
    }

    public async Task<IReadOnlyList<AnalisisHojaRef>> ListarHojasAsync(AnalisisActor actor, CancellationToken ct = default, bool soloEditables = false)
    {
        await using var db = _factory.Create();
        var filas = await db.AnalisisHojas.AsNoTracking()
            .Where(h => h.Libro != null)
            .OrderBy(h => h.Libro!.Nombre).ThenBy(h => h.Orden)
            .Select(h => new { h.Id, h.LibroId, Libro = h.Libro!.Nombre, h.Nombre, h.Libro.OwnerUserId, h.Libro.Visibilidad, h.Libro.AllowedUserIdsJson })
            .ToListAsync(ct);
        return filas
            .Where(f => soloEditables
                ? actor.EsAdmin || (actor.UserId is Guid u && f.OwnerUserId == u)
                : PuedeVer(f.OwnerUserId, f.Visibilidad, f.AllowedUserIdsJson, actor))
            .Select(f => new AnalisisHojaRef(f.Id, f.LibroId, f.Libro, f.Nombre))
            .ToList();
    }

    private static AnalisisWidget CopiarWidget(AnalisisWidget w, Guid hojaId, string titulo) => new()
    {
        HojaId = hojaId,
        Tipo = w.Tipo,
        Titulo = titulo,
        SpecJson = w.SpecJson,
        IndicadorKey = w.IndicadorKey,
        ConfigJson = w.ConfigJson,
        ContextBindingsJson = w.ContextBindingsJson,
        Orden = w.Orden,
        ColSpan = w.ColSpan,
        RowSpan = w.RowSpan
    };

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
        new(w.Id, w.HojaId, w.Tipo, w.Titulo, w.SpecJson, w.IndicadorKey, w.ConfigJson, w.Orden, w.ColSpan, w.RowSpan, w.ContextBindingsJson);
}
