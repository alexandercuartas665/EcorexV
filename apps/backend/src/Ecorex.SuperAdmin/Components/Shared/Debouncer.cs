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

    // El TOKEN se captura al crear el CTS y es lo unico que se lee despues del await. Antes se leia
    // cts.Token tras el Delay: si otra tecla (o el Dispose del componente) desechaba ese CTS justo cuando
    // el Delay ya habia terminado, cts.Token lanzaba ObjectDisposedException y, sin capturar, tumbaba el
    // circuito (visto en prod en TerceroPicker del asistente de tareas). CancellationToken.IsCancellationRequested
    // y Task.Delay(ms, token) no lanzan aunque el CTS ya este desechado.
    public async Task RunAsync(int ms, Func<Task> action)
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var previous = Interlocked.Exchange(ref _cts, cts);
        CancelAndDispose(previous);
        try
        {
            await Task.Delay(ms, token);
        }
        catch (OperationCanceledException)
        {
            return;   // llego otra tecla (o Cancel()) antes del plazo: este disparo se descarta
        }
        if (token.IsCancellationRequested) { return; }
        await action();
    }

    /// <summary>Cancela el disparo pendiente (al elegir del dropdown, al blur, al limpiar).</summary>
    public void Cancel()
    {
        try { Volatile.Read(ref _cts)?.Cancel(); }
        catch (ObjectDisposedException) { /* ya desechado por otra tecla o por Dispose: nada que cancelar */ }
    }

    public void Dispose() => CancelAndDispose(Interlocked.Exchange(ref _cts, null));

    private static void CancelAndDispose(CancellationTokenSource? cts)
    {
        if (cts is null) { return; }
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }
        cts.Dispose();
    }
}
