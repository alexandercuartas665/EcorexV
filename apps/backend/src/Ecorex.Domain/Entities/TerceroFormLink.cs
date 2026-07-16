using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Formulario dinamico OFRECIDO en el modal de tercero (Directorio General 000232 y Cargador de
/// contactos 000740). Entidad de CONFIGURACION tenant-scoped: desde "Configurar campos" el tenant
/// elige QUE formularios se pueden llenar por tercero (varios elegibles; el usuario escoge cual
/// llenar en la tercera columna del modal).
///
/// Las RESPUESTAS no viven aqui: se guardan en <see cref="FormResponse"/> ancladas por
/// <see cref="FormResponse.Reference"/> = "TERCERO:{terceroId}", el mismo patron que ya usa el
/// arranque form-first (que ancla por el numero de la tarea). Asi una respuesta por (formulario,
/// tercero) queda cubierta por el indice existente (TenantId, DefinitionId, Reference).
/// Multi-tenant (filtro global por reflexion).
/// </summary>
public class TerceroFormLink : TenantEntity
{
    /// <summary>Formulario ofrecido en el modal de tercero.</summary>
    public Guid FormDefinitionId { get; set; }
    public FormDefinition? FormDefinition { get; set; }

    /// <summary>Orden en el selector de formularios del modal.</summary>
    public int SortOrder { get; set; }
}
