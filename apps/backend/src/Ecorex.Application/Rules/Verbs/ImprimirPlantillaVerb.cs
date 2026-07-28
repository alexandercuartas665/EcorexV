namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo IMPRIMIR_PLANTILLA: declara la INTENCION de imprimir una plantilla del modulo
/// Plantillas con los datos del registro del formulario. No genera el documento aqui (un
/// verbo no tiene acceso al render HTTP): devuelve una accion PrintTemplate que el
/// DynamicFormRenderer detecta al hacer clic en el boton y ejecuta (abre imprimir/PDF/imagen).
///
/// LIMITACION conocida: el motor de reglas no tiene tipo de parametro "opciones/select"
/// (ver RuleParamType: Text, Number, Boolean, FieldCode, Json). Por eso la plantilla se
/// referencia por NOMBRE (param Text) y el renderer la resuelve contra IQuoteTemplateService.
/// </summary>
public sealed class ImprimirPlantillaVerb : IRuleVerb
{
    public const string VerbName = "IMPRIMIR_PLANTILLA";

    public string Name => VerbName;

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Imprimir plantilla",
        "Genera el documento de una plantilla del modulo Plantillas con los datos del registro. "
        + "El boton que dispara esta regla abre el resultado (imprimir / PDF / imagen).",
        [
            new RuleVerbParamDescriptor("template", "Plantilla (nombre)", RuleParamType.Text, Required: true,
                "NOMBRE EXACTO de la plantilla del modulo Plantillas. El motor de reglas no tiene "
                + "selector de opciones: la plantilla se resuelve por nombre al imprimir."),
            new RuleVerbParamDescriptor("format", "Formato", RuleParamType.Text, Required: false,
                "print, pdf o img. Si se omite, el boton muestra el dialogo para elegir el formato.")
        ]);

    public Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        var template = context.GetStringParam("template")?.Trim();
        if (string.IsNullOrWhiteSpace(template))
        {
            return Task.FromResult(RuleVerbResult.Fail("Parametro 'template' obligatorio (nombre de la plantilla)."));
        }

        var format = context.GetStringParam("format")?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(format) && format is not ("print" or "pdf" or "img"))
        {
            return Task.FromResult(RuleVerbResult.Fail("Parametro 'format' invalido (usa print, pdf o img)."));
        }

        var message = string.IsNullOrEmpty(format)
            ? $"Imprimir plantilla '{template}' (formato a elegir)."
            : $"Imprimir plantilla '{template}' ({format}).";
        return Task.FromResult(RuleVerbResult.Ok(
            message,
            actions: [RuleAction.PrintTemplate(template, string.IsNullOrEmpty(format) ? null : format)]));
    }
}
