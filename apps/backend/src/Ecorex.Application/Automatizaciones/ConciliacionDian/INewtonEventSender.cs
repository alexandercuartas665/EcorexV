namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

/// <summary>
/// Envia un evento RADIAN a NEWTON (recepcion de facturas). Es un acto LEGAL IRREVERSIBLE ante la DIAN:
/// quien lo llama debe haberlo gateado (confirmacion explicita). La implementacion resuelve las credenciales
/// (URL + Auth-Token) de la config del dron del tenant (variables NEWTON_URL / NEWTON_TOKEN) y hace el POST.
/// Vive como interfaz en Application para no meter HTTP aqui; la implementacion esta en la capa de presentacion.
/// </summary>
public interface INewtonEventSender
{
    /// <summary>POST /documentos-electronicos/{eventId}/event/{tipoEvento}. Devuelve (Ok, Error). tipoEvento:
    /// ACUSE_DE_RECIBO / RECIBO_DE_PRESTACION / ACEPTACION_EXPRESA / RECLAMO.</summary>
    Task<(bool Ok, string? Error)> SendEventAsync(string eventId, string tipoEvento, CancellationToken ct = default);
}
