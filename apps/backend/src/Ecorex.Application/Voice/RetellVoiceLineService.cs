using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Voice;

/// <summary>
/// CRUD de LINEAS de voz IA (Retell/Telnyx) del tenant. Varias por tenant (filtro global). Secretos cifrados
/// con <see cref="ISecretProtector"/> (DataProtection); nunca se devuelven ni se loguean. Solo una linea puede
/// ser la "por defecto".
/// </summary>
public sealed class RetellVoiceLineService : IRetellVoiceLineService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IRetellApiClient _retell;
    private readonly TimeProvider _clock;

    public RetellVoiceLineService(IApplicationDbContext db, ISecretProtector protector, IRetellApiClient retell, TimeProvider clock)
    {
        _db = db;
        _protector = protector;
        _retell = retell;
        _clock = clock;
    }

    public async Task<IReadOnlyList<RetellVoiceLineDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var lines = await _db.RetellVoiceLines.AsNoTracking().OrderBy(l => l.Name).ToListAsync(cancellationToken);
        return lines.Select(ToDto).ToList();
    }

    public async Task<RetellVoiceLineDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var line = await _db.RetellVoiceLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        return line is null ? null : ToDto(line);
    }

    public async Task<RetellVoiceLineDto> CreateAsync(SaveRetellVoiceLineRequest request, CancellationToken cancellationToken = default)
    {
        var line = new RetellVoiceLine();
        _db.RetellVoiceLines.Add(line);
        ApplyTo(line, request);
        await EnsureSingleDefaultAsync(line, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(line);
    }

    public async Task<RetellVoiceLineDto?> UpdateAsync(Guid id, SaveRetellVoiceLineRequest request, CancellationToken cancellationToken = default)
    {
        var line = await _db.RetellVoiceLines.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (line is null) { return null; }
        ApplyTo(line, request);
        await EnsureSingleDefaultAsync(line, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(line);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var line = await _db.RetellVoiceLines.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (line is null) { return; }
        _db.RetellVoiceLines.Remove(line);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RetellValidationResult> ValidateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var line = await _db.RetellVoiceLines.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (line is null || string.IsNullOrEmpty(line.RetellApiKeyEncrypted))
        {
            return new RetellValidationResult(false, "No hay API key de Retell en esta linea.");
        }

        string apiKey;
        try { apiKey = _protector.Unprotect(line.RetellApiKeyEncrypted); }
        catch { return new RetellValidationResult(false, "La API key almacenada no se pudo descifrar (posible cambio de entorno)."); }

        var ping = await _retell.PingAsync(apiKey, cancellationToken);
        if (!ping.Ok)
        {
            return new RetellValidationResult(false, ping.Error ?? $"Retell respondio HTTP {ping.StatusCode}.");
        }

        line.LastValidatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
        return new RetellValidationResult(true, null);
    }

    // ---- Internos ----

    private void ApplyTo(RetellVoiceLine line, SaveRetellVoiceLineRequest r)
    {
        line.Name = string.IsNullOrWhiteSpace(r.Name) ? "Linea" : r.Name.Trim();
        line.RetellApiKeyEncrypted = ApplySecret(line.RetellApiKeyEncrypted, r.ApiKey);
        line.SipPasswordEncrypted = ApplySecret(line.SipPasswordEncrypted, r.SipPassword);
        line.FromNumber = Normalize(r.FromNumber);
        line.TerminationUri = Normalize(r.TerminationUri);
        line.SipUsername = Normalize(r.SipUsername);
        line.VoiceId = Normalize(r.VoiceId);
        line.Language = string.IsNullOrWhiteSpace(r.Language) ? "es-419" : r.Language.Trim();
        line.IsEnabled = r.IsEnabled;
        line.IsDefault = r.IsDefault;
    }

    // Si esta linea queda como default, se quita el default a las demas del tenant (una sola por defecto).
    private async Task EnsureSingleDefaultAsync(RetellVoiceLine line, CancellationToken ct)
    {
        if (!line.IsDefault) { return; }
        var others = await _db.RetellVoiceLines.Where(l => l.IsDefault && l.Id != line.Id).ToListAsync(ct);
        foreach (var o in others) { o.IsDefault = false; }
    }

    private string? ApplySecret(string? current, string? incoming)
    {
        if (incoming is null) { return current; }
        if (incoming.Length == 0) { return null; }
        return _protector.Protect(incoming);
    }

    private static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static RetellVoiceLineDto ToDto(RetellVoiceLine l) => new(
        Id: l.Id,
        Name: l.Name,
        HasApiKey: !string.IsNullOrEmpty(l.RetellApiKeyEncrypted),
        FromNumber: l.FromNumber,
        TerminationUri: l.TerminationUri,
        SipUsername: l.SipUsername,
        HasSipPassword: !string.IsNullOrEmpty(l.SipPasswordEncrypted),
        VoiceId: l.VoiceId,
        Language: l.Language,
        IsEnabled: l.IsEnabled,
        IsDefault: l.IsDefault,
        LastValidatedAt: l.LastValidatedAt);
}
