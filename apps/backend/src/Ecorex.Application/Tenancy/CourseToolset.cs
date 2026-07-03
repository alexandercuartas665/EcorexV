using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Herramientas (function calling / "MCP") de CURSOS: el agente consulta los cursos eventuales (fecha,
/// cupo, valor) e inscribe a una persona. La inscripcion queda registrada en el modulo de cursos con su
/// estado de pago (no pagado por defecto). Solo ofrece lo que el modulo reporta; nunca inventa cursos.
/// </summary>
public interface ICourseToolset : IAgentToolset
{
}

public sealed class CourseToolset : ICourseToolset
{
    private readonly ICourseService _courses;

    public CourseToolset(ICourseService courses)
    {
        _courses = courses;
    }

    public string GroupKey => "cursos";
    public string GroupLabel => "Cursos";

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            "consultar_cursos",
            "Lista los cursos eventuales del salon con su fecha, valor, cupo y cupos disponibles. NUNCA inventes " +
            "cursos, fechas ni valores: ofrece solo los que devuelve esta herramienta.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),

        new AiToolSpec(
            "inscribir_curso",
            "Inscribe a una persona en un curso. Usala SOLO cuando el cliente confirme a que curso quiere entrar y te " +
            "haya dado su nombre. La inscripcion queda como NO pagada (un asesor coordina el pago). Devuelve los cupos " +
            "restantes o avisa si el curso ya esta lleno.",
            """{"type":"object","properties":{"curso":{"type":"string","description":"Nombre o id del curso"},"persona_nombre":{"type":"string","description":"Nombre de la persona a inscribir"},"persona_telefono":{"type":"string","description":"Telefono de la persona (opcional)"}},"required":["curso","persona_nombre"],"additionalProperties":false}"""),
    };

    public async Task<AgendaToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch
        {
            return Err("Los argumentos no son un JSON valido.");
        }

        try
        {
            return toolName switch
            {
                "consultar_cursos" => await CoursesAsync(cancellationToken),
                "inscribir_curso" => await EnrollAsync(args, actorUserId, cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    private async Task<AgendaToolResult> CoursesAsync(CancellationToken ct)
    {
        var list = await _courses.ListAsync(includeInactive: false, upcomingOnly: true, today: null, archived: false, cancellationToken: ct);
        var data = list.Select(c => new
        {
            id = c.Id,
            nombre = c.Name,
            detalle = c.Description,
            fecha = c.Date.ToString("yyyy-MM-dd"),
            hora = c.StartTime?.ToString("HH\\:mm"),
            valor = c.Price,
            cupo = c.Capacity,
            cupos_disponibles = c.SpotsLeft
        }).ToArray();
        return Ok(new { cursos = data, total = data.Length });
    }

    private async Task<AgendaToolResult> EnrollAsync(JsonElement args, Guid actor, CancellationToken ct)
    {
        var cursoToken = Str(args, "curso");
        var persona = Str(args, "persona_nombre");
        var telefono = Str(args, "persona_telefono");
        if (string.IsNullOrWhiteSpace(cursoToken)) { return Err("Falta el curso."); }
        if (string.IsNullOrWhiteSpace(persona)) { return Err("Falta el nombre de la persona a inscribir."); }

        var courses = await _courses.ListAsync(includeInactive: false, upcomingOnly: false, today: null, archived: false, cancellationToken: ct);
        var course = ResolveCourse(courses, cursoToken!);
        if (course is null) { return Err($"No encontre el curso '{cursoToken}'. Usa consultar_cursos para ver los nombres exactos."); }
        if (course.SpotsLeft <= 0) { return Err($"El curso '{course.Name}' ya no tiene cupo disponible."); }

        var reg = await _courses.AddRegistrationAsync(course.Id, persona!.Trim(), telefono, isPaid: false, actor, ct);
        if (reg is null) { return Err($"No se pudo inscribir (puede que el curso '{course.Name}' acabe de llenarse)."); }

        return Ok(new
        {
            ok = true,
            inscripcion_id = reg.Id,
            curso = course.Name,
            fecha = course.Date.ToString("yyyy-MM-dd"),
            persona = reg.PersonName,
            estado_pago = "no pagado",
            cupos_disponibles = Math.Max(0, course.SpotsLeft - 1),
            mensaje = "Inscripcion registrada. Un asesor coordinara el pago."
        });
    }

    private static CourseDto? ResolveCourse(IReadOnlyList<CourseDto> courses, string token)
    {
        if (Guid.TryParse(token, out var id))
        {
            var byId = courses.FirstOrDefault(c => c.Id == id);
            if (byId is not null) { return byId; }
        }
        var key = Normalize(token);
        return courses.FirstOrDefault(c => Normalize(c.Name) == key)
            ?? courses.FirstOrDefault(c => Normalize(c.Name).Contains(key) || key.Contains(Normalize(c.Name)));
    }

    // ===== Helpers =====

    private static AgendaToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), BookingCreated: false);
    private static AgendaToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), BookingCreated: false);

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string Normalize(string s)
    {
        var n = (s ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in n)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        return sb.ToString();
    }
}
