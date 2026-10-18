using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Una regla del motor de conciliacion DIAN (configurable, sin codigo). Las reglas de un tenant corren EN ORDEN
/// (SortOrder) sobre cada renglon: una BUSQUEDA (VLOOKUP contra un contenedor de datos) escribe un campo, y una
/// CONDICION (SI/entonces) asigna campos cuando se cumplen condiciones Y/O. Una regla puede reusar el campo que
/// calculo la anterior (encadenamiento). La definicion va como JSON para evolucionar sin migraciones.
/// Clave logica: (TenantId, Name). Un unico juego por tenant (aplica a todos los documentos CCD).
/// </summary>
public sealed class ConciliacionRule : TenantEntity
{
    /// <summary>Orden de ejecucion (ascendente). Las reglas se aplican una tras otra en este orden.</summary>
    public int SortOrder { get; set; }

    /// <summary>Nombre visible de la regla.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tipo: "Lookup" (VLOOKUP) o "Condition" (SI/entonces).</summary>
    public string Kind { get; set; } = "Lookup";

    /// <summary>Si esta activa (las inactivas se saltan en la corrida).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Especificacion de la regla (LookupSpec o ConditionSpec) serializada. JSON.</summary>
    public string DefinitionJson { get; set; } = "{}";
}
