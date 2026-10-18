namespace Ecorex.Application.Forms;

/// <summary>
/// FEATURE E (firma por link, por FILA de grilla). Orquesta el ciclo: el operario EMITE un link para que
/// un tercero firme UNA celda de firma de UNA fila de una respuesta existente; el tercero abre /firmar/{token}
/// y FIRMA (la imagen se guarda como StoredFile y la referencia /archivo/{id} queda en la celda); el operario
/// puede ANULAR una firma (destruir + re-pedir). Token solo-hash, un-solo-uso, con vencimiento, con el UNICO
/// punto cross-tenant permitido en la validacion anonima (por hash exacto). Ver <see cref="Ecorex.Domain.Entities.FormSignatureToken"/>.
/// </summary>
public interface IFormSignatureService
{
    /// <summary>
    /// Emite un token de firma para una celda de fila y arma el link absoluto. Revoca cualquier link vivo previo
    /// de la MISMA celda (un solo link activo). Opcionalmente envia el link por WhatsApp a traves de la linea de
    /// la conversacion de la tarea. Tenant-scoped (lo llama el operario autenticado).
    /// </summary>
    Task<FormResult<RequestSignatureResult>> RequestAsync(RequestSignatureInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Visor anonimo: valida el token (existe por hash, no vencido, no firmado, no revocado). NO requiere tenant
    /// en contexto; devuelve el TenantId del token para que el visor lo fije como ambient. Resultado neutro.
    /// </summary>
    Task<SignatureTokenValidation> ValidateAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Resumen SOLO-LECTURA de la fila a firmar (para mostrar en /firmar). Requiere el tenant ambient fijado.</summary>
    Task<IReadOnlyList<SignatureRowCell>> GetRowSummaryAsync(Guid responseId, string fieldCode, string rowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Visor anonimo (tenant ambient ya fijado): guarda la firma (PNG en data-URL), escribe la referencia
    /// /archivo/{id} en la celda de la fila y QUEMA el token. Concurrencia optimista de la respuesta.
    /// </summary>
    Task<FormResult<bool>> SignAsync(string token, string signatureDataUrl, string? signerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operario (tenant-scoped): ANULA la firma de una celda. Limpia la celda, borra la imagen (StoredFile) y
    /// revoca los tokens vivos de esa celda, para poder re-pedir. "destruir/re-pedir".
    /// </summary>
    Task<FormResult<bool>> VoidAsync(Guid responseId, string fieldCode, string rowId, string columnId, CancellationToken cancellationToken = default);
}

/// <summary>Datos para emitir (y opcionalmente enviar) un link de firma de una celda de fila.</summary>
public sealed record RequestSignatureInput(
    Guid ResponseId,
    string FieldCode,
    string RowId,
    string ColumnId,
    int LinkHours,
    bool RequireName,
    bool SendWhatsApp,
    string? Phone,
    string? Message,
    Guid ActorUserId,
    // Vigencia en MINUTOS (1..525600). Si es > 0 MANDA sobre LinkHours (vencimientos cortos). 0 = usar LinkHours.
    int LinkMinutes = 0);

/// <summary>Resultado de emitir: el token en claro (una sola vez), el link absoluto y el estado del envio.</summary>
public sealed record RequestSignatureResult(
    Guid TokenId,
    string ClearToken,
    string? Link,
    DateTimeOffset ExpiresAt,
    bool Sent,
    string? PhoneUsed,
    string? SendError);

/// <summary>Validacion neutra del token de firma. Invalid no expone el motivo.</summary>
public sealed record SignatureTokenValidation(
    bool IsValid,
    Guid TokenId,
    Guid TenantId,
    Guid ResponseId,
    string FieldCode,
    string RowId,
    string ColumnId,
    bool RequireName)
{
    public static readonly SignatureTokenValidation Invalid =
        new(false, default, default, default, "", "", "", false);
}

/// <summary>Una celda del resumen de la fila (etiqueta + valor) para el visor de firma.</summary>
public sealed record SignatureRowCell(string Label, string Value);
