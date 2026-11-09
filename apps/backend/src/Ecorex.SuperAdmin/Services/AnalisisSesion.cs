using System.Security.Claims;
using Ecorex.Application.Analisis;
using Ecorex.SuperAdmin.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace Ecorex.SuperAdmin.Services;

/// <summary>
/// Estado del modulo de Analisis por CIRCUITO (scoped, ADR-0158 D9): resuelve una vez quien opera
/// (usuario + si es Admin/Owner) para la pagina /analisis y para los componentes embebidos, y expone la
/// compuerta unica de consultas del circuito (<see cref="CircuitFormGate"/>, la misma de los formularios
/// dinamicos): todo widget, vista previa o formulario del circuito toca el DbContext de a uno.
/// </summary>
public sealed class AnalisisSesion
{
    private readonly AuthenticationStateProvider _auth;
    private readonly ICurrentPermissions _perms;
    private AnalisisActor? _actor;

    public AnalisisSesion(AuthenticationStateProvider auth, ICurrentPermissions perms, CircuitFormGate gate)
    {
        _auth = auth;
        _perms = perms;
        Gate = gate.Gate;
    }

    public SemaphoreSlim Gate { get; }

    public async Task<AnalisisActor> ActorAsync()
    {
        if (_actor is not null)
        {
            return _actor;
        }

        var state = await _auth.GetAuthenticationStateAsync();
        Guid? userId = Guid.TryParse(state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var u) ? u : null;
        _actor = new AnalisisActor(userId, await _perms.IsUnrestrictedAsync());
        return _actor;
    }

    /// <summary>Ejecuta una consulta del circuito pasando por la compuerta.</summary>
    public async Task<T> EnCompuertaAsync<T>(Func<Task<T>> consulta, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            return await consulta();
        }
        finally
        {
            Gate.Release();
        }
    }
}
