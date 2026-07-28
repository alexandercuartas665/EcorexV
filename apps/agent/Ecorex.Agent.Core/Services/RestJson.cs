using System.Text.Json;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Utilidades de parseo tolerante de JSON para el <see cref="RestExecutor"/>. Publicas y estaticas a
/// proposito: son la parte con reglas sutiles (objeto-indexado, clave vacia, envoltorios, rutas con
/// indices) y se prueban en aislamiento, sin red.
/// </summary>
public static class RestJson
{
    private static readonly string[] Wrappers = { "data", "items", "results", "records", "rows" };

    /// <summary>
    /// Materializa la coleccion objetivo de la LISTA como pares (clave, valor):
    ///   - Arreglo -> clave null (indice), un par por elemento.
    ///   - Objeto indexado (<c>{"1":{...},"2":{...}}</c>) -> clave = nombre de propiedad, valor = objeto.
    ///
    /// <paramref name="arrayPath"/> null/"" = tolerante: raiz arreglo, envoltorios comunes
    /// (data/items/results/records/rows), primer arreglo de nivel 1, o -si nada de eso- el propio objeto
    /// tratado como coleccion indexada. Con ruta explicita (con puntos/indices) navega hasta ahi.
    /// </summary>
    public static List<(string? Key, JsonElement Value)> Collection(JsonElement root, string? arrayPath)
    {
        var target = string.IsNullOrEmpty(arrayPath)
            ? AutoLocate(root)
            : (TryResolve(root, arrayPath!, out var r) ? r : default);

        return Materialize(target);
    }

    /// <summary>
    /// Localiza el arreglo hijo en el DETALLE (fan-out). <paramref name="childKey"/> es un nombre de
    /// propiedad de UN nivel (no una ruta): null = tolerante (busca un arreglo), <c>""</c> = la propiedad
    /// de clave vacia (OCS: software resuelto). Devuelve sus elementos, o lista vacia si no hay arreglo.
    /// </summary>
    public static List<JsonElement> ChildArray(JsonElement root, string? childKey)
    {
        var list = new List<JsonElement>();
        JsonElement arr;

        if (childKey is null)
        {
            var located = AutoLocate(root);
            if (located.ValueKind != JsonValueKind.Array) { return list; }
            arr = located;
        }
        else
        {
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(childKey, out var v) || v.ValueKind != JsonValueKind.Array)
            {
                return list;
            }
            arr = v;
        }

        foreach (var el in arr.EnumerateArray()) { list.Add(el); }
        return list;
    }

    /// <summary>
    /// Desanida UN nivel cuando el detalle viene envuelto en un objeto-indexado por id
    /// (<c>{"228":{...}}</c>): si <paramref name="childKey"/> NO esta directo en la raiz, baja a la
    /// primera propiedad cuyo valor sea un objeto. OCS lo requiere; para otras APIs es inocuo.
    /// </summary>
    public static JsonElement UnwrapIndexed(JsonElement root, string? childKey)
    {
        if (root.ValueKind != JsonValueKind.Object) { return root; }
        if (childKey is not null && root.TryGetProperty(childKey, out _)) { return root; }
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object) { return prop.Value; }
        }
        return root;
    }

    /// <summary>
    /// Resuelve una ruta con puntos e indices desde <paramref name="start"/>:
    /// <c>hardware.NAME</c>, <c>bios[0].SSN</c>, <c>accountinfo[0].TAG</c>. Un segmento vacio (dos puntos
    /// seguidos) referencia la propiedad de clave vacia. Ruta vacia = el propio elemento.
    /// </summary>
    public static bool TryResolve(JsonElement start, string? path, out JsonElement result)
    {
        result = start;
        if (string.IsNullOrEmpty(path)) { return true; }

        foreach (var seg in ParseSegments(path!))
        {
            if (seg.IsIndex)
            {
                if (result.ValueKind != JsonValueKind.Array || seg.Index < 0 || seg.Index >= result.GetArrayLength())
                {
                    result = default;
                    return false;
                }
                result = result[seg.Index];
            }
            else
            {
                if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(seg.Name!, out var next))
                {
                    result = default;
                    return false;
                }
                result = next;
            }
        }
        return true;
    }

    /// <summary>Valor escalar como texto (InvariantCulture via GetRawText para numeros).</summary>
    public static string? Scalar(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        // Objetos/arreglos: JSON crudo (no se pierde informacion; util si una columna guarda el subarbol).
        _ => el.GetRawText(),
    };

    // ---- internos ----

    private static JsonElement AutoLocate(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) { return root; }
        if (root.ValueKind != JsonValueKind.Object) { return default; }

        foreach (var w in Wrappers)
        {
            if (root.TryGetProperty(w, out var el) && el.ValueKind == JsonValueKind.Array) { return el; }
        }
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Array) { return prop.Value; }
        }
        // Sin arreglo por ningun lado: el objeto ES la coleccion indexada (OCS {"1":{...},...}).
        return root;
    }

    private static List<(string? Key, JsonElement Value)> Materialize(JsonElement target)
    {
        var items = new List<(string?, JsonElement)>();
        if (target.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in target.EnumerateArray()) { items.Add((null, el)); }
        }
        else if (target.ValueKind == JsonValueKind.Object)
        {
            // Objeto indexado: cada propiedad de valor-objeto es un item (clave = id). Se ignoran
            // propiedades escalares sueltas (metadatos) para no ensuciar la coleccion.
            foreach (var prop in target.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object) { items.Add((prop.Name, prop.Value)); }
            }
        }
        return items;
    }

    private readonly struct Seg
    {
        public readonly string? Name;
        public readonly int Index;
        public readonly bool IsIndex;
        private Seg(string? name, int index, bool isIndex) { Name = name; Index = index; IsIndex = isIndex; }
        public static Seg Prop(string name) => new(name, 0, false);
        public static Seg Idx(int i) => new(null, i, true);
    }

    private static IEnumerable<Seg> ParseSegments(string path)
    {
        // Se separa por '.' y dentro de cada parte se extraen los indices [n]. Asi "a..b" da una clave
        // vacia intermedia (clave ""), pero "bios[0].SSN" NO genera un segmento vacio tras el indice.
        var segs = new List<Seg>();
        foreach (var part in path.Split('.'))
        {
            var bracket = part.IndexOf('[');
            var name = bracket >= 0 ? part[..bracket] : part;

            // Propiedad (incluida la clave vacia ""), salvo cuando la parte es solo un indice ("[0]").
            if (bracket != 0) { segs.Add(Seg.Prop(name)); }

            if (bracket < 0) { continue; }

            // Uno o mas indices consecutivos: "[0]", "[0][1]".
            var i = bracket;
            while (i < part.Length && part[i] == '[')
            {
                var j = part.IndexOf(']', i + 1);
                if (j < 0) { break; } // corchete sin cerrar: se ignora el resto de la parte
                if (int.TryParse(part.Substring(i + 1, j - i - 1), out var idx)) { segs.Add(Seg.Idx(idx)); }
                i = j + 1;
            }
        }
        return segs;
    }
}
