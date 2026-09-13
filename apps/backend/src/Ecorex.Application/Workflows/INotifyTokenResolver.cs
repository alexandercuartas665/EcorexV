namespace Ecorex.Application.Workflows;

/// <summary>
/// Resuelve tokens de plantilla para las notificaciones de nodo: datos de la TAREA ({tarea.numero},
/// {tarea.contacto}, {tarea.telefono}, ...) y datos de los FORMULARIOS empacados en el flujo, leidos de las
/// respuestas ancladas a la tarea ({form.&lt;codigo_de_campo&gt;}). El diccionario resultante trae las claves con
/// y sin prefijo, para reusarse tanto en el renderizado de texto libre como en el mapeo de variables de una
/// plantilla HSM de WhatsApp (que resuelve por NOMBRE de variable).
/// </summary>
public interface INotifyTokenResolver
{
    /// <summary>Arma el mapa de tokens de una tarea (datos de tarea + campos de sus formularios anclados).</summary>
    Task<IReadOnlyDictionary<string, string>> BuildAsync(Domain.Entities.TaskItem task, CancellationToken cancellationToken = default);

    /// <summary>Sustituye {ns.clave} en la plantilla usando el mapa (case-insensitive). Token desconocido = vacio.</summary>
    string Render(string? template, IReadOnlyDictionary<string, string> tokens);
}
