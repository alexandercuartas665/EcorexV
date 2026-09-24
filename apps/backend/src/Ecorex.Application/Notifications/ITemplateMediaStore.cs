namespace Ecorex.Application.Notifications;

/// <summary>
/// Publica un archivo (p.ej. el PDF de una cotizacion) en una URL PUBLICA para usarlo como MEDIA del
/// encabezado de una plantilla HSM de WhatsApp (YCloud/Cloud). Meta/YCloud descarga ese documento por su
/// URL al enviar, asi que TIENE que ser accesible desde internet: se construye con la base publica del
/// entorno (<c>ECOREX_PUBLIC_BASE_URL</c>). En local (base = localhost) Meta no la alcanza; por eso el
/// header de media solo entrega en prod. Devuelve null si no hay base publica configurada o no hay bytes.
/// </summary>
public interface ITemplateMediaStore
{
    Task<string?> PublishAsync(byte[] bytes, string fileName, CancellationToken cancellationToken = default);
}
