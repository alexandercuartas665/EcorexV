using System.Text.Json;
using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Workflows;

/// <inheritdoc />
public sealed class NotifyTokenResolver : INotifyTokenResolver
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _clock;

    // Zona del tenant (America/Bogota = UTC-5, sin horario de verano) mientras el Tenant no guarde la suya.
    private static readonly TimeSpan TenantOffset = TimeSpan.FromHours(-5);

    // {ns.clave}: dos segmentos alfanumericos, insensible a espacios. Ej: {tarea.contacto}, {form.total}.
    private static readonly Regex TokenRegex = new(@"\{\s*([a-zA-Z0-9_]+)\.([a-zA-Z0-9_]+)\s*\}", RegexOptions.Compiled);

    public NotifyTokenResolver(IApplicationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyDictionary<string, string>> BuildAsync(Domain.Entities.TaskItem task, CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Put(string key, string? value)
        {
            var v = value ?? string.Empty;
            map[key] = v;              // bare (para variables HSM por nombre)
            map["tarea." + key] = v;   // con prefijo (para texto libre {tarea.x})
        }

        // Sistema: fecha/hora actuales (zona del tenant). Se exponen como {sistema.fecha} y, si no colisiona,
        // tambien bare (para una variable HSM llamada p.ej. "fecha").
        var now = _clock.GetUtcNow().ToOffset(TenantOffset);
        void PutSys(string key, string value)
        {
            map["sistema." + key] = value;
            if (!map.ContainsKey(key)) { map[key] = value; }
        }
        PutSys("fecha", now.ToString("yyyy-MM-dd"));
        PutSys("hora", now.ToString("HH:mm"));
        PutSys("fechahora", now.ToString("yyyy-MM-dd HH:mm"));

        // Datos de la tarea (mismos alias que el prellenado de formularios).
        var nombre = task.RequesterName ?? string.Empty;
        Put("id", task.Id.ToString());
        Put("numero", task.Number);
        Put("titulo", task.Title);
        Put("cliente", nombre);
        Put("contacto", nombre);
        Put("solicitante", nombre);
        Put("email", task.RequesterEmail);
        Put("correo", task.RequesterEmail);
        Put("telefono", task.RequesterPhone);
        Put("celular", task.RequesterPhone);
        Put("documento", task.RequesterDocument);
        Put("nit", task.RequesterDocument);

        // Datos de los formularios anclados a la tarea (Reference == numero o "numero-n"). Primer valor no
        // vacio por codigo de campo. Se exponen como {form.<codigo>} y, si no colisiona con la tarea, tambien bare.
        var num = task.Number;
        var datas = await _db.FormResponses.AsNoTracking()
            .Where(r => r.IsActive && (r.Reference == num || (r.Reference != null && r.Reference.StartsWith(num + "-"))))
            .OrderBy(r => r.CreatedAt)
            .Select(r => r.Data)
            .ToListAsync(cancellationToken);

        foreach (var data in datas)
        {
            if (string.IsNullOrWhiteSpace(data)) { continue; }
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) { continue; }
                foreach (var field in doc.RootElement.EnumerateObject())
                {
                    var value = ReadFieldValue(field.Value);
                    if (string.IsNullOrWhiteSpace(value)) { continue; }
                    var formKey = "form." + field.Name;
                    if (!map.ContainsKey(formKey)) { map[formKey] = value!; }       // primer valor gana
                    if (!map.ContainsKey(field.Name)) { map[field.Name] = value!; } // bare, sin pisar los de tarea
                }
            }
            catch (JsonException) { /* respuesta corrupta: se ignora */ }
        }

        return map;
    }

    public string Render(string? template, IReadOnlyDictionary<string, string> tokens)
    {
        if (string.IsNullOrEmpty(template)) { return string.Empty; }
        return TokenRegex.Replace(template, m =>
        {
            var key = m.Groups[1].Value + "." + m.Groups[2].Value;
            return tokens.TryGetValue(key, out var v) ? v : string.Empty;
        });
    }

    // Lee el valor de un campo del Data ({ code: { value, type } }); tolera value string o escalar.
    private static string? ReadFieldValue(JsonElement field)
    {
        if (field.ValueKind == JsonValueKind.Object && field.TryGetProperty("value", out var v))
        {
            return v.ValueKind == JsonValueKind.String ? v.GetString() : (v.ValueKind is JsonValueKind.Null ? null : v.ToString());
        }
        if (field.ValueKind == JsonValueKind.String) { return field.GetString(); }
        return null;
    }
}
