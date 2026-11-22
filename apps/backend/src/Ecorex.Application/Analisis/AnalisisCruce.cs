using System.Globalization;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;

namespace Ecorex.Application.Analisis;

/// <summary>
/// Una condicion de una seleccion hecha tocando un grafico (ADR-0158 Ola 7c): un campo de la fuente del
/// widget de origen igual a un valor (la etiqueta tal cual la pinto el grafico). <see cref="Parametro"/>
/// (fecha/vendedor/tablero) indica que ese campo es el que la hoja usa para ese parametro en la fuente de
/// origen: asi la condicion tambien viaja a widgets de OTRAS fuentes por su campo equivalente.
/// </summary>
public sealed record CondicionCruzada(string? Campo, string Valor, string? Parametro = null)
{
    /// <summary>Nombre del campo para el usuario (lo completa <see cref="AnalisisCruce.Resolver"/>).</summary>
    public string? CampoNombre { get; init; }
}

/// <summary>
/// Lo que el usuario toco en un grafico de la hoja. Filtra los DEMAS widgets de la hoja (el de origen solo
/// resalta el elemento tocado). No se guarda: vive en la vista.
/// </summary>
public sealed record SeleccionCruzada(Guid OrigenWidgetId, string SourceKey, IReadOnlyList<CondicionCruzada> Condiciones)
{
    public string OrigenTitulo { get; init; } = string.Empty;

    /// <summary>Texto del chip: "Estado: Active, Prioridad: High".</summary>
    public string Etiqueta => string.Join(", ", Condiciones.Select(c => $"{c.CampoNombre ?? c.Campo ?? c.Parametro}: {c.Valor}"));

    /// <summary>
    /// Mismo elemento tocado del MISMO widget: compara los valores en orden (las condiciones crudas del clic aun
    /// no traen el campo resuelto, por eso no se comparan campos).
    /// </summary>
    public bool MismosValores(IReadOnlyList<CondicionCruzada> otras) =>
        otras.Count == Condiciones.Count && Condiciones.Zip(otras).All(p => string.Equals(p.First.Valor, p.Second.Valor, StringComparison.Ordinal));

    /// <summary>Elemento del grafico de origen que esta seleccion resalta: (eje, serie).</summary>
    public (string Eje, string? Serie) Elemento => (Condiciones[0].Valor, Condiciones.Count > 1 ? Condiciones[1].Valor : null);
}

/// <summary>
/// Filtro cruzado y detalle por clic (ADR-0158 Ola 7c). Puro: traduce el elemento tocado de un grafico
/// (eje/serie) a condiciones sobre la fuente del widget, y una seleccion a filtros del motor de reportes
/// sobre la fuente de OTRO widget.
/// </summary>
public static class AnalisisCruce
{
    /// <summary>Tope de filas del panel de registros.</summary>
    public const int MaxFilasDetalle = 500;

    /// <summary>Etiqueta del grupo que junta lo que no cabe en el top (no es un valor real).</summary>
    public const string Otros = "Otros";

    /// <summary>
    /// true si los graficos del widget pueden originar una seleccion: consulta propia (no indicador curado
    /// ni combinado) y un grafico con elementos que tocar (no KPI ni velocimetro).
    /// </summary>
    public static bool AdmiteSeleccion(AnalisisWidgetDto w) =>
        string.IsNullOrWhiteSpace(w.IndicadorKey)
        && AnalisisGraficos.EsGrafico(w.Tipo)
        && !WidgetDisenoMapper.EsEscalar(w.Tipo);

    /// <summary>
    /// Elemento tocado -> condiciones sobre la fuente del widget, o null si ese elemento no se puede filtrar
    /// ("Otros", "(sin dato)", widget sin agrupacion). <paramref name="eje"/> es la categoria / fila / dia /
    /// bloque y <paramref name="serie"/> la serie / columna / sub-bloque (cuenta solo con dos agrupaciones).
    /// </summary>
    public static IReadOnlyList<CondicionCruzada>? Condiciones(AnalisisWidgetDto w, WidgetShape? forma, string? eje, string? serie)
    {
        if (!AdmiteSeleccion(w) || forma is null || string.IsNullOrEmpty(eje))
        {
            return null;
        }

        // Calendario: el dia tocado sobre el campo de fecha del widget (se resuelve con la fuente).
        if (forma is CalendarioShape)
        {
            return EsDia(eje, out _) ? new[] { new CondicionCruzada(null, eje, HojaFiltroBinder.Fecha) } : null;
        }

        var spec = AnalisisJson.DeserializeSpec(w.SpecJson);
        if (spec is null || spec.GroupBy.Count == 0 || !Filtrable(eje))
        {
            return null;
        }

        if (forma is CategoriasShape cat && EsGrupoOtros(cat, eje, w))
        {
            return null;
        }

        var r = new List<CondicionCruzada> { new(spec.GroupBy[0], eje) };
        var dosGrupos = spec.GroupBy.Count >= 2 && forma is SerieShape or HeatmapShape;
        if (dosGrupos && !string.IsNullOrEmpty(serie))
        {
            if (!Filtrable(serie))
            {
                return null;
            }

            r.Add(new CondicionCruzada(spec.GroupBy[1], serie));
        }

        return r;
    }

