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
/// </summary>
public static class PermissionPolicy
{
    public const string Prefix = "Perm:";

    /// <summary>Construye el nombre de policy para un modulo y accion.</summary>
    public static string For(string moduleKey, PermissionAction action)
        => $"{Prefix}{moduleKey}:{action}";

    /// <summary>Intenta parsear un nombre <c>Perm:{module}:{action}</c>. false si no aplica el prefijo.</summary>
    public static bool TryParse(string policyName, out string moduleKey, out PermissionAction action)
    {
        moduleKey = string.Empty;
        action = default;
        if (string.IsNullOrEmpty(policyName) || !policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = policyName[Prefix.Length..];
        // La accion es el ultimo segmento tras el ultimo ':'; el modulo es todo lo anterior (un
        // Route podria, en teoria, contener ':', por eso partimos por la ULTIMA aparicion).
        var sep = body.LastIndexOf(':');
        if (sep <= 0 || sep == body.Length - 1)
        {
            return false;
        }

        var modulePart = body[..sep];
        var actionPart = body[(sep + 1)..];
        if (!Enum.TryParse(actionPart, ignoreCase: false, out PermissionAction parsed))
        {
            return false;
        }

        moduleKey = modulePart;
        action = parsed;
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
        if (PermissionPolicy.TryParse(policyName, out var moduleKey, out var action))
        {
            var policy = new AuthorizationPolicyBuilder()
                // Mantiene el gate de tenant: exige usuario de un tenant (igual que TenantMember).
                .RequireClaim("tenant_id")
                .AddRequirements(new PermissionRequirement(moduleKey, action))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}
