namespace Ecorex.Domain.Enums;

/// <summary>
/// Motor de Directorio que creo/gestiona un <see cref="Entities.Tercero"/> (Capa 8, 2do motor de
/// contactos). Ambos motores viven en la MISMA tabla Tercero; esta marca dice cual lo genero, para
/// que cada lector (motor Clasico, motor Modular, Gestor de contactos 000740) sepa como interpretar
/// su ficha. Los registros existentes quedan como <see cref="Clasico"/> por defecto.
/// </summary>
public enum DirectoryEngine
{
    /// <summary>Motor actual (fichas por perfil + FichasJson). Valor por defecto de todo lo existente.</summary>
    Clasico,

    /// <summary>Motor avanzado "Directorio Modular" (secciones + categorias componibles).</summary>
    Modular
}
