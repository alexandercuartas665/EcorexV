namespace Ecorex.SuperAdmin.Services;

/// <summary>
/// Serializa el acceso al DbContext scoped del circuito desde TODOS los DynamicFormRenderer del
/// mismo circuito (carga inicial, reglas, autosave, lookup, subformularios y submit). Blazor Server
/// comparte UN DbContext por circuito; dos formularios que cargan a la vez -por ejemplo el del aside
/// del modal de Tercero y el de una pildora de concepto de la pestana Contacto Cliente- iniciaban una
/// "A second operation was started on this context instance" y tumbaban el circuito (se destapaba con
/// la BD remota por tunel, donde cada consulta tarda cientos de ms y la ventana de solape es amplia).
///
/// Antes cada renderer tenia su propio SemaphoreSlim, que solo serializaba SUS operaciones; no
/// impedia que DOS renderers distintos tocaran el DbContext a la vez. Al ser SCOPED hay una sola
/// instancia por circuito, compartida por todos los renderers, asi que el gate serializa entre
/// instancias. El renderer ya NO posee ni libera el semaforo (lo posee este servicio con el scope).
/// </summary>
public sealed class CircuitFormGate
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
}
