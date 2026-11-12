using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Analisis;

// ---- Paquete portable de un libro (ADR-0158): llevar un libro de un sistema/tenant a otro ----

/// <summary>Un widget dentro del paquete (sin ids ni tenant: solo su definicion visual y de consulta).</summary>
public sealed record AnalisisWidgetPaquete(
    AnalisisTipoWidget Tipo, string Titulo, string? SpecJson, string? IndicadorKey,
    string? ConfigJson, int ColSpan, int RowSpan, string? ContextBindingsJson);

/// <summary>Una hoja dentro del paquete, con sus filtros y sus widgets en orden.</summary>
public sealed record AnalisisHojaPaquete(string Nombre, int Orden, string? FiltrosJson, IReadOnlyList<AnalisisWidgetPaquete> Widgets);

/// <summary>
/// Paquete autocontenido de un libro de indicadores para EXPORTAR/IMPORTAR entre sistemas (ADR-0158).
/// Lleva SOLO la definicion (nombre, hojas, widgets y sus specs/config), nunca ids, tenant, dueno,
/// visibilidad ni datos: al importar nace un libro NUEVO, privado, del usuario que importa. Las consultas
/// a fuentes nativas (Directorio/Tareas/Horas) son iguales en todo sistema; las referencias a un
/// Contenedor o a ids concretos (tablero, usuario, tercero) pueden no existir en el destino y se avisan.
/// </summary>
public sealed record AnalisisLibroPaquete(
    string Formato, int Version, string Nombre, string? Descripcion, IReadOnlyList<AnalisisHojaPaquete> Hojas)
{
    public const string FormatoActual = "ecorex.analisis.libro";
    public const int VersionActual = 1;

    // Topes defensivos al importar un JSON arbitrario (el export propio nunca los alcanza).
    public const int MaxHojas = 50;
    public const int MaxWidgetsPorLibro = 400;
}

/// <summary>Resultado de importar: libro creado + avisos no fatales (referencias que revisar en el destino).</summary>
public sealed record AnalisisImportResultado(bool Ok, Guid? LibroId, string? Nombre, IReadOnlyList<string> Avisos, string? Error)
{
    public static AnalisisImportResultado Falla(string error) => new(false, null, null, Array.Empty<string>(), error);
}

/// <summary>Exportar/importar un libro completo (definicion) entre sistemas o tenants (ADR-0158).</summary>
public interface IAnalisisPortabilidad
{
    /// <summary>Paquete JSON de un libro que el actor puede ver (nombre de archivo + contenido), o null si no.</summary>
    Task<(string FileName, string Json)?> ExportarAsync(Guid libroId, AnalisisActor actor, CancellationToken ct = default);

    /// <summary>Crea un libro NUEVO (del actor, privado) a partir de un paquete JSON. Atomico.</summary>
    Task<AnalisisImportResultado> ImportarAsync(string json, AnalisisActor actor, CancellationToken ct = default);
}

public sealed class AnalisisPortabilidad : IAnalisisPortabilidad
{
    private const int MaxNombreLibro = 150;
    private const int MaxNombreHoja = 120;
    private const int MaxTitulo = 150;

    private readonly IAnalisisService _svc;
    private readonly IApplicationDbContextFactory _factory;
    private readonly IIndicadorCuradoRegistry _curados;

    public AnalisisPortabilidad(IAnalisisService svc, IApplicationDbContextFactory factory, IIndicadorCuradoRegistry curados)
    {
        _svc = svc;
        _factory = factory;
        _curados = curados;
    }

    public async Task<(string FileName, string Json)?> ExportarAsync(Guid libroId, AnalisisActor actor, CancellationToken ct = default)
    {
        // ObtenerLibroAsync aplica la visibilidad: null si el actor no puede ver el libro.
        var libro = await _svc.ObtenerLibroAsync(libroId, actor, ct);
        if (libro is null)
        {
            return null;
        }

        var paquete = new AnalisisLibroPaquete(
            AnalisisLibroPaquete.FormatoActual, AnalisisLibroPaquete.VersionActual, libro.Nombre, libro.Descripcion,
            libro.Hojas
                .OrderBy(h => h.Orden)
                .Select(h => new AnalisisHojaPaquete(h.Nombre, h.Orden, h.FiltrosJson,
                    h.Widgets.OrderBy(w => w.Orden)
                        .Select(w => new AnalisisWidgetPaquete(w.Tipo, w.Titulo, w.SpecJson, w.IndicadorKey, w.ConfigJson, w.ColSpan, w.RowSpan, w.ContextBindingsJson))
                        .ToList()))
                .ToList());

        var json = JsonSerializer.Serialize(paquete, AnalisisJson.Options);
        return (NombreArchivo(libro.Nombre) + ".ecorex-libro.json", json);
    }

    public async Task<AnalisisImportResultado> ImportarAsync(string json, AnalisisActor actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return AnalisisImportResultado.Falla("El archivo esta vacio.");
        }

        AnalisisLibroPaquete? paquete;
        try
        {
            paquete = JsonSerializer.Deserialize<AnalisisLibroPaquete>(json, AnalisisJson.Options);
        }
        catch (JsonException)
        {
            return AnalisisImportResultado.Falla("El archivo no es un JSON valido.");
        }

        if (paquete is null || !string.Equals(paquete.Formato, AnalisisLibroPaquete.FormatoActual, StringComparison.Ordinal))
        {
            return AnalisisImportResultado.Falla("El archivo no es un libro de indicadores exportado por ECOREX.");
        }

