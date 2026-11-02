using System.Text.Json;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Opciones de un campo tipo Lista (Select) cuya lista DEPENDE del valor de otro campo (el "padre"):
/// p.ej. un "Subsector" cuyas opciones cambian segun el "Sector / Industria" elegido.
///
/// Se serializa a JSON y vive en <c>TerceroFieldDefinition.Options</c> (el MISMO sitio donde un Select
/// plano guarda su lista de texto; por eso NO hizo falta columna nueva). Se distingue de un Select plano
/// porque <c>Options</c> parsea a este JSON con <see cref="DependsOn"/> no vacio; un Select plano es texto
/// (una opcion por linea) y no parsea.
///
/// <para><see cref="DependsOn"/> = <c>"sector"</c> (el campo duro Sector) o la clave de otro campo Select
/// de la ficha general. <see cref="Groups"/> mapea cada valor del padre -> sus sub-opciones.</para>
/// </summary>
public sealed record DependentSelectOptions(
    string DependsOn,
    Dictionary<string, List<string>> Groups)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Parsea el <c>Options</c> de un campo; devuelve null si NO es una lista dependiente.</summary>
    public static DependentSelectOptions? TryParse(string? options)
    {
        if (string.IsNullOrWhiteSpace(options)) { return null; }
        var t = options.TrimStart();
        if (t.Length == 0 || t[0] != '{') { return null; }   // Select plano = texto por linea
        try
        {
            var dto = JsonSerializer.Deserialize<DependentSelectOptions>(options, JsonOpts);
            if (dto is null || string.IsNullOrWhiteSpace(dto.DependsOn)) { return null; }
            return new DependentSelectOptions(
                dto.DependsOn.Trim(),
                dto.Groups ?? new Dictionary<string, List<string>>(StringComparer.Ordinal));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Sub-opciones para un valor del padre (vacio si ese valor no tiene grupo).</summary>
    public IReadOnlyList<string> OptionsFor(string? parentValue)
    {
        if (string.IsNullOrEmpty(parentValue)) { return Array.Empty<string>(); }
        foreach (var kv in Groups)
        {
            if (string.Equals(kv.Key, parentValue, StringComparison.Ordinal))
            {
                return kv.Value ?? (IReadOnlyList<string>)Array.Empty<string>();
            }
        }
        return Array.Empty<string>();
    }
}
