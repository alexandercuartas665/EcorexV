using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Bot de Telegram PROPIO de un tenant (agencia), para enviar alertas (p.ej. el CIERRE del agente) a un
/// chat o grupo de Telegram. Tenant-scoped (filtro global); un registro por tenant. El token del bot
/// (de BotFather) se guarda cifrado (ISecretProtector) y nunca se expone ni se loggea.
///
/// El destino concreto (chat_id) lo define cada alerta; aqui vive solo la credencial del bot.
/// </summary>
public class TenantTelegramConfig : TenantEntity
{
    /// <summary>Token del bot de Telegram (de BotFather), cifrado en reposo.</summary>
    public string? BotTokenEncrypted { get; set; }

    /// <summary>Usuario del bot (@nombre_bot), solo como etiqueta legible. Opcional.</summary>
    public string? BotUsername { get; set; }

    /// <summary>Si esta habilitado el envio por Telegram con esta config del tenant.</summary>
    public bool IsEnabled { get; set; }

    public DateTimeOffset? LastValidatedAt { get; set; }
}
