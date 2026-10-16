namespace Ecorex.Application.Notifications;

/// <summary>
/// Fallback de <see cref="ITemplateMediaStore"/> para contextos que SOLO cargan la capa Application (pruebas,
/// procesos sin wwwroot): no publica nada y devuelve null (= "no hay base publica para alojar el archivo"). En
/// la app real (Ecorex.SuperAdmin) se registra DESPUES el <c>TemplateMediaStore</c> concreto, que GANA (ultima
/// registracion) y publica de verdad en <c>wwwroot/uploads/templates</c>. Existe para que inyectar
/// ITemplateMediaStore en servicios de Application (p.ej. el conector de WhatsApp, para el envio de media por
/// YCloud) nunca rompa la resolucion de dependencias donde no hay un almacen publico configurado.
/// </summary>
public sealed class NullTemplateMediaStore : ITemplateMediaStore
{
    public Task<string?> PublishAsync(byte[] bytes, string fileName, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}
