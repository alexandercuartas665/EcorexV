using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Fuente de datos EXTERNA gobernada (ADR-0063), para que un reporte pueda leer datos EN VIVO de una
/// base de datos ajena (p.ej. la legacy db3dev) SIN llevar su propia cadena de conexion en el RDL.
///
/// Entidad de PLATAFORMA (hereda de <see cref="BaseEntity"/>, NO es <see cref="ITenantScoped"/>): el
/// catalogo lo administra UNICAMENTE PlatformAdmin (CRUD auditado). El acceso desde un tenant se
/// gobierna EXPLICITAMENTE por <see cref="ExternalDataSourceGrant"/>; no hay filtro por tenant sobre el
/// dato externo, por eso la concesion es obligatoria y el alcance viaja por parametros de contexto.
///
/// La cadena de conexion se guarda SIEMPRE cifrada (<see cref="ConnectionStringEncrypted"/>,
/// ISecretProtector/DataProtection); nunca en claro, ni en el repo, ni en el reporte, ni en logs. Se
/// descifra solo en memoria al ejecutar. Se exige un usuario de BD de SOLO LECTURA y el conector fuerza
/// la lectura ademas.
/// </summary>
public class ExternalDataSource : BaseEntity
{
    /// <summary>Nombre legible de la fuente (p.ej. "db3dev - Maravilla").</summary>
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>Motor de la base de datos externa. Acota el driver del conector.</summary>
    public ExternalDataProvider Provider { get; set; } = ExternalDataProvider.SqlServer;

    /// <summary>Cadena de conexion cifrada en reposo. NUNCA en claro ni versionada ni loggeada.</summary>
    public string? ConnectionStringEncrypted { get; set; }

    /// <summary>Marca de intencion: la fuente se usa en SOLO LECTURA. Siempre true (el conector ademas
    /// lo fuerza). Se conserva explicita para la auditoria y para dejar la regla visible.</summary>
    public bool IsReadOnly { get; set; } = true;

    /// <summary>Si esta habilitada para uso. Deshabilitada = registrada pero no ejecutable.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Ultima vez que la "prueba de conexion" de solo lectura fue exitosa.</summary>
    public DateTimeOffset? LastValidatedAt { get; set; }
}
