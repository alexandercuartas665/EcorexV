namespace Ecorex.Domain.Enums;

/// <summary>
/// Etapa del embudo de una oportunidad de negocio (modulo Gestor de Clientes 000740).
/// El orden es el del pipeline; Ganada y Perdida son estados terminales (oportunidad cerrada).
/// </summary>
public enum OportunidadEtapa
{
    Nueva = 0,
    Calificada = 1,
    Propuesta = 2,
    Negociacion = 3,
    Ganada = 4,
    Perdida = 5
}
