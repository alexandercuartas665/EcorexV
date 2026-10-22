using Ecorex.Application.Requerimientos;

namespace Ecorex.Application.Rules;

/// <summary>
/// Resuelve las opciones de un parametro de regla tipo <see cref="RuleParamType.Select"/> cuyo
/// <see cref="RuleVerbParamDescriptor.OptionsSource"/> apunta a un origen dinamico (datos del tenant).
/// Asi el editor de reglas puede ofrecer listas (p. ej. los Origenes de Requerimientos) sin que el verbo
/// conozca la BD. Extensible: cada clave de origen se resuelve aqui.
/// </summary>
public interface IRuleParamOptionsProvider
{
    /// <summary>Opciones para un origen (clave estable). Lista vacia si la clave no se reconoce.</summary>
    Task<IReadOnlyList<RuleParamOption>> GetOptionsAsync(string source, CancellationToken ct = default);
}

public sealed class RuleParamOptionsProvider : IRuleParamOptionsProvider
{
    private readonly IRequerimientoService _req;
    private readonly IRequerimientoFieldService _fields;

    public RuleParamOptionsProvider(IRequerimientoService req, IRequerimientoFieldService fields)
    {
        _req = req; _fields = fields;
    }

    public async Task<IReadOnlyList<RuleParamOption>> GetOptionsAsync(string source, CancellationToken ct = default)
    {
        switch ((source ?? string.Empty).Trim())
        {
            // Origenes de Requerimientos (definen el proceso de compra/cotizacion). value = Guid, label = nombre.
            case "requerimiento-origenes":
                {
                    var origenes = await _req.ListOrigenesActivosAsync(ct);
                    return origenes.Select(o => new RuleParamOption(o.Id.ToString(), o.Nombre)).ToList();
                }
            // Campos propios de la NECESIDAD (ambito Padre) como destinos de mapeo. value = "dyn:{fieldKey}".
            case "requerimiento-campos-padre":
                {
                    var defs = await _fields.ListDefsAsync("Padre", ct);
                    return defs.Select(d => new RuleParamOption("dyn:" + d.FieldKey, d.Label + " (campo propio)")).ToList();
                }
            default:
                return Array.Empty<RuleParamOption>();
        }
    }
}
