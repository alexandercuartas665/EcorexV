namespace Ecorex.Domain.Enums;

/// <summary>
/// Area funcional a la que pertenece un modulo del catalogo (module registry, legacy 000109).
/// Agrupa el catalogo en la UI y servira para derivar el menu por grupos.
/// </summary>
public enum ModuleArea
{
    /// <summary>Nucleo del producto: actividades, proyectos, tableros.</summary>
    Principal,
    /// <summary>Operacion diaria: administrar/programar actividades.</summary>
    Operaciones,
    /// <summary>Motores de configuracion: flujos, formularios, reglas.</summary>
    Automatizacion,
    /// <summary>Modulos de sistema: dependencias, modulos web, metricas.</summary>
    Sistema,
    /// <summary>Modulos comerciales heredados del backbone (pipeline, chat).</summary>
    Crm
}