        if (paquete.Version > AnalisisLibroPaquete.VersionActual)
        {
            return AnalisisImportResultado.Falla("El archivo viene de una version mas nueva del sistema; actualiza para importarlo.");
        }

        var nombre = Limpio(paquete.Nombre, MaxNombreLibro) ?? "Libro importado";
        var hojasFuente = (paquete.Hojas ?? Array.Empty<AnalisisHojaPaquete>()).Take(AnalisisLibroPaquete.MaxHojas).ToList();
        var avisos = new List<string>();

        await using var db = _factory.Create();
        await using var tx = await db.BeginTransactionAsync(ct);

        var libro = new AnalisisLibro
        {
            Nombre = nombre,
            Descripcion = Limpio(paquete.Descripcion, 1000),
            OwnerUserId = actor.UserId,
            Visibilidad = AnalisisVisibilidad.Privado
        };
        db.AnalisisLibros.Add(libro);

        var totalWidgets = 0;
        var conContenedor = 0;
        var curadosFaltantes = 0;
        var orden = 0;
        foreach (var h in hojasFuente)
        {
            var hoja = new AnalisisHoja
            {
                LibroId = libro.Id,
                Nombre = Limpio(h.Nombre, MaxNombreHoja) ?? $"Hoja {orden + 1}",
                Orden = orden++,
                FiltrosJson = h.FiltrosJson
            };
            db.AnalisisHojas.Add(hoja);

            var ordenW = 0;
            foreach (var w in h.Widgets ?? Array.Empty<AnalisisWidgetPaquete>())
            {
                if (totalWidgets >= AnalisisLibroPaquete.MaxWidgetsPorLibro)
                {
                    avisos.Add($"Se omitieron widgets: el libro supera el tope de {AnalisisLibroPaquete.MaxWidgetsPorLibro}.");
                    break;
                }

                var titulo = Limpio(w.Titulo, MaxTitulo);
                var tieneSpec = !string.IsNullOrWhiteSpace(w.SpecJson);
                var tieneCurado = !string.IsNullOrWhiteSpace(w.IndicadorKey);
                if (titulo is null || tieneSpec == tieneCurado)
                {
                    avisos.Add("Se omitio un widget con definicion incompleta.");
                    continue;
                }

                // Tipo fuera del catalogo (numero desconocido o el reservado): no se puede pintar.
                if (!WidgetDisenoMapper.EsTipoValido(w.Tipo))
                {
                    avisos.Add($"Se omitio el widget \"{titulo}\": su tipo no existe en este sistema.");
                    continue;
                }

                db.AnalisisWidgets.Add(new AnalisisWidget
                {
                    HojaId = hoja.Id,
                    Tipo = w.Tipo,
                    Titulo = titulo,
                    SpecJson = tieneSpec ? w.SpecJson : null,
                    IndicadorKey = tieneSpec ? null : w.IndicadorKey!.Trim(),
                    ConfigJson = w.ConfigJson,
                    ContextBindingsJson = w.ContextBindingsJson,
                    Orden = ordenW++,
                    ColSpan = Math.Clamp(w.ColSpan, 1, AnalisisService.MaxColumnas),
                    RowSpan = 1
                });
                totalWidgets++;

                // Avisos no fatales: referencias que quiza no existan en el destino.
                if (ReferenciaContenedor(w.SpecJson) || ReferenciaContenedor(w.ConfigJson))
                {
                    conContenedor++;
                }

                if (tieneCurado && w.IndicadorKey != CombinadoDiseno.IndicadorKey && _curados.Find(w.IndicadorKey!.Trim()) is null)
                {
                    curadosFaltantes++;
                }
            }
        }

        // El libro nace con al menos una hoja (como en CrearLibroAsync) aunque el paquete venga sin hojas.
        if (orden == 0)
        {
            db.AnalisisHojas.Add(new AnalisisHoja { LibroId = libro.Id, Nombre = "Hoja 1", Orden = 0 });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (conContenedor > 0)
        {
            avisos.Add($"{conContenedor} widget(s) usan un Contenedor de datos: verifica que exista en este sistema y reasigna la fuente si hace falta.");
        }

        if (curadosFaltantes > 0)
        {
            avisos.Add($"{curadosFaltantes} widget(s) usan un indicador que no existe en este sistema y apareceran con error hasta reemplazarlos.");
        }

        return new AnalisisImportResultado(true, libro.Id, libro.Nombre, avisos, null);
    }

    /// <summary>true si el JSON de un widget referencia una fuente de Contenedor (sus ids no cruzan sistemas).</summary>
    private static bool ReferenciaContenedor(string? widgetJson) =>
        !string.IsNullOrEmpty(widgetJson) && widgetJson.Contains("container:", StringComparison.OrdinalIgnoreCase);

    private static string? Limpio(string? valor, int max)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var v = valor.Trim();
        return v.Length > max ? v[..max].Trim() : v;
    }

    /// <summary>Nombre de archivo seguro a partir del titulo del libro (fallback "libro").</summary>
    private static string NombreArchivo(string? nombre)
    {
        var limpio = new string((nombre ?? string.Empty)
            .Where(c => !char.IsControl(c) && Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), c) < 0).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(limpio) ? "libro" : (limpio.Length > 80 ? limpio[..80].Trim() : limpio);
    }
}
