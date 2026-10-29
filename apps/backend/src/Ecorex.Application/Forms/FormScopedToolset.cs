using System.Text.Json;
using Ecorex.Application.Forms.Calc;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms;

/// <summary>
/// ADR-0131: toolset ACOTADO a UN formulario (la "capacidad MCP" del control AiIntake). Se CONSTRUYE con la
/// definicion del formulario actual y la config del control; solo conoce las GRILLAS y campos de ESA definicion.
/// NO escribe en BD ni toca otros formularios/registros/tenants (prohibido por construccion): rellenar_tabla
/// RECOLECTA filas propuestas que el servicio devuelve para que el HUMANO confirme y se apliquen despues. No
/// reusa FormAuthoringToolset (ese edita DEFINICIONES; aqui son DATOS de un registro).
/// </summary>
public sealed class FormScopedToolset
{
    private static readonly FormControlType[] ScalarInputs =
    {
        FormControlType.Text, FormControlType.TextArea, FormControlType.Select, FormControlType.MultiCheck,
        FormControlType.Radio, FormControlType.Toggle, FormControlType.Number, FormControlType.Date,
        FormControlType.Time, FormControlType.DateTime
    };

    private readonly IReadOnlyList<FormQuestionDto> _grids;
    private readonly HashSet<string> _scalarFields;
    private readonly IReadOnlyList<(string Field, HashSet<string> Blocks)> _lienzos;
    private readonly int _maxRows;
    private readonly List<(string Grid, Dictionary<string, string> Row)> _rows = new();
    private readonly Dictionary<string, string> _fields = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _blocks = new(StringComparer.OrdinalIgnoreCase);

