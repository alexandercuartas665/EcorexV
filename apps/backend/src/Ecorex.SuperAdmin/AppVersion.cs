namespace Ecorex.SuperAdmin;

/// <summary>
/// Version del sistema ECOREX.tareas. Fuente unica, se sube A MANO en cada release
/// (semantica: MAJOR.MINOR.PATCH). Se muestra en el footer del login y en el topbar
/// del shell; sirve ademas para confirmar que un deploy aterrizo (el numero cambia).
/// </summary>
public static class AppVersion
{
    /// <summary>Numero semantico actual (sin la 'v').</summary>
    public const string Current = "0.15.128";

    /// <summary>Como se muestra en la UI (ej. "v0.1.0").</summary>
    public static string Display => "v" + Current;
}
