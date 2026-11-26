using Ecorex.Application.Actividades;
using Ecorex.Application.Requerimientos;
using Ecorex.Application.Tenancy;

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
    /// <summary>Conceptos (subcategorias) vigentes del tenant. value = Guid, label = "CODIGO - Nombre".</summary>
    public const string ActividadSubcategorias = "actividad-subcategorias";

    /// <summary>Tipos de actividad vigentes. value = Guid, label = "Categoria - Nombre".</summary>
    public const string ActividadTipos = "actividad-tipos";

    /// <summary>Usuarios del tenant (TenantUser). value = Guid del TenantUser, label = nombre (o correo).</summary>
    public const string TenantUsuarios = "tenant-usuarios";

    private readonly IRequerimientoService _req;
    private readonly IRequerimientoFieldService _fields;
    private readonly IActividadCatalogoService? _catalogo;
    private readonly IActivityTypeService? _tipos;
    private readonly ITenantUserService? _usuarios;

    public RuleParamOptionsProvider(IRequerimientoService req, IRequerimientoFieldService fields,
        IActividadCatalogoService? catalogo = null, IActivityTypeService? tipos = null, ITenantUserService? usuarios = null)
    {
        _req = req; _fields = fields; _catalogo = catalogo; _tipos = tipos; _usuarios = usuarios;
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
            case ActividadSubcategorias when _catalogo is not null:
                {
                    var subs = await _catalogo.ListSubcategoriasAsync(categoriaId: null, includeArchived: false, ct);
                    return subs
                        .OrderBy(s => s.Codigo, StringComparer.OrdinalIgnoreCase)
                        .Select(s => new RuleParamOption(s.Id.ToString(), $"{s.Codigo} - {s.Nombre}"))
                        .ToList();
                }
            case ActividadTipos when _tipos is not null:
                {
                    var tipos = await _tipos.ListAsync(includeArchived: false, ct);
                    return tipos
                        .OrderBy(t => t.Category, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(t => new RuleParamOption(t.Id.ToString(),
                            string.IsNullOrWhiteSpace(t.Category) ? t.Name : $"{t.Category} - {t.Name}"))
                        .ToList();
                }
            case TenantUsuarios when _usuarios is not null:
                {
                    var users = await _usuarios.ListAsync(ct);
                    return users
                        .Select(u => new RuleParamOption(u.Id.ToString(),
                            string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : $"{u.DisplayName} ({u.Email})"))
                        .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            default:
                return Array.Empty<RuleParamOption>();
        }
    }
}
