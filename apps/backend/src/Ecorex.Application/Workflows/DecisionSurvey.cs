namespace Ecorex.Application.Workflows;

/// <summary>
/// Definicion de la ENCUESTA de un enlace de decision (Ola 3 plantillas de decision). Se arma del formulario
/// reportable elegido al configurar el enlace y se CONGELA (JSON) en el token, para que la pagina /d/{token}
/// renderice las preguntas sin depender del formulario en vivo. Las respuestas aterrizan como FormResponse
/// (DefinitionId = <see cref="FormId"/>, Reference = numero de la tarea) -> reportable.
/// </summary>
public sealed record DecisionSurvey(
    Guid FormId,
    string? Title,
    IReadOnlyList<DecisionSurveyQuestion> Questions);

/// <summary>Una pregunta de la encuesta. <see cref="Type"/> es el tipo de control SIMPLIFICADO que pinta la
/// pagina (text/textarea/select/radio/multicheck/toggle/number/date).</summary>
public sealed record DecisionSurveyQuestion(
    string Code,
    string Label,
    string Type,
    bool Required,
    IReadOnlyList<string>? Options = null);
