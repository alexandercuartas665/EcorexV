using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Vinculo DEFINICION de formulario -> regla, disparado al ENVIAR el formulario (on-submit). A diferencia
/// de <see cref="FormFieldRule"/> (que se dispara por CAMBIO DE CAMPO y liga a una pregunta), este liga a la
/// definicion completa y sus reglas corren una vez, en SortOrder, cuando el FormResponse pasa a Submitted
/// (incluida la ruta publica anonima /f/{token}, server-side). Uso tipico: crear una actividad al recibir un
/// lead. FK a la definicion en cascada; a la regla NO ACTION. Unico por (DefinitionId, RuleId). TENANT-SCOPED.
/// </summary>
public class FormSubmitRule : TenantEntity
{
    public Guid DefinitionId { get; set; }
    public FormDefinition? Definition { get; set; }

    public Guid RuleId { get; set; }
    public Rule? Rule { get; set; }

    public int SortOrder { get; set; }
}
