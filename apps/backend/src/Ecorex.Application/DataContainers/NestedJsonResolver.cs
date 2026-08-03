using System.Text.Json;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Resuelve rutas de JSON con PUNTOS e INDICES (<c>id_type.name</c>, <c>name[0]</c>,
/// <c>address.city.city_name</c>, <c>phones[0].number</c>, <c>contacts[0].email</c>,
/// <c>metadata.created</c>) para el importador REST in-process (<see cref="ApiImportService"/>).
///
/// Es una REPLICA byte-a-byte de la logica de <c>Ecorex.Agent.Core.Services.RestJson</c>
/// (TryResolve + Scalar + ParseSegments) que ya usa el AGENTE Colmena. No se comparte por
/// referencia de proyecto porque el agente vive en OTRA solucion (apps/agent), apunta a
/// net10.0-windows (DPAPI) y no forma parte de apps/backend/Ecorex.sln; y Ecorex.Application es
/// net10.0 multiplataforma (corre en el matriz dual de CI en Linux). Referenciar el agente aqui
/// arrastraria el TFM Windows; referenciar Application desde el agente arrastraria EF Core. Se
/// mantiene UNA sola SEMANTICA (mismos casos: dot-path, indice, indice+dot, ausencia -> no
/// resuelve) y un follow-up (ADR-0059) para promover el helper a un proyecto leaf (Ecorex.Shared)
/// al que ambas soluciones puedan apuntar.
///
/// Regla clave para Upsert: si una ruta NO resuelve, se OMITE del diccionario de la fila (no se
/// escribe cadena vacia), para que el nucleo de ingesta no sobrescriba el valor existente.
/// </summary>
public static class NestedJsonResolver
{
    /// <summary>
    /// Resuelve una ruta con puntos e indices desde <paramref name="start"/>:
    /// <c>id_type.name</c>, <c>phones[0].number</c>, <c>name[0]</c>. Un segmento vacio (dos puntos
    /// seguidos) referencia la propiedad de clave vacia. Ruta vacia/null = el propio elemento.
    /// Devuelve false (y result=default) si algun segmento no existe / el indice esta fuera de rango.
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

    /// <summary>
    /// Proyecta un elemento JSON a una fila (ruta -> valor escalar) resolviendo cada ruta anidada.
    /// Las rutas que NO resuelven se OMITEN (no entran al diccionario): en Upsert eso hace que el
    /// nucleo de ingesta conserve el valor existente en vez de borrarlo con vacio. Una ruta que
    /// resuelve a JSON null SI entra (con valor null): es un "limpiar" explicito del origen.
    /// </summary>
    public static Dictionary<string, string?> ProjectRow(JsonElement element, IEnumerable<string> paths)
    {
        var row = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path) || row.ContainsKey(path)) { continue; }
            if (TryResolve(element, path, out var v))
            {
                row[path] = Scalar(v);
            }
            // else: ruta ausente -> se omite (no sobrescribir en Upsert).
        }
        return row;
    }

    // ---- internos ----

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
