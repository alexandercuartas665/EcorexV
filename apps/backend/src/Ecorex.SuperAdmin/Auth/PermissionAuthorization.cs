using System.Globalization;
using Ecorex.Application.Roles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Ecorex.SuperAdmin.Auth;

/// <summary>
/// Convencion de nombres de las policies dinamicas de permiso (Ola B2, ADR-0033):
/// <c>Perm:{moduleKey}:{action}</c> con action in View/Create/Edit/Delete. Ej.:
/// <c>Perm:inventario-items:View</c>. El <see cref="PermissionPolicyProvider"/> las materializa al
/// vuelo; el resto de policies (Inventario.Ver, AdmUsuarios.Editar, ...) siguen intactas.
///
/// <para>Ola 7 (endurecimiento) - POLICIES COMPUESTAS POR VISTA: una vista puede exigir VARIOS
/// permisos combinados con AND, uniendo segmentos con '+':
/// <c>Perm:{m1}:{a1}+{m2}:{a2}</c> (ej. la vista de alta de actividad exige VER y CREAR:
/// <c>Perm:actividades:View+actividades:Create</c>). Cada segmento se materializa como un
/// <see cref="PermissionRequirement"/> independiente; ASP.NET Core exige que TODOS los requisitos
/// de la policy se cumplan, de modo que varios requisitos == AND. Esto replica el "multi-permiso"
/// del legacy (una opcion de menu que dependia de la conjuncion de varios permisos).</para>
/// </summary>
public static class PermissionPolicy
{
    public const string Prefix = "Perm:";

    /// <summary>Separador de segmentos en una policy compuesta (AND). Ej. <c>a:View+b:Edit</c>.</summary>
    public const char AndSeparator = '+';

    /// <summary>Construye el nombre de policy para un modulo y accion.</summary>
    public static string For(string moduleKey, PermissionAction action)
        => $"{Prefix}{moduleKey}:{action}";

    /// <summary>
    /// Construye el nombre de una policy COMPUESTA (AND) a partir de varios (modulo, accion).
    /// Ej. <c>ForAll(("actividades",View),("actividades",Create))</c> = <c>Perm:actividades:View+actividades:Create</c>.
    /// </summary>
    public static string ForAll(params (string moduleKey, PermissionAction action)[] parts)
        => Prefix + string.Join(AndSeparator, parts.Select(p => $"{p.moduleKey}:{p.action}"));

    /// <summary>Intenta parsear un nombre <c>Perm:{module}:{action}</c> simple. false si no aplica el prefijo o si es compuesto.</summary>
    public static bool TryParse(string policyName, out string moduleKey, out PermissionAction action)
    {
        moduleKey = string.Empty;
        action = default;
        if (!TryParseMany(policyName, out var parts) || parts.Count != 1)
        {
            return false;
        }

        moduleKey = parts[0].moduleKey;
        action = parts[0].action;
        return true;
    }

    /// <summary>
    /// Parsea un nombre <c>Perm:{m1}:{a1}[+{m2}:{a2}...]</c> en uno o mas segmentos (modulo, accion).
    /// Devuelve false si falta el prefijo o si algun segmento es invalido (todos deben parsear).
    /// </summary>
    public static bool TryParseMany(
        string policyName, out IReadOnlyList<(string moduleKey, PermissionAction action)> parts)
    {
        parts = Array.Empty<(string, PermissionAction)>();
        if (string.IsNullOrEmpty(policyName) || !policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = policyName[Prefix.Length..];
        var segments = body.Split(AndSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var acc = new List<(string moduleKey, PermissionAction action)>(segments.Length);
        foreach (var segment in segments)
        {
            // La accion es el ultimo segmento tras el ultimo ':'; el modulo es todo lo anterior (un
            // Route podria, en teoria, contener ':', por eso partimos por la ULTIMA aparicion).
            var sep = segment.LastIndexOf(':');
            if (sep <= 0 || sep == segment.Length - 1)
            {
                return false;
            }

            var modulePart = segment[..sep];
            var actionPart = segment[(sep + 1)..];
            if (!Enum.TryParse(actionPart, ignoreCase: false, out PermissionAction parsed))
            {
                return false;
            }

            acc.Add((modulePart, parsed));
        }

        parts = acc;
        return true;
    }
}

/// <summary>Requisito de autorizacion por permiso de un modulo (Modulo x Accion).</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string moduleKey, PermissionAction action)
    {
        ModuleKey = moduleKey;
        Action = action;
    }

    public string ModuleKey { get; }
    public PermissionAction Action { get; }
}

/// <summary>
/// Handler del <see cref="PermissionRequirement"/>: concede si el usuario es Unrestricted (Owner/
/// Admin o sin rol) o si su matriz permite (ModuleKey, Accion). Consulta <see cref="ICurrentPermissions"/>
/// (que ya aplica la regla opt-in y es fail-open). El gate de tenant se combina en la policy con
/// RequireClaim("tenant_id").
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ICurrentPermissions _permissions;

    public PermissionAuthorizationHandler(ICurrentPermissions permissions)
    {
        _permissions = permissions;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var eff = await _permissions.GetAsync();
        if (eff.Unrestricted || eff.Can(requirement.ModuleKey, requirement.Action))
        {
            context.Succeed(requirement);
        }
        // Si no concede, no llamamos Fail(): dejamos que otros handlers/requisitos decidan; la
        // ausencia de Succeed ya niega la policy.
    }
}

/// <summary>
/// Provider de policies que materializa al vuelo las policies con prefijo <c>Perm:</c> (una policy
/// = RequireClaim("tenant_id") + <see cref="PermissionRequirement"/>). Para cualquier otro nombre
/// delega en el <see cref="DefaultAuthorizationPolicyProvider"/>, de modo que TODAS las policies
/// existentes (Inventario.Ver, TenantMember, PlatformOperator, ...) siguen funcionando igual.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Ola 7: soporta policies simples y COMPUESTAS (AND). TryParseMany devuelve 1+ segmentos;
        // se añade un PermissionRequirement por segmento y ASP.NET exige que TODOS se cumplan (AND).
        if (PermissionPolicy.TryParseMany(policyName, out var parts))
        {
            var builder = new AuthorizationPolicyBuilder()
                // Mantiene el gate de tenant: exige usuario de un tenant (igual que TenantMember).
                .RequireClaim("tenant_id");
            foreach (var (moduleKey, action) in parts)
            {
                builder.AddRequirements(new PermissionRequirement(moduleKey, action));
            }

            return Task.FromResult<AuthorizationPolicy?>(builder.Build());
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}
