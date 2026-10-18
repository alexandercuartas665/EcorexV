using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// FEATURE E (firma por link, por FILA de grilla): token opaco que habilita a un tercero a firmar
/// UNA celda de firma de UNA fila de una respuesta EXISTENTE, sin sesion, desde /firmar/{token}. A
/// diferencia de <see cref="FormToken"/> (que CREA respuestas nuevas), este apunta a una respuesta ya
/// creada (<see cref="ResponseId"/>) + la grilla (<see cref="FieldCode"/>) + la fila (<see cref="RowId"/>,
/// el mismo "__rowId" estable que usan las gestiones por fila, ADR-0085) + la columna de firma
/// (<see cref="ColumnId"/>). Igual que FormToken se guarda SOLO el hash SHA-256 del token en claro; la
/// resolucion del visor anonimo es el unico punto cross-tenant permitido (por hash exacto con
/// IgnoreQueryFilters) y luego fija el tenant del token como ambient. TENANT-SCOPED.
///
/// Ciclo: emitido -&gt; (vence) | (se usa: <see cref="SignedAt"/> + <see cref="StoredFileId"/>) | (se revoca:
/// <see cref="RevokedAt"/>). Un solo uso implicito: validado vuelve invalido una vez firmado. Que venza o
/// se use NUNCA borra una firma ya capturada (vive en el StoredFile + la celda de la respuesta); solo una
/// anulacion explicita del operario la retira.
/// </summary>
public class FormSignatureToken : TenantEntity
{
    /// <summary>SHA-256 (hex minusculas, 64 chars) del token opaco. Unico por tenant.</summary>
    public string TokenHash { get; set; } = null!;

    /// <summary>Respuesta EXISTENTE que se firma.</summary>
    public Guid ResponseId { get; set; }
    public FormResponse? Response { get; set; }

    /// <summary>Field code de la pregunta GridDetail que contiene la columna de firma.</summary>
    public string FieldCode { get; set; } = null!;

    /// <summary>Identidad estable de la fila a firmar ("__rowId", GUID N). La misma que las gestiones por fila.</summary>
    public string RowId { get; set; } = null!;

    /// <summary>Id de la columna de firma dentro de la grilla.</summary>
    public string ColumnId { get; set; } = null!;

    /// <summary>Si es true, el firmante debe teclear su nombre antes de firmar.</summary>
    public bool RequireName { get; set; }

    /// <summary>Nombre del firmante capturado al firmar (si se pidio).</summary>
    public string? SignerName { get; set; }

    /// <summary>Telefono E.164 al que se envio el link (auditoria). Null si solo se copio el enlace.</summary>
    public string? TargetPhone { get; set; }

    /// <summary>Archivo de la imagen de la firma (StoredFile) una vez firmada. Null si aun no firma.</summary>
    public Guid? StoredFileId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Marca el momento de la firma (un-solo-uso): si no es null, el token ya no es firmable.</summary>
    public DateTimeOffset? SignedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
