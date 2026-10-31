using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Filtrado de una lista de Contenedor en formularios: filtros fijos (columna = valor) y, sobre
/// todo, filtros EN CASCADA (columna = valor tomado de OTRO campo del mismo formulario). Se guarda
/// serializado en <c>FormQuestion.FilterJson</c>, asi que NO requiere columna/migracion nueva.
///
/// Reparto de responsabilidades (a proposito, para que el adaptador de datos siga siendo generico):
/// - El DISENADOR arma este spec con UI (el usuario nunca escribe JSON).
/// - El RENDER resuelve la cascada contra el estado del formulario (lee el campo padre, trae de su
///   fila la columna indicada) y produce un diccionario columna->valor YA resuelto.
/// - El adaptador (<see cref="DataContainerLookupSource"/>) solo aplica ese diccionario ya resuelto
///   (<see cref="FormLookupRequest.EqFilters"/>): no conoce el formulario ni otras tablas.
///
/// Compatibilidad: un FilterJson antiguo con forma plana <c>{"col":"val"}</c> se interpreta como
/// filtros fijos, para no romper configuraciones previas.
/// </summary>
public sealed record FormLookupFilterSpec(
    IReadOnlyDictionary<string, string>? Fixed = null,
    IReadOnlyList<FormLookupCascade>? Cascade = null)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [JsonIgnore]
    public bool HasCascade => Cascade is { Count: > 0 };

    [JsonIgnore]
    public bool IsEmpty => (Fixed is null || Fixed.Count == 0) && !HasCascade;

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>
    /// Lee el spec desde el texto guardado. Acepta la forma nueva (<c>{"fixed":{..},"cascade":[..]}</c>)
    /// y la forma plana antigua (<c>{"col":"val"}</c>, tratada como filtros fijos). Defensivo: cualquier
    /// JSON invalido o vacio devuelve un spec vacio (sin filtros), nunca lanza.
    /// </summary>
    public static FormLookupFilterSpec Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return new FormLookupFilterSpec(); }
        var text = raw.TrimStart();
        if (text.Length == 0 || text[0] != '{') { return new FormLookupFilterSpec(); }
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return new FormLookupFilterSpec(); }

            var hasNewShape = root.TryGetProperty("cascade", out _)
                || root.TryGetProperty("fixed", out _);

            if (hasNewShape)
            {
                var fixedMap = ReadFixed(root);
                var cascade = ReadCascade(root);
                return new FormLookupFilterSpec(fixedMap, cascade);
            }

            // Forma plana antigua: todo par string->string es un filtro fijo.
            var plain = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in root.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    var v = p.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(p.Name) && v is not null) { plain[p.Name] = v; }
                }
            }
            return new FormLookupFilterSpec(plain.Count > 0 ? plain : null, null);
        }
        catch (JsonException)
        {
            return new FormLookupFilterSpec();
        }
    }

    private static IReadOnlyDictionary<string, string>? ReadFixed(JsonElement root)
    {
        if (!root.TryGetProperty("fixed", out var fx) || fx.ValueKind != JsonValueKind.Object) { return null; }
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in fx.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String)
            {
                var v = p.Value.GetString();
                if (!string.IsNullOrWhiteSpace(p.Name) && v is not null) { map[p.Name] = v; }
            }
        }
        return map.Count > 0 ? map : null;
    }

    private static IReadOnlyList<FormLookupCascade>? ReadCascade(JsonElement root)
    {
        if (!root.TryGetProperty("cascade", out var arr) || arr.ValueKind != JsonValueKind.Array) { return null; }
        var list = new List<FormLookupCascade>();
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) { continue; }
            var column = Str(e, "column") ?? "";
            var fromField = Str(e, "fromField") ?? "";
            var fromColumn = Str(e, "fromColumn") ?? "";
            var require = !e.TryGetProperty("require", out var rq)
                || rq.ValueKind != JsonValueKind.False; // por defecto obligatorio
            // Se conservan entradas INCOMPLETAS a proposito: el disenador las muestra para que el usuario
            // las complete (si se descartaran aqui, "+ Agregar filtro" nunca persistiria una fila vacia).
            // El motor (render/adaptador) ignora las que no esten completas via IsComplete.
            list.Add(new FormLookupCascade(column, fromField, fromColumn, require));
        }
        return list.Count > 0 ? list : null;
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// Un filtro en cascada: la columna <paramref name="Column"/> de ESTA tabla debe igualar el valor
/// que tenga, en la fila elegida del campo padre <paramref name="FromField"/> (otro campo lista de
/// este formulario), la columna <paramref name="FromColumn"/>.
/// </summary>
/// <param name="Require">
/// true (por defecto): mientras el campo padre este vacio, la lista no devuelve nada (obliga a elegir
/// primero el padre). false: si el padre esta vacio, el filtro se omite y se ve la lista completa.
/// </param>
public sealed record FormLookupCascade(string Column, string FromField, string FromColumn, bool Require = true)
{
    /// <summary>Una entrada solo filtra cuando tiene las tres piezas; el motor ignora las incompletas.</summary>
    [JsonIgnore]
    public bool IsComplete => !string.IsNullOrWhiteSpace(Column)
        && !string.IsNullOrWhiteSpace(FromField)
        && !string.IsNullOrWhiteSpace(FromColumn);
}
