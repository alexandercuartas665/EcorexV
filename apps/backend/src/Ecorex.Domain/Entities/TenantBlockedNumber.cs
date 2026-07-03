using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Numero de telefono en la lista negra GLOBAL del tenant: ningun agente de IA le responde.
/// Es compartida por todos los agentes. La comparacion en el dispatcher es por digitos (tolera "+",
/// espacios y el codigo de pais sobrante).
/// </summary>
public class TenantBlockedNumber : TenantEntity
{
    /// <summary>Telefono normalizado a solo digitos.</summary>
    public string Phone { get; set; } = null!;

    /// <summary>Nota opcional: motivo, o como se agrego (ej. comando Manejo_asesor).</summary>
    public string? Note { get; set; }
}
