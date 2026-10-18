using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Forms.Calc;
using Ecorex.Application.Forms.Lookups;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms;

/// <summary>
/// Implementacion de <see cref="IFormSignatureService"/> (FEATURE E). Mismo esquema de token solo-hash y
/// resolucion anonima por hash exacto que <see cref="FormTokenService"/>. La celda de firma guarda la
/// referencia "/archivo/{id}" de un <see cref="StoredFile"/>; que el token venza o se use nunca borra esa
/// firma (solo una anulacion explicita la retira). La identidad de fila es el mismo "__rowId" estable de las
/// gestiones por fila (ADR-0085).
/// </summary>
public sealed class FormSignatureService : IFormSignatureService
{
    /// <summary>Clave de identidad de fila dentro del JSON de la grilla (igual que las gestiones por fila).</summary>
    public const string RowIdKey = "__rowId";

    private const string Module = "FormSignature";
    private const int MaxSignatureBytes = 2 * 1024 * 1024; // 2 MB

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IFileStorageService _files;
    private readonly INotifyLinkBuilder _links;
    private readonly IWhatsAppConnectorService _whatsapp;

    public FormSignatureService(
        IApplicationDbContext db,
        ITenantContext tenant,
        IFileStorageService files,
        INotifyLinkBuilder links,
        IWhatsAppConnectorService whatsapp)
    {
        _db = db;
        _tenant = tenant;
        _files = files;
        _links = links;
        _whatsapp = whatsapp;
    }

    public async Task<FormResult<RequestSignatureResult>> RequestAsync(RequestSignatureInput input, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return FormResult<RequestSignatureResult>.Invalid("No hay tenant activo.");
        }
        if (string.IsNullOrWhiteSpace(input.FieldCode) || string.IsNullOrWhiteSpace(input.RowId) || string.IsNullOrWhiteSpace(input.ColumnId))
        {
            return FormResult<RequestSignatureResult>.Invalid("Falta la grilla, la fila o la columna de firma.");
        }

