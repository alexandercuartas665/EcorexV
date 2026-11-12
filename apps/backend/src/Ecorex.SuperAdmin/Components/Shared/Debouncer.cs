namespace Ecorex.SuperAdmin.Components.Shared;

/// <summary>
/// Ayudante de "debounce" para handlers async de autocompletar/busqueda en Blazor Server. Reinicia el
/// temporizador en cada tecla y ejecuta la accion solo tras 'ms' de inactividad; si llega otra tecla
/// antes, el disparo previo se descarta. Reduce el spam de consultas a BD por tecla.
///
/// Uso en un componente:
///   private readonly Debouncer _debounce = new();
///   // en el handler de la tecla: el texto se asigna YA (para que el input conserve lo tecleado) y
///   // solo la consulta pesada se difiere:
///   _texto = e.Value?.ToString() ?? "";
///   await _debounce.RunAsync(250, () => BuscarAsync(_texto));
///   // al elegir una opcion o al perder el foco: cancelar el disparo pendiente para que el dropdown
///   // no "reviva" despues de haber elegido:
///   _debounce.Cancel();
///
/// Nota: la accion corre tras un await; como el handler que la invoca se ejecuta en el contexto del
/// circuito de Blazor, tocar estado del componente y StateHasChanged() dentro de la accion es seguro.
/// Recuerda Dispose() del Debouncer en el Dispose() del componente.
/// </summary>
public sealed class Debouncer : IDisposable
{
    private CancellationTokenSource? _cts;

    public async Task RunAsync(int ms, Func<Task> action)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = _cts = new CancellationTokenSource();
        try
        {
            await Task.Delay(ms, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;   // llego otra tecla (o Cancel()) antes del plazo: este disparo se descarta
        }
        if (cts.Token.IsCancellationRequested) { return; }
        await action();
    }

    /// <summary>Cancela el disparo pendiente (al elegir del dropdown, al blur, al limpiar).</summary>
    public void Cancel()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
