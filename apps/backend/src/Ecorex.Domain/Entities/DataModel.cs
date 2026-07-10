using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Contenedor de datos (nivel superior): un MODELO que agrupa VARIAS tablas
/// (<see cref="DataContainer"/>) y las relaciones entre ellas, mas su configuracion de importacion
/// (conectores, destino, clientes, procesos). Corresponde a un origen (ej. un JSON de un web service)
/// que trae varias estructuras/matrices; cada estructura es una tabla del contenedor. TENANT-SCOPED.
/// El nombre es unico por tenant. Se borra en cascada (tablas, conectores, destino, procesos).
/// </summary>
public class DataModel : TenantEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Tablas que componen el contenedor (nivel raiz; las sub-tablas cuelgan de estas).</summary>
    public ICollection<DataContainer> Tables { get; set; } = new List<DataContainer>();
}

/// <summary>
/// Destino de los datos importados de un contenedor (1:1 con <see cref="DataModel"/>). Define DONDE
/// el cliente/motor deja la data: dentro del sistema (tablas EAV) o en una base de datos aliada
/// (motor + host + credenciales CIFRADAS). Solo CONFIGURACION en esta fase. TENANT-SCOPED.
/// </summary>
public class DataDestination : TenantEntity
{
    public Guid ModelId { get; set; }
    public DataModel? Model { get; set; }

    public DestinationKind Kind { get; set; } = DestinationKind.System;

    // ---- Solo para Kind == AlliedDatabase ----
    public DbEngine? DbEngine { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? DatabaseName { get; set; }
    public string? Username { get; set; }
    /// <summary>Contrasena/secreto de la BD aliada, cifrado con ISecretProtector. NUNCA en claro.</summary>
    public string? CredentialsEncrypted { get; set; }
}
