using System.Reflection;
using Ecorex.Application.Actividades;
using Ecorex.Application.Requerimientos;
using Ecorex.Application.Rules;
using Ecorex.Application.Rules.Verbs;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// CREAR_TAREA sin GUIDs a mano: concepto, tipo de actividad y asignado son listas (Select + OptionsSource) que
/// resuelve RuleParamOptionsProvider. El valor guardado sigue siendo el Guid (retro-compatible).
/// </summary>
public class RuleParamOptionsCrearTareaTests
{
    /// <summary>Doble minimo: cada metodo devuelve Task.FromResult del valor configurado por nombre.</summary>
    public class Fake<T> : DispatchProxy where T : class
    {
        private Dictionary<string, object?> _results = new();

        public static T Create(Dictionary<string, object?> results)
        {
            var proxy = Create<T, Fake<T>>();
            ((Fake<T>)(object)proxy)._results = results;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            if (!_results.TryGetValue(name, out var value))
            {
                throw new NotSupportedException(name);
            }
            var ret = targetMethod.ReturnType;
            if (ret.IsGenericType && ret.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var fromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(ret.GetGenericArguments()[0]);
                return fromResult.Invoke(null, new[] { value });
            }
            return value;
        }
    }

    private static readonly Guid SubId = Guid.NewGuid();
    private static readonly Guid TipoId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static RuleParamOptionsProvider Provider()
    {
        var catalogo = Fake<IActividadCatalogoService>.Create(new()
        {
            ["ListSubcategoriasAsync"] = (IReadOnlyList<ActividadSubcategoriaDto>)new[]
            {
                CrearSub(SubId, "CAT-01-05", "Captacion de lead"),
            }
        });
        var tipos = Fake<IActivityTypeService>.Create(new()
        {
            ["ListAsync"] = (IReadOnlyList<ActivityTypeDto>)new[]
            {
                new ActivityTypeDto(TipoId, "Comercial", "Visita", null, 0, false, null, false)
            }
        });
        var usuarios = Fake<ITenantUserService>.Create(new()
        {
            ["ListAsync"] = (IReadOnlyList<TenantUserDto>)new[]
            {
                new TenantUserDto(UserId, Guid.NewGuid(), "andres@soldarco.test", TenantRole.Advisor, PlatformUserStatus.Active, DisplayName: "Andres"),
            }
        });
        return new RuleParamOptionsProvider(
            Fake<IRequerimientoService>.Create(new()), Fake<IRequerimientoFieldService>.Create(new()),
            catalogo, tipos, usuarios);
    }

    private static ActividadSubcategoriaDto CrearSub(Guid id, string codigo, string nombre)
    {
        // El DTO tiene varios campos; se arma por reflexion con defaults para no acoplar la prueba a su forma.
        var ctor = typeof(ActividadSubcategoriaDto).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var args = ctor.GetParameters().Select(p => p.Name switch
        {
            "Id" => id,
            "Codigo" => codigo,
            "Nombre" => nombre,
            _ => p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
        }).ToArray();
        return (ActividadSubcategoriaDto)ctor.Invoke(args);
    }

    [Fact]
    public async Task Proveedor_ResuelveLasTresListas_ConElGuidComoValor()
    {
        var p = Provider();

        var subs = await p.GetOptionsAsync(RuleParamOptionsProvider.ActividadSubcategorias);
        Assert.Equal(new RuleParamOption(SubId.ToString(), "CAT-01-05 - Captacion de lead"), Assert.Single(subs));

        var tipos = await p.GetOptionsAsync(RuleParamOptionsProvider.ActividadTipos);
        Assert.Equal(new RuleParamOption(TipoId.ToString(), "Comercial - Visita"), Assert.Single(tipos));

        var users = await p.GetOptionsAsync(RuleParamOptionsProvider.TenantUsuarios);
        Assert.Equal(new RuleParamOption(UserId.ToString(), "Andres (andres@soldarco.test)"), Assert.Single(users));

        Assert.Empty(await p.GetOptionsAsync("no-existe"));
    }

    [Fact]
    public void CrearTarea_ConceptoTipoYAsignado_SonListas()
    {
        var d = new CrearTareaVerb(null!).Descriptor.Params.ToDictionary(x => x.Name);
        Assert.Equal((RuleParamType.Select, RuleParamOptionsProvider.ActividadSubcategorias), (d["subcategoriaId"].Type, d["subcategoriaId"].OptionsSource));
        Assert.True(d["subcategoriaId"].Required);
        Assert.Equal((RuleParamType.Select, RuleParamOptionsProvider.ActividadTipos), (d["activityTypeId"].Type, d["activityTypeId"].OptionsSource));
        Assert.Equal((RuleParamType.Select, RuleParamOptionsProvider.TenantUsuarios), (d["assigneeUserId"].Type, d["assigneeUserId"].OptionsSource));
        // Las plantillas siguen siendo texto (llevan tokens {campo}).
        Assert.Equal(RuleParamType.Text, d["titleTemplate"].Type);
    }
}