    /// <summary>
    /// "Filtrar la hoja por esto" sobre la lista vigente (una seleccion por widget de origen): si es el MISMO
    /// elemento ya elegido en ese widget, la quita; si no, la agrega o reemplaza la de ese widget (resuelta con
    /// la fuente de origen). Devuelve false si no se pudo (spec invalido o fuente no disponible al agregar).
    /// </summary>
    public static bool Alternar(List<SeleccionCruzada> cruces, AnalisisWidgetDto widget, IReadOnlyList<CondicionCruzada> condiciones, ReportSourceDescriptor? fuente)
    {
        var i = cruces.FindIndex(c => c.OrigenWidgetId == widget.Id);
        if (i >= 0 && cruces[i].MismosValores(condiciones))
        {
            cruces.RemoveAt(i);
            return true;
        }

        var spec = AnalisisJson.DeserializeSpec(widget.SpecJson);
        if (spec is null || fuente is null || !string.Equals(fuente.Key, spec.SourceKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sel = new SeleccionCruzada(widget.Id, spec.SourceKey, Resolver(condiciones, fuente, widget.ContextBindingsJson))
        {
            OrigenTitulo = widget.Titulo
        };
        if (i >= 0)
        {
            cruces[i] = sel;
        }
        else
        {
            cruces.Add(sel);
        }

        return true;
    }

    /// <summary>
    /// Completa las condiciones con la fuente de ORIGEN: el campo de las condiciones por parametro (dia del
    /// calendario), el parametro de hoja de los campos que lo son, y el nombre para mostrar.
    /// </summary>
    public static IReadOnlyList<CondicionCruzada> Resolver(IReadOnlyList<CondicionCruzada> condiciones, ReportSourceDescriptor fuente, string? bindingsJson) =>
        condiciones.Select(c =>
        {
            var campo = c.Campo ?? (c.Parametro is null ? null : HojaFiltroBinder.Resolver(c.Parametro, fuente, bindingsJson));
            var parametro = c.Parametro ?? (campo is null
                ? null
                : HojaFiltroBinder.Parametros.FirstOrDefault(p =>
                    string.Equals(HojaFiltroBinder.Resolver(p, fuente, bindingsJson), campo, StringComparison.OrdinalIgnoreCase)));
            var nombre = campo is null ? null : fuente.FindField(campo)?.DisplayName;
            return c with { Campo = campo, Parametro = parametro, CampoNombre = nombre ?? campo ?? c.Parametro };
        }).ToList();

    /// <summary>
    /// Filtros que una seleccion agrega a un widget de la fuente <paramref name="destino"/>, o null si no
    /// aplica (a esa fuente le falta un campo equivalente: el widget se muestra sin ese filtro y lo avisa).
    /// Misma fuente: el mismo campo. Otra fuente: el campo que esa fuente usa para el mismo parametro de hoja.
    /// </summary>
    public static IReadOnlyList<ReportFilter>? Filtros(SeleccionCruzada sel, ReportSourceDescriptor destino, string? bindingsDestino, TimeZoneInfo? zona)
    {
        var mismaFuente = string.Equals(sel.SourceKey, destino.Key, StringComparison.OrdinalIgnoreCase);
        var r = new List<ReportFilter>();
        foreach (var c in sel.Condiciones)
        {
            var campo = mismaFuente && c.Campo is not null
                ? c.Campo
                : c.Parametro is null ? null : HojaFiltroBinder.Resolver(c.Parametro, destino, bindingsDestino);
            if (campo is null || destino.FindField(campo) is not { CanFilter: true } f)
            {
                return null;
            }

            switch (f.Type)
            {
                case ReportFieldType.Date:
                    // Un dia completo (el grafico agrupa por dia): [00:00, 00:00 del dia siguiente) en la zona del tenant.
                    if (!EsDia(c.Valor, out var dia))
                    {
                        return null;
                    }

                    r.Add(new ReportFilter(f.Key, ReportFilterOperator.GreaterThanOrEqual, new[] { HojaFiltroBinder.Limite(dia, destino, zona) }));
                    r.Add(new ReportFilter(f.Key, ReportFilterOperator.LessThan, new[] { HojaFiltroBinder.Limite(dia.AddDays(1), destino, zona) }));
                    break;

                case ReportFieldType.Boolean:
                    // El grafico pinta los booleanos como Si/No (WidgetShapes).
                    r.Add(ReportFilter.Eq(f.Key, c.Valor switch { "Si" => "true", "No" => "false", var v => v }));
                    break;

                default:
                    r.Add(ReportFilter.Eq(f.Key, c.Valor));
                    break;
            }
        }

        return r;
    }

    /// <summary>
    /// Columnas del panel de registros: las utiles de cada fuente nativa conocida; en otras fuentes, los
    /// campos agrupados y medidos del widget + los primeros de la fuente (sin los de tipo ID). El campo de
    /// enlace (Id de la tarea o del tercero) va al final y la vista lo oculta.
    /// </summary>
    public static IReadOnlyList<string> CamposDetalle(ReportSourceDescriptor fuente, ReportQuerySpec spec)
    {
        string[] conocidos = fuente.Key switch
        {
            TaskItemReportSource.SourceKey => new[] { "Number", "Title", "Status", "Stage", "Board", "AssigneeName", "Cliente", "Vendedor", "CreatedAt", "DueDate" },
            TerceroReportSource.SourceKey => new[] { "Nombre", "Tipo", "Estado", "Perfiles", "Vendedor", "Ciudad", "Nit", "Email", "Telefono", "CreatedAt" },
            WorkLogReportSource.SourceKey => new[] { "Fecha", "Usuario", "TareaNumero", "TareaTitulo", "Horas", "Tipo", "Tablero", "Cliente" },
            TerceroNotaReportSource.SourceKey => new[] { "Fecha", "Tercero", "Accion", "Categoria", "Subcategoria", "Autor", "Valor", "Vendedor" },
            _ => Array.Empty<string>()
        };

        var campos = new List<string>();
        void Agregar(string? key)
        {
            if (!string.IsNullOrWhiteSpace(key) && fuente.FindField(key) is { } f
                && !campos.Contains(f.Key, StringComparer.OrdinalIgnoreCase))
            {
                campos.Add(f.Key);
            }
        }

        // Lo que el widget agrupa y mide siempre se ve (es lo que el usuario toco).
        foreach (var g in spec.GroupBy)
        {
            Agregar(g);
        }

        foreach (var a in spec.Aggregates)
        {
            Agregar(a.FieldKey);
        }

        foreach (var k in conocidos)
        {
            Agregar(k);
        }

        if (conocidos.Length == 0)
        {
            foreach (var f in fuente.Fields.Where(f => !EsCampoId(f)))
            {
                if (campos.Count >= 10)
                {
                    break;
                }

                Agregar(f.Key);
            }
        }

        if (CampoEnlace(fuente) is { } enlace)
        {
            campos.RemoveAll(c => string.Equals(c, enlace.Campo, StringComparison.OrdinalIgnoreCase));
            campos.Add(enlace.Campo);
        }

        return campos;
    }

    /// <summary>Registro que se puede abrir desde el detalle: tipo ("tarea"/"tercero") y el campo con su Id.</summary>
    public static (string Tipo, string Campo)? CampoEnlace(ReportSourceDescriptor fuente) => fuente.Key switch
    {
        TaskItemReportSource.SourceKey when fuente.FindField("Id") is not null => ("tarea", "Id"),
        TerceroReportSource.SourceKey => ("tercero", "Id"),
        TerceroNotaReportSource.SourceKey => ("tercero", "TerceroId"),
        _ => null
    };

    private static bool EsCampoId(ReportField f) =>
        f.Key.EndsWith("Id", StringComparison.Ordinal) || f.DisplayName.Contains("(ID)", StringComparison.OrdinalIgnoreCase);

    private static bool Filtrable(string valor) => !string.Equals(valor, WidgetShapes.SinDato, StringComparison.Ordinal);

    /// <summary>"Otros" es el grupo sintetico solo si es el ultimo item y la lista se recorto al top.</summary>
    private static bool EsGrupoOtros(CategoriasShape cat, string eje, AnalisisWidgetDto w) =>
        string.Equals(eje, Otros, StringComparison.Ordinal)
        && cat.Items.Count == AnalisisWidgetRunner.Top(w.ConfigJson) + 1
        && string.Equals(cat.Items[^1].Etiqueta, Otros, StringComparison.Ordinal);

    private static bool EsDia(string valor, out DateOnly dia) =>
        DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dia);
}
