using Ecorex.SuperAdmin.Components.Shared;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Debouncer de los buscadores (TerceroPicker, ciudad, relaciones, lookups). En prod tumbaba el circuito con
/// ObjectDisposedException cuando una tecla (o el Dispose del componente) desechaba el CTS justo cuando el
/// disparo anterior ya habia terminado su espera. Estas pruebas martillan esa carrera y fijan el contrato:
/// nunca lanza, y solo corre la ultima accion.
/// </summary>
public class DebouncerTests
{
    [Fact]
    public async Task Teclas_Rapidas_Y_Dispose_Concurrente_Nunca_Lanzan()
    {
        for (var i = 0; i < 300; i++)
        {
            var d = new Debouncer();
            var t1 = Task.Run(() => d.RunAsync(1, () => Task.CompletedTask));
            var t2 = Task.Run(() => d.RunAsync(1, () => Task.CompletedTask));
            var t3 = Task.Run(async () => { await Task.Delay(1); d.Cancel(); d.Dispose(); });
            var t4 = Task.Run(() => d.RunAsync(0, () => Task.CompletedTask));
            await Task.WhenAll(t1, t2, t3, t4); // con el codigo viejo, alguna vuelta lanzaba ObjectDisposedException
        }
    }

    [Fact]
    public async Task Solo_Corre_La_Ultima_Accion()
    {
        var d = new Debouncer();
        var corridas = new List<string>();
        var a = d.RunAsync(200, () => { corridas.Add("a"); return Task.CompletedTask; });
        var b = d.RunAsync(20, () => { corridas.Add("b"); return Task.CompletedTask; });
        await Task.WhenAll(a, b);
        Assert.Equal(new[] { "b" }, corridas);
    }

    [Fact]
    public async Task Cancel_Descarta_El_Disparo_Pendiente_Y_Dispose_Es_Idempotente()
    {
        var d = new Debouncer();
        var corrio = false;
        var t = d.RunAsync(100, () => { corrio = true; return Task.CompletedTask; });
        d.Cancel();
        await t;
        Assert.False(corrio);
        d.Dispose();
        d.Dispose();
        d.Cancel();
    }
}
