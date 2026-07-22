using System.Text;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Convierte el contexto estructurado de la ola 1 en el texto que lee el modelo.
///
/// Se serializa a MARKDOWN plano y no a JSON crudo porque el destinatario es un LLM: encabezados y
/// listas cortas se leen mejor que un arbol de llaves, y cuestan menos tokens que el mismo dato
/// envuelto en comillas y comas. El orden es el mismo del DTO (paso, datos previos, caso,
/// historial), que es el orden en que una persona necesitaria la informacion.
///
/// Cada recorte de la ola 1 se ANUNCIA en el texto: el modelo debe saber que esta viendo una
/// ventana del caso y no el caso entero, para que no concluya sobre lo que no ve.
/// </summary>
public static class WorkflowAgentContextSerializer
{
    public static string ToText(WorkflowAgentContextDto context)
    {
        var sb = new StringBuilder();

        // (a) El paso a atender y el formulario que debe quedar resuelto.
        sb.AppendLine("# Paso a atender");
        sb.AppendLine($"- Nodo: {context.Node.Name ?? context.Node.BpmnElementId}");
        if (context.Node.StepNumber is int step) { sb.AppendLine($"- Numero de paso: {step}"); }
        sb.AppendLine($"- Tipo: {context.Node.NodeType}");
        if (!string.IsNullOrWhiteSpace(context.Node.Description))
        {
            sb.AppendLine($"- Instrucciones del paso: {context.Node.Description}");
        }

        if (context.Node.Form is { } form)
        {
            sb.AppendLine();
            sb.AppendLine($"## Formulario del paso: {form.Title} ({form.Code})");
            if (!string.IsNullOrWhiteSpace(form.Description)) { sb.AppendLine(form.Description); }
            foreach (var field in form.Fields)
            {
                var required = field.Required ? " [obligatorio]" : "";
                var help = string.IsNullOrWhiteSpace(field.HelpText) ? "" : $" - {field.HelpText}";
                var options = string.IsNullOrWhiteSpace(field.OptionsJson) ? "" : $" - opciones: {field.OptionsJson}";
                sb.AppendLine($"- {field.Label} ({field.FieldCode}, {field.ControlType}){required}{help}{options}");
            }
            if (form.FieldsTruncated) { sb.AppendLine("- [el formulario tiene mas campos de los mostrados]"); }
        }

        // (b) Lo ya capturado antes: es donde suele estar el dato que decide el paso.
        sb.AppendLine();
        sb.AppendLine("# Datos capturados en pasos anteriores");
        if (context.PriorData.Forms.Count == 0)
        {
            sb.AppendLine("(ninguno)");
        }
        foreach (var prior in context.PriorData.Forms)
        {
            sb.AppendLine();
            sb.AppendLine($"## {prior.FormTitle} ({prior.FormCode}) - paso: {prior.NodeName ?? "sin nombre"}");
            foreach (var answer in prior.Answers)
            {
                sb.AppendLine($"- {answer.Label ?? answer.FieldCode}: {answer.Value ?? "(vacio)"}");
            }
            if (prior.AnswersTruncated) { sb.AppendLine("- [respuesta recortada: habia mas campos]"); }
        }
        if (context.PriorData.Truncated)
        {
            sb.AppendLine("- [hubo mas formularios previos de los mostrados]");
        }

        // (c) El caso: la tarea que disparo el flujo y el tercero involucrado.
        if (context.Task is { } task)
        {
            sb.AppendLine();
            sb.AppendLine("# Caso");
            sb.AppendLine($"- Numero: {task.Number}");
            sb.AppendLine($"- Titulo: {task.Title}");
            if (!string.IsNullOrWhiteSpace(task.Description)) { sb.AppendLine($"- Detalle: {task.Description}"); }
            sb.AppendLine($"- Estado: {task.Status} | Prioridad: {task.Priority}");
            if (task.DueDate is { } due) { sb.AppendLine($"- Vence: {due:yyyy-MM-dd}"); }
            if (!string.IsNullOrWhiteSpace(task.RequesterName)) { sb.AppendLine($"- Solicitante: {task.RequesterName}"); }
            if (task.Tercero is { } tercero)
            {
                sb.AppendLine($"- Tercero: {tercero.Nombre} ({tercero.Tipo})"
                    + (string.IsNullOrWhiteSpace(tercero.IdValor) ? "" : $" id {tercero.IdValor}"));
            }
        }

        // (d) Por donde paso el caso: aprobaciones y comentarios previos.
        sb.AppendLine();
        sb.AppendLine("# Historial del caso");
        foreach (var historyStep in context.History.Steps)
        {
            var marker = historyStep.IsCurrent ? " <- paso actual" : "";
            var approval = string.IsNullOrWhiteSpace(historyStep.ApprovalResult) ? "" : $" | resultado: {historyStep.ApprovalResult}";
            var comment = string.IsNullOrWhiteSpace(historyStep.ApprovalComment) ? "" : $" | comentario: {historyStep.ApprovalComment}";
            var who = string.IsNullOrWhiteSpace(historyStep.ExecutedByEmail) ? "" : $" | por: {historyStep.ExecutedByEmail}";
            sb.AppendLine($"- [{historyStep.Status}] {historyStep.NodeName ?? "sin nombre"}{approval}{comment}{who}{marker}");
        }
        if (context.History.Truncated)
        {
            sb.AppendLine($"- [se muestran los mas recientes de {context.History.TotalSteps} pasos]");
        }

        return sb.ToString();
    }
}
