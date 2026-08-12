namespace Ecorex.Domain.Enums;

/// <summary>
/// Cada cuanto se debe ejecutar una busqueda de contactos configurada
/// (<see cref="Ecorex.Domain.Entities.ContactSearchDefinition"/>). Por ahora solo se GUARDA la
/// frecuencia (y la ultima corrida); la ejecucion automatica en segundo plano queda para una ola
/// siguiente. Manual = solo cuando el usuario pulsa "Ejecutar".
/// </summary>
public enum ContactSearchSchedule
{
    /// <summary>Nunca corre sola: solo bajo demanda.</summary>
    Manual = 0,
    /// <summary>Una vez al dia.</summary>
    Diaria,
    /// <summary>Una vez por semana.</summary>
    Semanal,
    /// <summary>Una vez al mes.</summary>
    Mensual
}