    public FormScopedToolset(FormDefinitionDetailDto def, int maxRows)
    {
        _grids = def.Questions.Where(q => q.ControlType == FormControlType.GridDetail).ToList();
        _scalarFields = def.Questions.Where(q => ScalarInputs.Contains(q.ControlType))
            .Select(q => q.FieldCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _lienzos = def.Questions.Where(q => q.ControlType == FormControlType.LienzoBloques)
            .Select(q => (q.FieldCode, LienzoBloquesConfig.Parse(q.OptionsJson).Blocks
                .Select(b => b.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .ToList();
        _maxRows = Math.Max(1, maxRows);
    }

    /// <summary>Filas propuestas por el agente (por grilla), ya filtradas a columnas escribibles.</summary>
    public IReadOnlyList<(string Grid, IReadOnlyDictionary<string, string> Row)> ProposedRows =>
        _rows.Select(r => (r.Grid, (IReadOnlyDictionary<string, string>)r.Row)).ToList();
    /// <summary>Campos simples (cabecera) propuestos por el agente.</summary>
    public IReadOnlyDictionary<string, string> ProposedFields => _fields;
    /// <summary>Bloques de lienzo propuestos por el agente: lienzo(field_code) -> { blockId: texto }.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ProposedBlocks =>
        _blocks.ToDictionary(k => k.Key, v => (IReadOnlyDictionary<string, string>)v.Value, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AiToolSpec> GetSpecs()
    {
        var specs = new List<AiToolSpec>(Specs);
        // set_bloque solo se ofrece cuando el formulario tiene al menos un control LienzoBloques (no ensucia AiIntake).
        if (_lienzos.Count > 0)
        {
            specs.Add(new AiToolSpec("set_bloque",
                "PROPONE el texto de un BLOQUE de un Lienzo de ESTE formulario (NO escribe aun; el humano confirma). " +
                "'lienzo' es el field_code del control de lienzo y 'block' el id del bloque. Llama una vez por bloque.",
                """{"type":"object","properties":{"lienzo":{"type":"string"},"block":{"type":"string"},"valor":{"type":"string"}},"required":["lienzo","block","valor"],"additionalProperties":false}"""));
        }
        return specs;
    }

    private static readonly AiToolSpec[] Specs =
    {
        new AiToolSpec("get_grid_schema",
            "Devuelve el ESQUEMA de columnas de una grilla de ESTE formulario: por columna el colId, label, tipo, " +
            "si es ESCRIBIBLE (las calculadas/consecutivas NO) y, si es de lista, las opciones validas. Usala " +
            "ANTES de rellenar_tabla para conocer los colId exactos.",
            """{"type":"object","properties":{"grid":{"type":"string","description":"field_code de la grilla destino"}},"required":["grid"],"additionalProperties":false}"""),
        new AiToolSpec("rellenar_tabla",
            "PROPONE filas para una grilla de ESTE formulario (NO escribe aun; el humano confirma). 'filas' es un " +
            "arreglo de objetos {colId: valor} usando los colId del esquema. NO incluyas columnas calculadas o " +
            "consecutivas (se recalculan solas). No inventes datos: deja vacio lo que no aparezca en el documento.",
            """{"type":"object","properties":{"grid":{"type":"string"},"filas":{"type":"array","items":{"type":"object","additionalProperties":true}}},"required":["grid","filas"],"additionalProperties":false}"""),
        new AiToolSpec("set_campo",
            "PROPONE el valor de un campo simple (cabecera) de ESTE formulario por su field_code. Opcional.",
            """{"type":"object","properties":{"field":{"type":"string"},"valor":{"type":"string"}},"required":["field","valor"],"additionalProperties":false}"""),
    };

    public AgentToolResult Execute(string name, string argsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            var a = doc.RootElement;
            return name switch
            {
                "get_grid_schema" => GetGridSchema(a),
                "rellenar_tabla" => RellenarTabla(a),
                "set_campo" => SetCampo(a),
                "set_bloque" => SetBloque(a),
                _ => Err($"Herramienta desconocida: {name}")
            };
        }
        catch (JsonException) { return Err("Los argumentos no son un JSON valido."); }
        catch (Exception ex) { return Err($"Error en {name}: {ex.Message}"); }
    }

    private FormQuestionDto? FindGrid(string? code)
        => string.IsNullOrWhiteSpace(code) ? null
           : _grids.FirstOrDefault(g => string.Equals(g.FieldCode, code!.Trim(), StringComparison.OrdinalIgnoreCase));

    // Columna ESCRIBIBLE por el agente: no calculada (Calc), no consecutiva (Seq), no rollup, no gestion.
    // Las lookup/calculadas se llenan solas al recalcular tras aplicar.
    private static bool IsWritable(FormGridColumn c)
        => string.IsNullOrWhiteSpace(c.Calc) && string.IsNullOrWhiteSpace(c.Seq)
           && string.IsNullOrWhiteSpace(c.Rollup) && !string.Equals(c.Kind, "gestion", StringComparison.OrdinalIgnoreCase);

    private AgentToolResult GetGridSchema(JsonElement a)
    {
        var grid = Str(a, "grid");
        var q = FindGrid(grid);
        if (q is null) { return Err($"La grilla '{grid}' no existe en este formulario. Grillas disponibles: {string.Join(", ", _grids.Select(g => g.FieldCode))}."); }
        var cols = FormGridCalculator.ParseColumns(q.OptionsJson);
        var schema = cols.Select(c => new
        {
            colId = c.Id,
            label = c.Label,
            tipo = c.Kind,
            escribible = IsWritable(c),
            opciones = (c.Options is { Count: > 0 }) ? c.Options.Select(o => o.Value ?? o.Id).ToList() : null
        }).ToList();
        return Ok(new { ok = true, grid = q.FieldCode, columnas = schema });
    }

    private AgentToolResult RellenarTabla(JsonElement a)
    {
        var grid = Str(a, "grid");
        var q = FindGrid(grid);
        if (q is null) { return Err($"La grilla '{grid}' no existe en este formulario."); }
        var cols = FormGridCalculator.ParseColumns(q.OptionsJson);
        var writable = cols.Where(IsWritable).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!a.TryGetProperty("filas", out var filasEl) || filasEl.ValueKind != JsonValueKind.Array)
        {
            return Err("'filas' debe ser un arreglo de objetos {colId: valor}.");
        }

        var accepted = 0;
        var ignored = 0;
        foreach (var f in filasEl.EnumerateArray())
        {
            if (CountRows(q.FieldCode) >= _maxRows) { break; }
            if (f.ValueKind != JsonValueKind.Object) { continue; }
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in f.EnumerateObject())
            {
                if (!writable.Contains(p.Name)) { ignored++; continue; }   // columna calculada o desconocida
                var val = ScalarToString(p.Value);
                if (val is not null) { row[p.Name] = val; }
            }
            if (row.Count > 0) { _rows.Add((q.FieldCode, row)); accepted++; }
        }
        return Ok(new { ok = true, grid = q.FieldCode, filas_propuestas = accepted, columnas_ignoradas = ignored, tope = _maxRows });
    }

    private AgentToolResult SetCampo(JsonElement a)
    {
        var field = Str(a, "field")?.Trim();
        var valor = Str(a, "valor");
        if (string.IsNullOrWhiteSpace(field)) { return Err("Falta 'field'."); }
        if (!_scalarFields.Contains(field!)) { return Err($"'{field}' no es un campo simple de este formulario."); }
        _fields[field!] = valor ?? "";
        return Ok(new { ok = true, field, valor });
    }

    private AgentToolResult SetBloque(JsonElement a)
    {
        var lienzo = Str(a, "lienzo")?.Trim();
        var block = Str(a, "block")?.Trim();
        var valor = Str(a, "valor");
        if (string.IsNullOrWhiteSpace(lienzo)) { return Err("Falta 'lienzo'."); }
        if (string.IsNullOrWhiteSpace(block)) { return Err("Falta 'block'."); }
        var lz = _lienzos.FirstOrDefault(l => string.Equals(l.Field, lienzo, StringComparison.OrdinalIgnoreCase));
        if (lz.Field is null) { return Err($"'{lienzo}' no es un lienzo de este formulario. Lienzos: {string.Join(", ", _lienzos.Select(l => l.Field))}."); }
        if (!lz.Blocks.Contains(block!)) { return Err($"El bloque '{block}' no existe en el lienzo '{lienzo}'. Bloques: {string.Join(", ", lz.Blocks)}."); }
        if (!_blocks.TryGetValue(lz.Field, out var map)) { map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); _blocks[lz.Field] = map; }
        map[block!] = valor ?? "";
        return Ok(new { ok = true, lienzo = lz.Field, block });
    }

    private int CountRows(string grid) => _rows.Count(r => string.Equals(r.Grid, grid, StringComparison.OrdinalIgnoreCase));

    private static string? ScalarToString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => v.GetRawText()   // arreglos/objetos (p.ej. multicheck) como JSON
    };

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static AgentToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), false);
    private static AgentToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), false);
}
