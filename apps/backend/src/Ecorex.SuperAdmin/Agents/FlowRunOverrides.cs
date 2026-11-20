using Ecorex.Application.Automatizaciones.ConciliacionDian;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Gancho de MODULO para las corridas que dispara el motor generico (secuencia de tramos, ADR-0171): antes
/// de cada corrida, cada proveedor puede agregar valores de corrida (overrides) que solo su modulo sabe
/// calcular. El motor no conoce a ningun modulo (ADR-0169: los CUFE ya cargados son conocimiento del
/// modulo DIAN). Se resuelve por scope con el tenant ya fijado.
/// </summary>
public interface IFlowRunOverrideProvider
{
    /// <summary>Agrega (o ajusta) overrides para una corrida de <paramref name="flowId"/>. Recibe los ya
    /// calculados (p.ej. DIAS_DESDE/DIAS_ATRAS del tramo). No hace nada si el flujo no es de su modulo.</summary>
    Task ContributeAsync(Guid flowId, Dictionary<string, string> overrides, CancellationToken ct = default);
}

/// <summary>
/// Proveedor del modulo Conciliacion DIAN: si el flujo es uno de los configurados como extraccion del
/// conciliador, le inyecta CUFES_CONOCIDOS para el rango del tramo (mismo calculo que el boton "Importar del
/// bot"), asi la corrida programada se salta lo que ya esta cargado y baja solo lo nuevo.
/// </summary>
public sealed class ConciliacionDianFlowOverrides(
    IAutorizacionComprasConfigService cfg,
    IConciliacionDianService svc) : IFlowRunOverrideProvider
{
    /// <summary>Variable con la que el dron recibe los CUFE ya cargados (prefijos de 12 hex, por coma).</summary>
    public const string VarCufesConocidos = "CUFES_CONOCIDOS";

    public async Task ContributeAsync(Guid flowId, Dictionary<string, string> overrides, CancellationToken ct = default)
    {
        if (overrides.ContainsKey(VarCufesConocidos)) { return; } // alguien ya lo decidio (p.ej. "volver a bajar")
        var eventos = await cfg.GetExtraccionAsync(ct);
        if (!eventos.Any(e => e.FlowId == flowId)) { return; }

        static int Num(Dictionary<string, string> m, string k, int def)
            => m.TryGetValue(k, out var s) && int.TryParse(s, out var n) && n >= 0 ? n : def;
        overrides[VarCufesConocidos] = await svc.CufesConocidosAsync(
            Num(overrides, "DIAS_DESDE", 0), Num(overrides, "DIAS_ATRAS", 180), ct);
    }
}
