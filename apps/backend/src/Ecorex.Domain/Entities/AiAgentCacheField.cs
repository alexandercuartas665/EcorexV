using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de un dato que el agente debe ir capturando durante la conversacion (capa 3, datos cache).
/// Entidad TENANT-SCOPED. Ej.: "pais", "nombre", "destino". El tenant lo configura desde el agente y el
/// motor de inferencia luego intenta llenarlo a partir de los mensajes del cliente.
/// </summary>
public class AiAgentCacheField : TenantEntity
{
    public Guid AgentId { get; set; }
    public AiAgent? Agent { get; set; }

    /// <summary>Clave del dato (slug derivado del nombre). Unica por agente.</summary>
    public string FieldKey { get; set; } = null!;

    /// <summary>Nombre visible del dato (ej. "Pais").</summary>
    public string Label { get; set; } = null!;

    /// <summary>De que trata el dato; le sirve al motor para extraerlo del texto del cliente.</summary>
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Si esta en true, el motor de inferencia puede sobrescribir el valor del dato durante la
    /// conversacion (ej. el cliente cambia el destino). Si esta en false, una vez capturado queda
    /// fijo y no se actualiza (ej. tipo_cliente, idContacto).
    /// </summary>
    public bool IsUpdatable { get; set; } = true;
}
