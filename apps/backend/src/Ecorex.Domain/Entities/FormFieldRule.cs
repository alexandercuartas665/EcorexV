using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Vinculo pregunta de formulario -> regla (port de FORX_DATA.EJECUTA_PARAM): al cambiar
/// el valor del campo en el renderer se ejecutan sus reglas en SortOrder y se aplican las
/// acciones de UI devueltas (ocultar/mostrar/set value/required). FK a la pregunta en
/// cascada; a la regla NO ACTION (el vinculo no arrastra la regla). Unico por
/// (FormQuestionId, RuleId). TENANT-SCOPED.
/// </summary>
public class FormFieldRule : TenantEntity
{
    public Guid FormQuestionId { get; set; }
    public FormQuestion? FormQuestion { get; set; }

    public Guid RuleId { get; set; }
    public Rule? Rule { get; set; }

    public int SortOrder { get; set; }
}
