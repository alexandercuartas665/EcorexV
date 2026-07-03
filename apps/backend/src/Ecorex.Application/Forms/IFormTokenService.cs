namespace Ecorex.Application.Forms;

/// <summary>
/// Tokens opacos de publicacion de formularios por URL (/f/{token}, ADR-0015). El token en
/// claro se devuelve UNA sola vez al emitir; solo se persiste su hash SHA-256. La validacion
/// del visor anonimo es el UNICO punto cross-tenant permitido del modulo: busca por hash
/// exacto con IgnoreQueryFilters (sin tenant en contexto) y devuelve el TenantId del token
/// para que el visor lo fije como ambient (AmbientTenantContext.Begin) en el resto del
/// pipeline.
/// </summary>
public interface IFormTokenService
{
    /// <summary>Emite un token para la definicion (que debe estar Active). Requiere tenant activo.</summary>
    Task<FormResult<EmitFormTokenResult>> EmitAsync(Guid definitionId, EmitFormTokenRequest options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida un token en claro con las 4 verificaciones: existe (por hash), no expirado,
    /// no usado (si SingleUse) y no revocado. NO requiere tenant en contexto. El resultado
    /// invalido es neutro (sin motivo) para no filtrar informacion.
    /// </summary>
    Task<FormTokenValidation> ValidateAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Revoca el token (tenant-scoped: solo tokens del tenant activo).</summary>
    Task<FormResult<bool>> RevokeAsync(Guid tokenId, CancellationToken cancellationToken = default);

    /// <summary>Marca el token como usado tras un submit exitoso si es SingleUse (tenant-scoped).</summary>
    Task MarkUsedAsync(Guid tokenId, CancellationToken cancellationToken = default);

    /// <summary>Tokens emitidos para una definicion (para administrarlos desde el disenador).</summary>
    Task<IReadOnlyList<FormTokenDto>> ListAsync(Guid definitionId, CancellationToken cancellationToken = default);
}