        var response = await _db.FormResponses.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == input.ResponseId, cancellationToken);
        if (response is null)
        {
            return FormResult<RequestSignatureResult>.NotFound("Registro no encontrado.");
        }

        // Un solo link vivo por celda: revoca los previos no firmados.
        await RevokeLiveTokensAsync(input.ResponseId, input.FieldCode, input.RowId, input.ColumnId, cancellationToken);

        // Vigencia: minutos manda sobre horas (vencimientos cortos). Fuera de rango o ausente -> horas (default 72 h).
        var expiresAt = input.LinkMinutes is >= 1 and <= 525600
            ? DateTimeOffset.UtcNow.AddMinutes(input.LinkMinutes)
            : DateTimeOffset.UtcNow.AddHours(input.LinkHours is < 1 or > 8760 ? 72 : input.LinkHours);
        var clearToken = GenerateToken();
        var token = new FormSignatureToken
        {
            TenantId = tenantId,
            TokenHash = HashToken(clearToken),
            ResponseId = input.ResponseId,
            FieldCode = input.FieldCode.Trim(),
            RowId = input.RowId.Trim(),
            ColumnId = input.ColumnId.Trim(),
            RequireName = input.RequireName,
            ExpiresAt = expiresAt,
        };
        _db.FormSignatureTokens.Add(token);
        await _db.SaveChangesAsync(cancellationToken);

        var link = _links.BuildSignatureLink(clearToken);

        var sent = false;
        string? phoneUsed = null;
        string? sendError = null;
        if (input.SendWhatsApp)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                sendError = "No hay URL publica configurada; copia el link y compartelo.";
            }
            else
            {
                (sent, phoneUsed, sendError) = await TrySendWhatsAppAsync(input, link!, cancellationToken);
                if (sent && !string.IsNullOrWhiteSpace(phoneUsed))
                {
                    token.TargetPhone = phoneUsed!.Length > 32 ? phoneUsed[..32] : phoneUsed;
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        return FormResult<RequestSignatureResult>.Ok(new RequestSignatureResult(
            token.Id, clearToken, link, token.ExpiresAt, sent, phoneUsed, sendError));
    }

    public async Task<SignatureTokenValidation> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return SignatureTokenValidation.Invalid;
        }
        var hash = HashToken(token.Trim());

        // UNICO punto cross-tenant permitido: el visor anonimo no tiene tenant en contexto; busca por el
        // hash exacto (imposible de enumerar) y devuelve el TenantId para fijar el ambient.
        var entity = await _db.FormSignatureTokens.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (entity is null
            || entity.ExpiresAt <= DateTimeOffset.UtcNow
            || entity.SignedAt is not null
            || entity.RevokedAt is not null)
        {
            return SignatureTokenValidation.Invalid;
        }

        return new SignatureTokenValidation(
            true, entity.Id, entity.TenantId, entity.ResponseId,
            entity.FieldCode, entity.RowId, entity.ColumnId, entity.RequireName);
    }

    public async Task<IReadOnlyList<SignatureRowCell>> GetRowSummaryAsync(Guid responseId, string fieldCode, string rowId, CancellationToken cancellationToken = default)
    {
        var response = await _db.FormResponses.AsNoTracking().FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);
        if (response is null) { return []; }

        var row = FindRow(response.Data, fieldCode, rowId);
        if (row is null) { return []; }

        var question = await _db.FormQuestions.AsNoTracking()
            .FirstOrDefaultAsync(q => q.DefinitionId == response.DefinitionId && q.FieldCode == fieldCode, cancellationToken);
        var columns = FormGridCalculator.ParseColumns(question?.OptionsJson);
        var extras = FormGridColumnLookupParser.Parse(question?.OptionsJson);

        var cells = new List<SignatureRowCell>();
        foreach (var col in columns)
        {
            // No mostrar columnas de firma ni campos internos.
            if (extras.GetValueOrDefault(col.Id)?.Signature is not null) { continue; }
            var value = row.GetValueOrDefault(col.Id);
            if (string.IsNullOrWhiteSpace(value)) { continue; }
            cells.Add(new SignatureRowCell(string.IsNullOrWhiteSpace(col.Label) ? col.Id : col.Label, value!));
        }
        return cells;
    }

    public async Task<FormResult<bool>> SignAsync(string token, string signatureDataUrl, string? signerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return FormResult<bool>.Invalid("Enlace no valido.");
        }
        var hash = HashToken(token.Trim());
        var entity = await _db.FormSignatureTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (entity is null
            || entity.ExpiresAt <= DateTimeOffset.UtcNow
            || entity.SignedAt is not null
            || entity.RevokedAt is not null)
        {
            return FormResult<bool>.Invalid("Este enlace ya no admite firmas.");
        }
        if (entity.RequireName && string.IsNullOrWhiteSpace(signerName))
        {
            return FormResult<bool>.Invalid("Escribe tu nombre para firmar.");
        }

        if (!TryDecodeDataUrl(signatureDataUrl, out var bytes, out var contentType))
        {
            return FormResult<bool>.Invalid("La firma no es valida.");
        }
        if (bytes.Length > MaxSignatureBytes)
        {
            return FormResult<bool>.Invalid("La firma es demasiado grande.");
        }

        var stored = await _files.SaveAsync(new FileStorageSaveRequest(
            Module,
            Ref1: entity.ResponseId.ToString("N"),
            Ref2: Shorten(entity.ColumnId, 60),
            FileName: $"{entity.RowId}.png",
            ContentType: contentType,
            Content: bytes), cancellationToken);

        // Escribe la referencia en la celda de la fila (concurrencia optimista de la respuesta).
        var response = await _db.FormResponses.FirstOrDefaultAsync(r => r.Id == entity.ResponseId, cancellationToken);
        if (response is null)
        {
            return FormResult<bool>.NotFound("Registro no encontrado.");
        }
        if (!TrySetCell(response, entity.FieldCode, entity.RowId, entity.ColumnId, stored.Url))
        {
            return FormResult<bool>.Invalid("No se encontro la fila a firmar.");
        }
        await _db.SaveChangesAsync(cancellationToken);

        entity.StoredFileId = stored.Id;
        entity.SignedAt = DateTimeOffset.UtcNow;
        entity.SignerName = string.IsNullOrWhiteSpace(signerName) ? null : Shorten(signerName!.Trim(), 160);
        await _db.SaveChangesAsync(cancellationToken);

        return FormResult<bool>.Ok(true);
    }

    public async Task<FormResult<bool>> VoidAsync(Guid responseId, string fieldCode, string rowId, string columnId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is null)
        {
            return FormResult<bool>.Invalid("No hay tenant activo.");
        }

        var response = await _db.FormResponses.FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);
        if (response is null)
        {
            return FormResult<bool>.NotFound("Registro no encontrado.");
        }

        // Borra la imagen de las firmas ya hechas para esta celda.
        var signed = await _db.FormSignatureTokens
            .Where(t => t.ResponseId == responseId && t.FieldCode == fieldCode && t.RowId == rowId && t.ColumnId == columnId && t.StoredFileId != null)
            .ToListAsync(cancellationToken);
        foreach (var t in signed)
        {
            if (t.StoredFileId is Guid fileId)
            {
                try { await _files.DeleteAsync(fileId, cancellationToken); } catch { /* best-effort */ }
            }
        }

        // Limpia la celda y revoca los tokens vivos (para re-pedir).
        TrySetCell(response, fieldCode, rowId, columnId, null);
        await RevokeLiveTokensAsync(responseId, fieldCode, rowId, columnId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<bool>.Ok(true);
    }

    // ---- Helpers ----

    private async Task RevokeLiveTokensAsync(Guid responseId, string fieldCode, string rowId, string columnId, CancellationToken ct)
    {
        var live = await _db.FormSignatureTokens
            .Where(t => t.ResponseId == responseId && t.FieldCode == fieldCode && t.RowId == rowId && t.ColumnId == columnId
                        && t.SignedAt == null && t.RevokedAt == null)
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var t in live) { t.RevokedAt = now; }
    }

    private async Task<(bool sent, string? phone, string? error)> TrySendWhatsAppAsync(RequestSignatureInput input, string link, CancellationToken ct)
    {
        var phone = input.Phone?.Trim();
        if (string.IsNullOrWhiteSpace(phone))
        {
            return (false, null, "No hay telefono para enviar el link; copialo y compartelo.");
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        var last = digits.Length >= 10 ? digits[^10..] : digits;
        var conv = await _db.Conversations.AsNoTracking()
            .Where(c => c.WhatsAppLineId != null && c.ContactPhone.Contains(last))
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (conv?.WhatsAppLineId is not Guid lineId)
        {
            return (false, phone, "No hay una conversacion de WhatsApp para enviar el link; copialo y compartelo.");
        }

        var text = ComposeMessage(input.Message, link);
        var result = await _whatsapp.SendTestAsync(lineId, phone, text, input.ActorUserId, cancellationToken: ct);
        return result.Ok ? (true, phone, null) : (false, phone, result.Error ?? "No se pudo enviar el link por WhatsApp.");
    }

    private static string ComposeMessage(string? template, string link)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return $"Hola, por favor firma el recibido en este enlace: {link}";
        }
        var t = template.Trim();
        return t.Contains("{link}", StringComparison.OrdinalIgnoreCase)
            ? t.Replace("{link}", link, StringComparison.OrdinalIgnoreCase)
            : $"{t}\n{link}";
    }

    /// <summary>Localiza la fila (por __rowId) en el JSON de la grilla. Null si no existe.</summary>
    private static Dictionary<string, string?>? FindRow(string? data, string fieldCode, string rowId)
    {
        var doc = FormResponseService.ParseDocument(data);
        if (!doc.TryGetValue(fieldCode, out var gridVal)) { return null; }
        var rows = FormFieldValidator.ParseGridRows(gridVal.Value);
        foreach (var r in rows)
        {
            if (string.Equals(r.GetValueOrDefault(RowIdKey), rowId, StringComparison.Ordinal))
            {
                return new Dictionary<string, string?>(r, StringComparer.Ordinal);
            }
        }
        return null;
    }

    /// <summary>Escribe (o limpia con value=null) una celda de la fila por __rowId y reserializa el documento.</summary>
    private static bool TrySetCell(FormResponse response, string fieldCode, string rowId, string columnId, string? value)
    {
        var doc = new Dictionary<string, FormFieldValue>(FormResponseService.ParseDocument(response.Data), StringComparer.Ordinal);
        if (!doc.TryGetValue(fieldCode, out var gridVal)) { return false; }
        var rows = FormFieldValidator.ParseGridRows(gridVal.Value)
            .Select(r => new Dictionary<string, string?>(r, StringComparer.Ordinal))
            .ToList();
        var idx = rows.FindIndex(r => string.Equals(r.GetValueOrDefault(RowIdKey), rowId, StringComparison.Ordinal));
        if (idx < 0) { return false; }
        rows[idx][columnId] = value;
        doc[fieldCode] = new FormFieldValue(JsonSerializer.Serialize(rows, JsonOptions), gridVal.Type);
        response.Data = JsonSerializer.Serialize(doc, JsonOptions);
        return true;
    }

    private static bool TryDecodeDataUrl(string? dataUrl, out byte[] bytes, out string contentType)
    {
        bytes = [];
        contentType = "image/png";
        if (string.IsNullOrWhiteSpace(dataUrl)) { return false; }
        var s = dataUrl.Trim();
        var comma = s.IndexOf(',');
        var b64 = comma >= 0 ? s[(comma + 1)..] : s;
        if (comma >= 0 && s.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var meta = s[5..comma];
            var semi = meta.IndexOf(';');
            var mime = (semi >= 0 ? meta[..semi] : meta).Trim();
            if (!string.IsNullOrWhiteSpace(mime)) { contentType = mime; }
        }
        try
        {
            bytes = Convert.FromBase64String(b64);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..max];

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string HashToken(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
