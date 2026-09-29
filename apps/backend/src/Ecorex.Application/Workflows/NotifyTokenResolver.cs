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

    // Meses en espanol para {sistema.fechalarga} (sin depender de la cultura del contenedor).
    private static readonly string[] MesesEs =
    {
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    };

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
        // Fecha en letras (es-CO), p.ej. "10 de septiembre de 2026": las cartas comerciales la piden escrita.
        // Meses hardcodeados para no depender de que la cultura es-CO exista en el contenedor (InvariantGlobalization).
        PutSys("fechalarga", $"{now.Day} de {MesesEs[now.Month - 1]} de {now.Year}");
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

        // Empresa: datos de la Entidad PRINCIPAL del tenant (membrete de documentos, {empresa.*}).
        // Solo con prefijo (no bare): evita colisionar con variables de tarea/formulario.
        var empresa = await _db.Entidades.AsNoTracking()
            .Where(e => e.IsActive && !e.IsArchived)
            .OrderByDescending(e => e.IsPrincipal).ThenBy(e => e.SortOrder).ThenBy(e => e.Codigo)
            .FirstOrDefaultAsync(cancellationToken);
        if (empresa is not null)
        {
            void PutEmp(string key, string? value) => map["empresa." + key] = value ?? string.Empty;
            var nit = empresa.TaxId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(empresa.TaxIdDv)) { nit = nit + "-" + empresa.TaxIdDv; }
            PutEmp("razonsocial", empresa.Nombre);
            PutEmp("nombre", empresa.Nombre);
            PutEmp("nombrecomercial", empresa.NombreComercial);
            PutEmp("sigla", empresa.Sigla);
            PutEmp("nit", nit);
            PutEmp("taxid", empresa.TaxId);
            PutEmp("direccion", empresa.Direccion);
            PutEmp("ciudad", empresa.Ciudad);
            PutEmp("departamento", empresa.Departamento);
            PutEmp("pais", empresa.Pais);
            PutEmp("telefono", empresa.Telefono);
            PutEmp("email", empresa.Email);
            PutEmp("correo", empresa.Email);
            PutEmp("web", empresa.Web);
            PutEmp("representantelegal", empresa.RepresentanteLegal);
            // Logo listo para <img src="{empresa.logo}">: si ya es data URI se usa tal cual; si es base64
            // "pelado" se le antepone el prefijo PNG. Vacio si el tenant no cargo logo.
            var logo = empresa.LogoBase64;
            if (!string.IsNullOrWhiteSpace(logo) && !logo.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                logo = "data:image/png;base64," + logo.Trim();
            }
            PutEmp("logo", logo);
        }

        // Tercero del Directorio (000232) enlazado a la tarea: tokens {tercero.*} y alias {directorio.*}
        // (ADR-0114 Ola 3). Columnas + campos dinamicos de las fichas (jsonb) -> p.ej. {directorio.direccion}.
        if (task.TerceroId is Guid terceroId)
        {
            var tercero = await _db.Terceros.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == terceroId, cancellationToken);
            if (tercero is not null)
            {
                void PutTercero(string key, string? value)
                {
                    var norm = NormalizeKey(key);
                    if (norm.Length == 0) { return; }
                    var v = value ?? string.Empty;
                    map.TryAdd("tercero." + norm, v);
                    map.TryAdd("directorio." + norm, v);
                }
                PutTercero("nombre", tercero.Nombre);
                PutTercero("razonsocial", tercero.Nombre);
                PutTercero("ciudad", tercero.Ciudad);
                PutTercero("email", tercero.Email);
                PutTercero("correo", tercero.Email);
                PutTercero("telefono", tercero.Telefono);
                PutTercero("celular", tercero.Telefono);
                PutTercero("sector", tercero.Sector);
                PutTercero("cargo", tercero.Cargo);
                PutTercero("tipo", tercero.Tipo.ToString());
                PutTercero("identificacion", tercero.IdValor);
                PutTercero("nit", tercero.IdValor);
                PutTercero("documento", tercero.IdValor);
                PutTercero("idtipo", tercero.IdTipo.ToString());
                // Campos de las fichas dinamicas: los de columna ya estan puestos y ganan (TryAdd no pisa).
                AddFichasTokens(map, tercero.FichasJson);
            }
        }

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

    // Aplana los campos de las fichas dinamicas del tercero (jsonb: ficha -> campo -> valor) y los expone
    // como {directorio.<campo>} y {tercero.<campo>}. TryAdd: no pisa los tokens de columna ya puestos ni el
    // primer valor no vacio de un campo repetido entre fichas.
    private static void AddFichasTokens(IDictionary<string, string> map, string? fichasJson)
    {
        if (string.IsNullOrWhiteSpace(fichasJson)) { return; }
        try
        {
            using var doc = JsonDocument.Parse(fichasJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return; }
            foreach (var ficha in doc.RootElement.EnumerateObject())
            {
                if (ficha.Value.ValueKind != JsonValueKind.Object) { continue; }
                foreach (var campo in ficha.Value.EnumerateObject())
                {
                    var value = ReadFieldValue(campo.Value);
                    if (string.IsNullOrWhiteSpace(value)) { continue; }
                    var norm = NormalizeKey(campo.Name);
                    if (norm.Length == 0) { continue; }
                    map.TryAdd("directorio." + norm, value!);
                    map.TryAdd("tercero." + norm, value!);
                }
            }
        }
        catch (JsonException) { /* fichas corruptas: se ignoran */ }
    }

    // Normaliza una clave a los caracteres que admite el token {ns.clave} ([a-z0-9_]): sin tildes, en
    // minuscula, espacios/guiones -> '_', el resto se descarta. "Direccion" -> "direccion".
    private static string NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) { return string.Empty; }
        var normalized = key.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) { continue; }
            if (ch is ' ' or '-') { sb.Append('_'); }
            else if (char.IsLetterOrDigit(ch) && ch < 128) { sb.Append(char.ToLowerInvariant(ch)); }
            else if (ch == '_') { sb.Append('_'); }
        }
        return sb.ToString().Trim('_');
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
