namespace Ecorex.Domain.Enums;

/// <summary>Modo de trabajo del modulo de Requerimientos/Ordenes de compra (config del tenant).
/// Consolidado = junta las necesidades del MISMO producto en una sola linea sumada (se planifican como
/// grupo); Dividido = cada necesidad se ve y se planifica por separado. Un registro puede salir de la
/// consolidacion con el flag ForzarDividir aunque el modulo este en modo Consolidado.</summary>
public enum RequerimientoModo
{
    Consolidado = 0,
    Dividido = 1,
}
