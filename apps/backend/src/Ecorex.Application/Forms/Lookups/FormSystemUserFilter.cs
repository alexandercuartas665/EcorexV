using System.Text.Json;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Acotamiento del origen "Usuarios del sistema" (<see cref="Ecorex.Domain.Enums.FormSourceKind.SystemUser"/>):
/// lista blanca OPCIONAL de ids de TenantUser que el campo puede ofrecer. Se guarda serializado en
/// <see cref="Ecorex.Domain.Entities.FormQuestion.FilterJson"/> con forma <c>{"users":["guid",...]}</c>,
/// asi que NO requiere columna ni migracion nueva. Vacio o ausente = TODOS los usuarios del tenant
/// (comportamiento original, p. ej. el campo "Redireccionamiento"). El disenador arma la lista con
/// casillas (nunca escribe JSON) y el adaptador <see cref="SystemUserLookupSource"/> solo la aplica.
///
/// No colisiona con <see cref="FormLookupFilterSpec"/> (cascada de Contenedor): aquel solo corre para
/// DataContainer y esta forma {"users":[..]} no tiene "fixed"/"cascade" ni valores string al tope.
/// </summary>
public sealed record FormSystemUserFilter(IReadOnlyList<string> UserIds)
{
    public static readonly FormSystemUserFilter Empty = new(Array.Empty<string>());

    /// <summary>Hay lista blanca activa: el catalogo se acota a estos ids.</summary>
    public bool HasWhitelist => UserIds.Count > 0;

    /// <summary>Lee la lista blanca de un FilterJson. Defensivo: JSON invalido/vacio -> sin lista.</summary>
    public static FormSystemUserFilter Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return Empty; }
        var text = raw.TrimStart();
        if (text.Length == 0 || text[0] != '{') { return Empty; }
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("users", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
            {
                return Empty;
            }
            var ids = new List<string>();
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                {
                    var v = e.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) { ids.Add(v!.Trim()); }
                }
            }
            return ids.Count > 0 ? new FormSystemUserFilter(ids) : Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>Serializa a FilterJson, o null si no hay lista (para no guardar ruido).</summary>
    public string? ToJsonOrNull()
        => UserIds.Count == 0 ? null : JsonSerializer.Serialize(new { users = UserIds });

    /// <summary>Devuelve una copia con el id agregado o quitado (toggle), sin duplicados.</summary>
    public FormSystemUserFilter Toggle(string userId)
    {
        userId = (userId ?? string.Empty).Trim();
        if (userId.Length == 0) { return this; }
        var set = new List<string>(UserIds);
        var idx = set.FindIndex(x => string.Equals(x, userId, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) { set.RemoveAt(idx); } else { set.Add(userId); }
        return new FormSystemUserFilter(set);
    }
}
