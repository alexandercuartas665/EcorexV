using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;

namespace Ecorex.Application.Analisis;

/// <summary>
/// Filtros GLOBALES de una hoja (ADR-0158 Ola 3), guardados en <c>AnalisisHoja.FiltrosJson</c>: rango de
/// fechas, vendedor y tablero. Se inyectan a cada widget sobre el campo que le corresponde en SU fuente
/// (ver <see cref="HojaFiltroBinder"/>); un widget cuya fuente no tiene ese campo lo ignora.
/// </summary>
public sealed record HojaFiltros
{
    public DateOnly? Desde { get; init; }

    public DateOnly? Hasta { get; init; }

    public string? Vendedor { get; init; }

    public string? Tablero { get; init; }

    public bool Vacio => Desde is null && Hasta is null && string.IsNullOrWhiteSpace(Vendedor) && string.IsNullOrWhiteSpace(Tablero);

    public static HojaFiltros Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HojaFiltros();
        }

        try
        {
            return JsonSerializer.Deserialize<HojaFiltros>(json, AnalisisJson.Options) ?? new HojaFiltros();
        }
        catch (JsonException)
        {
            return new HojaFiltros();
        }
    }

    public string? ToJson()
    {
        var limpio = this with
        {
            Vendedor = string.IsNullOrWhiteSpace(Vendedor) ? null : Vendedor.Trim(),
            Tablero = string.IsNullOrWhiteSpace(Tablero) ? null : Tablero.Trim()
        };
        return limpio.Vacio ? null : JsonSerializer.Serialize(limpio, AnalisisJson.Options);
    }
}

/// <summary>
/// Enlace "parametro de hoja -> campo de la fuente". Orden de resolucion: (1) lo que el widget declara en
/// <c>ContextBindingsJson</c> ({"fecha":"CampoX","vendedor":"CampoY","tablero":"CampoZ"}; "" = no aplicar),
/// (2) el mapa conocido de las fuentes nativas, (3) convencion por nombre (primer campo fecha; campo cuyo
/// nombre contiene "vendedor"/"tablero"). Puro.
/// </summary>
public static class HojaFiltroBinder
{
    public const string Fecha = "fecha";
    public const string Vendedor = "vendedor";
    public const string Tablero = "tablero";

    public static readonly string[] Parametros = { Fecha, Vendedor, Tablero };

    private static readonly Dictionary<string, Dictionary<string, string>> Conocidos = new(StringComparer.OrdinalIgnoreCase)
    {
        [TaskItemReportSource.SourceKey] = new() { [Fecha] = "CreatedAt", [Vendedor] = "Vendedor", [Tablero] = "Board" },
        [TerceroReportSource.SourceKey] = new() { [Fecha] = "CreatedAt", [Vendedor] = "Vendedor" },
        [TerceroNotaReportSource.SourceKey] = new() { [Fecha] = "Fecha", [Vendedor] = "Vendedor" },
        [WorkLogReportSource.SourceKey] = new() { [Fecha] = "Fecha", [Tablero] = "Tablero" }
    };

    public static Dictionary<string, string> LeerBindings(string? json)
    {
        var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return r;
        }

        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            foreach (var (k, v) in d ?? new())
            {
                r[k] = v ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Bindings corruptos: se usa la convencion.
        }

        return r;
    }

    /// <summary>Campo de la fuente para el parametro, o null si el widget no lo entiende.</summary>
    public static string? Resolver(string parametro, ReportSourceDescriptor fuente, string? bindingsJson)
    {
        var propios = LeerBindings(bindingsJson);
        if (propios.TryGetValue(parametro, out var explicito))
        {
            return string.IsNullOrWhiteSpace(explicito) ? null : Valido(parametro, fuente, explicito);
        }

        if (Conocidos.TryGetValue(fuente.Key, out var mapa) && mapa.TryGetValue(parametro, out var conocido))
        {
            return Valido(parametro, fuente, conocido);
        }

        return parametro switch
        {
            Fecha => fuente.Fields.FirstOrDefault(f => f.Type == ReportFieldType.Date && f.CanFilter)?.Key,
            _ => fuente.Fields.FirstOrDefault(f => f.CanFilter && f.Type == ReportFieldType.Text
                && (f.Key.Contains(parametro, StringComparison.OrdinalIgnoreCase)
                    || f.DisplayName.Contains(parametro, StringComparison.OrdinalIgnoreCase)))?.Key
        };
    }

    private static string? Valido(string parametro, ReportSourceDescriptor fuente, string campo)
    {
        var f = fuente.FindField(campo);
        if (f is null || !f.CanFilter)
        {
            return null;
        }

        return parametro == Fecha && f.Type != ReportFieldType.Date ? null : f.Key;
    }

    /// <summary>Filtros extra que la hoja agrega a un widget de esa fuente.</summary>
    public static IReadOnlyList<ReportFilter> Filtros(HojaFiltros? hoja, ReportSourceDescriptor fuente, string? bindingsJson)
    {
        var r = new List<ReportFilter>();
        if (hoja is null || hoja.Vacio)
        {
            return r;
        }

        if ((hoja.Desde is not null || hoja.Hasta is not null) && Resolver(Fecha, fuente, bindingsJson) is string campoFecha)
        {
            // Rango cerrado por dias completos: Hasta incluye todo ese dia (se usa el dia siguiente como tope).
            if (hoja.Desde is DateOnly desde)
            {
                r.Add(new ReportFilter(campoFecha, ReportFilterOperator.GreaterThanOrEqual, new[] { desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }));
            }

            if (hoja.Hasta is DateOnly hasta)
            {
                r.Add(new ReportFilter(campoFecha, ReportFilterOperator.LessThan, new[] { hasta.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }));
            }
        }

        if (!string.IsNullOrWhiteSpace(hoja.Vendedor) && Resolver(Vendedor, fuente, bindingsJson) is string campoVendedor)
        {
            r.Add(new ReportFilter(campoVendedor, ReportFilterOperator.Contains, new[] { hoja.Vendedor.Trim() }));
        }

        if (!string.IsNullOrWhiteSpace(hoja.Tablero) && Resolver(Tablero, fuente, bindingsJson) is string campoTablero)
        {
            r.Add(new ReportFilter(campoTablero, ReportFilterOperator.Contains, new[] { hoja.Tablero.Trim() }));
        }

        return r;
    }
}
