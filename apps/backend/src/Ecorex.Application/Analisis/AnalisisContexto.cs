using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;

namespace Ecorex.Application.Analisis;

/// <summary>
/// Contexto de EMBEBIDO (ADR-0158 D9): parametros que pone el modulo anfitrion (p.ej. la ficha del
/// tercero pone tercero = id) y que cada widget convierte en un filtro de igualdad sobre el campo que le
/// corresponde en SU fuente. Resolucion: (1) ContextBindingsJson del widget, (2) mapa conocido de las
/// fuentes nativas, (3) un campo de la fuente cuya clave sea el nombre del parametro. Un parametro que el
/// widget no entiende se ignora (no rompe) y el resultado lo marca como "fuera de contexto". Puro.
/// </summary>
public static class AnalisisContexto
{
    public const string Tercero = "tercero";

    private static readonly Dictionary<string, Dictionary<string, string>> Conocidos = new(StringComparer.OrdinalIgnoreCase)
    {
        [TaskItemReportSource.SourceKey] = new(StringComparer.OrdinalIgnoreCase) { [Tercero] = "TerceroId" },
        [WorkLogReportSource.SourceKey] = new(StringComparer.OrdinalIgnoreCase) { [Tercero] = "TerceroId" },
        [TerceroNotaReportSource.SourceKey] = new(StringComparer.OrdinalIgnoreCase) { [Tercero] = "TerceroId" },
        [TerceroReportSource.SourceKey] = new(StringComparer.OrdinalIgnoreCase) { [Tercero] = "Id" }
    };

    /// <summary>Campo de la fuente para el parametro de contexto, o null si el widget no lo entiende.</summary>
    public static string? Resolver(string parametro, ReportSourceDescriptor fuente, string? bindingsJson)
    {
        var propios = HojaFiltroBinder.LeerBindings(bindingsJson);
        string? campo;
        if (propios.TryGetValue(parametro, out var explicito))
        {
            campo = string.IsNullOrWhiteSpace(explicito) ? null : explicito;
        }
        else if (Conocidos.TryGetValue(fuente.Key, out var mapa) && mapa.TryGetValue(parametro, out var conocido))
        {
            campo = conocido;
        }
        else
        {
            campo = parametro;
        }

        return campo is not null && fuente.FindField(campo) is { CanFilter: true } f ? f.Key : null;
    }

    /// <summary>Filtros de igualdad del contexto que este widget entiende, y si ignoro alguno.</summary>
    public static (IReadOnlyList<ReportFilter> Filtros, bool AlgunoIgnorado) Filtros(
        IReadOnlyDictionary<string, string>? contexto, ReportSourceDescriptor fuente, string? bindingsJson)
    {
        var r = new List<ReportFilter>();
        var ignorado = false;
        foreach (var (param, valor) in contexto ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                continue;
            }

            if (Resolver(param, fuente, bindingsJson) is string campo)
            {
                r.Add(ReportFilter.Eq(campo, valor));
            }
            else
            {
                ignorado = true;
            }
        }

        return (r, ignorado);
    }
}
