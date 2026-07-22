using System.Text.Json;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Configuracion de lookup de UNA COLUMNA de tabla (GridDetail). Hasta ahora el lookup con
/// autollenado solo existia a nivel de CAMPO (FormQuestionDto.SourceKind + AutofillMapJson); esto
/// lo lleva a la columna reusando la MISMA capa (<see cref="IFormLookupService"/> y sus
/// adaptadores Item/Tercero/DataContainer), sin ningun codigo especifico de inventario.
///
/// <para><see cref="ValueField"/> es la diferencia importante con el lookup de campo: a nivel de
/// campo se guarda el id de la entidad, pero en una tabla de cotizacion la celda debe guardar una
/// CLAVE LEGIBLE que el asesor teclea y lee (el SKU). Si viene vacio se guarda el id, igual que el
/// campo.</para>
///
/// <para><see cref="Autofill"/> mapea campoDeLaFuente -&gt; idDeColumnaDestino DENTRO DE LA MISMA
/// FILA. Lo copiado es un SNAPSHOT y queda EDITABLE: el asesor puede ajustar costo, marca o
/// proveedor en una cotizacion puntual sin tocar el catalogo. NO es un vinculo vivo, por lo que
/// nada vuelve a leer la fuente despues de elegir (decision del usuario).</para>
/// </summary>
public sealed record FormGridLookupConfig(
    FormSourceKind SourceKind,
    string? SourceRef,
    string? DisplayField,
    string? ValueField,
    string? FilterJson,
    IReadOnlyDictionary<string, string> Autofill)
{
    /// <summary>Campos que hay que pedirle a la fuente: el de mostrar, el de la clave y los origenes del autollenado.</summary>
    public IReadOnlyList<string> Fields()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(DisplayField)) { set.Add(DisplayField!); }
        if (!string.IsNullOrWhiteSpace(ValueField)) { set.Add(ValueField!); }
        foreach (var source in Autofill.Keys) { set.Add(source); }
        return set.ToList();
    }

    /// <summary>Clave que se guarda en la celda para un resultado: <see cref="ValueField"/> o, si no hay, el id.</summary>
    public string KeyOf(FormLookupItem item)
        => !string.IsNullOrWhiteSpace(ValueField) && item.Fields.TryGetValue(ValueField!, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v!
            : item.Value;
}

/// <summary>
/// Comprobacion de existencias de una columna: esta columna trae el stock disponible y se compara
/// contra la columna <see cref="Against"/> (la cantidad pedida) para avisar en la fila cuando no
/// alcanza. Es declarativo y no bloquea nada: se puede cotizar sobre pedido.
/// </summary>
public sealed record FormGridStockCheck(string Against);

/// <summary>
/// Extras de una columna de tabla que NO viven en FormGridColumn (calculo/agregado): lookup con
/// autollenado, valor por defecto al crear la fila y comprobacion de existencias. Se parsean del
/// MISMO OptionsJson de la pregunta, en paralelo a FormGridCalculator.ParseColumns, para no
/// mezclar las dos responsabilidades ni tocar el contrato del calculador.
/// </summary>
public sealed record FormGridColumnExtras(
    string Id,
    FormGridLookupConfig? Lookup,
    string? Default,
    FormGridStockCheck? StockCheck);

/// <summary>
/// Parseo de los extras de columna del OptionsJson. Defensivo: cualquier columna mal formada se
/// ignora y la tabla sigue funcionando como texto plano (las definiciones viejas [{id,label}] no
/// declaran nada de esto y siguen valiendo).
/// </summary>
public static class FormGridColumnLookupParser
{
    /// <summary>Extras por id de columna. Diccionario vacio si el JSON no es un array valido.</summary>
    public static IReadOnlyDictionary<string, FormGridColumnExtras> Parse(string? optionsJson)
    {
        var map = new Dictionary<string, FormGridColumnExtras>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(optionsJson)) { return map; }
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return map; }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                var id = el.TryGetProperty("id", out var pid) ? pid.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) { continue; }

                var lookup = ParseLookup(el);
                var def = el.TryGetProperty("default", out var pd) ? ReadScalar(pd) : null;
                var stock = ParseStockCheck(el);
                if (lookup is null && def is null && stock is null) { continue; }

                map[id!] = new FormGridColumnExtras(id!, lookup, def, stock);
            }
        }
        catch (JsonException) { /* extras invalidos: la tabla se comporta como texto plano */ }
        return map;
    }

    private static FormGridLookupConfig? ParseLookup(JsonElement col)
    {
        if (!col.TryGetProperty("lookup", out var lk) || lk.ValueKind != JsonValueKind.Object) { return null; }

        // "source" acepta el nombre del enum (Item / Tercero / DataContainer). Si no se reconoce no
        // se arma lookup: mejor una columna de texto que una que consulta la fuente equivocada.
        var sourceName = lk.TryGetProperty("source", out var ps) ? ps.GetString() : null;
        if (!Enum.TryParse<FormSourceKind>(sourceName, ignoreCase: true, out var kind) || kind == FormSourceKind.Options)
        {
            return null;
        }

        var autofill = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (lk.TryGetProperty("autofill", out var af) && af.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in af.EnumerateObject())
            {
                var target = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(target)) { autofill[p.Name] = target!; }
            }
        }

        // El filtro se conserva como JSON crudo: lo interpreta cada adaptador de fuente, no la UI.
        string? filterJson = null;
        if (lk.TryGetProperty("filter", out var pf) && pf.ValueKind == JsonValueKind.Object)
        {
            filterJson = pf.GetRawText();
        }

        return new FormGridLookupConfig(
            kind,
            Trimmed(lk, "sourceRef"),
            Trimmed(lk, "displayField"),
            Trimmed(lk, "valueField"),
            filterJson,
            autofill);
    }

    private static FormGridStockCheck? ParseStockCheck(JsonElement col)
    {
        if (!col.TryGetProperty("stockCheck", out var sc) || sc.ValueKind != JsonValueKind.Object) { return null; }
        var against = Trimmed(sc, "against");
        return string.IsNullOrWhiteSpace(against) ? null : new FormGridStockCheck(against!);
    }

    private static string? Trimmed(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String) { return null; }
        var s = p.GetString()?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    /// <summary>El default se admite como texto o numero (1 y "1" son lo mismo en una celda).</summary>
    private static string? ReadScalar(JsonElement p) => p.ValueKind switch
    {
        JsonValueKind.String => p.GetString(),
        JsonValueKind.Number => p.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };
}
