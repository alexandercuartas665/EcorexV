using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Cuenta maestra de un proveedor de IA de la plataforma (Super Admin SaaS). GLOBAL: un registro
/// por proveedor (Claude, Gemini, ChatGpt, DeepSeek). La API key se guarda cifrada (ISecretProtector)
/// y nunca se expone completa ni se loggea. Las agencias usan estos proveedores en sus agentes.
/// </summary>
public class AiProviderConfig : BaseEntity
{
    public AiProvider Provider { get; set; }

    /// <summary>API key del proveedor cifrada en reposo.</summary>
    public string? ApiKeyEncrypted { get; set; }

    /// <summary>Modelo por defecto del proveedor (ej. claude-opus-4-7, gpt-4o, gemini-2.5-pro).</summary>
    public string? Model { get; set; }

    /// <summary>URL base opcional (para gateways/compatibilidad o self-hosting).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Si esta habilitado para que las agencias lo usen en sus agentes.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Marca este proveedor como el que usa el ASISTENTE DE CREACION DE FORMULARIOS ("IA gestion de
    /// formularios"). Solo UNO deberia tenerlo activo; el servicio lo garantiza al guardar (lo apaga en los
    /// demas). Si ninguno lo tiene, el asistente cae a Gemini por defecto. Requiere IsEnabled + API key.</summary>
    public bool UseForFormBuilder { get; set; }

    /// <summary>TRANSCRIPTOR DE PLATAFORMA (ADR-0174), solo aplica a la fila de Gemini: modelo con que se
    /// transcriben las notas de voz de los agentes cuyo proveedor no oye audio (DeepSeek, ChatGPT, Claude).
    /// Null = gemini-2.5-flash. El consumo se carga al tenant del agente.</summary>
    public string? TranscriptionModel { get; set; }

    /// <summary>Marca este proveedor como el del AGENTE EDITOR DE PLANTILLAS (correo y WhatsApp, ADR-0174):
    /// "IA gestion de plantillas". Single-select igual que <see cref="UseForFormBuilder"/>. Si ninguno lo
    /// tiene, el agente cae al proveedor de formularios y luego a Gemini.</summary>
    public bool UseForTemplateBuilder { get; set; }
}
