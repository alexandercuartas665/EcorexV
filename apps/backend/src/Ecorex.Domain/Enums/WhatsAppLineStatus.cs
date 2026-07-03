namespace Ecorex.Domain.Enums;

/// <summary>Estado operativo de una linea WhatsApp del tenant (modulo 1.4).</summary>
public enum WhatsAppLineStatus
{
    Created,
    Connecting,
    Connected,
    Disconnected,
    Failed,
    Disabled
}
