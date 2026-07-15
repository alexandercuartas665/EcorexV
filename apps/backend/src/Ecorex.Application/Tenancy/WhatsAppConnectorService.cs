using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class WhatsAppConnectorService : IWhatsAppConnectorService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IEvolutionApiClient _client;
    private readonly IWhatsAppCloudClient _cloud;
    private readonly IYCloudApiClient _ycloud;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _timeProvider;

    public WhatsAppConnectorService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        ISecretProtector secretProtector,
        IEvolutionApiClient client,
        IWhatsAppCloudClient cloud,
        IYCloudApiClient ycloud,
        IAuditWriter audit,
        TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _secretProtector = secretProtector;
        _client = client;
        _cloud = cloud;
        _ycloud = ycloud;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public async Task<EvolutionServerSettingDto> GetServerAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TenantEvolutionConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var master = await _db.EvolutionMasterConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var masterReady = master is not null && !string.IsNullOrWhiteSpace(master.BaseUrl) && !string.IsNullOrWhiteSpace(master.ApiKeyEncrypted);

        return new EvolutionServerSettingDto(
            UseMasterServer: cfg?.UseMasterServer ?? true,
            MasterReady: masterReady,
            MasterBaseUrl: master?.BaseUrl,
            OwnBaseUrl: cfg?.BaseUrl,
            OwnTokenMasked: cfg?.ApiTokenEncrypted is { } enc ? Mask(enc) : null,
            HasOwnToken: cfg?.ApiTokenEncrypted is not null);
    }

    public async Task<EvolutionServerSettingDto?> SetServerAsync(SetEvolutionServerRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var cfg = await _db.TenantEvolutionConfigs.FirstOrDefaultAsync(cancellationToken);
        if (cfg is null)
        {
            cfg = new TenantEvolutionConfig { TenantId = tenantId };
            _db.TenantEvolutionConfigs.Add(cfg);
        }

        cfg.UseMasterServer = request.UseMasterServer;
        if (!request.UseMasterServer)
        {
            cfg.BaseUrl = NormalizeBaseUrl(request.OwnBaseUrl);
            if (!string.IsNullOrWhiteSpace(request.OwnApiToken))
            {
                cfg.ApiTokenEncrypted = _secretProtector.Protect(request.OwnApiToken.Trim());
            }
        }
        cfg.IsActive = true;

        _audit.Write(actorUserId, "evolution.server.set", nameof(TenantEvolutionConfig), cfg.Id,
            previousValue: null, newValue: new { cfg.UseMasterServer, cfg.BaseUrl }, tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetServerAsync(cancellationToken);
    }

    public async Task<LineConnectResult> ConnectLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return new LineConnectResult(false, null, "La linea no existe.");
        }

        // Canal emulado (pruebas): no hay nada externo, queda conectada de inmediato.
        if (line.Provider == WhatsAppProvider.Emulator)
        {
            var nowE = _timeProvider.GetUtcNow();
            line.Status = WhatsAppLineStatus.Connected;
            line.LastStatusAt = nowE;
            line.LastConnectedAt = nowE;
            await _db.SaveChangesAsync(cancellationToken);
            return new LineConnectResult(true, null, null);
        }

        // Linea Cloud (Meta): no hay QR. Validar token + phone_number_id y marcar conectada.
        if (line.Provider == WhatsAppProvider.Cloud)
        {
            var creds = CloudCreds(line);
            if (creds is null) { return new LineConnectResult(false, null, "Faltan el phone_number_id o el token de la linea Cloud."); }
            var check = await _cloud.CheckAsync(creds.Value.phoneNumberId, creds.Value.token, cancellationToken);
            var now0 = _timeProvider.GetUtcNow();
            if (!check.Ok)
            {
                line.Status = WhatsAppLineStatus.Failed;
                line.LastStatusAt = now0;
                await _db.SaveChangesAsync(cancellationToken);
                return new LineConnectResult(false, null, check.Error ?? "No se pudo validar la linea con Meta.");
            }
            line.Status = WhatsAppLineStatus.Connected;
            line.LastStatusAt = now0;
            line.LastConnectedAt = now0;
            if (string.IsNullOrWhiteSpace(line.PhoneNumber) && !string.IsNullOrWhiteSpace(check.DisplayPhoneNumber)) { line.PhoneNumber = check.DisplayPhoneNumber; }
            _audit.Write(actorUserId, "whatsapp-line.connect", nameof(WhatsAppLine), line.Id,
                previousValue: null, newValue: new { provider = "Cloud", phoneNumberId = creds.Value.phoneNumberId }, tenantId: line.TenantId);
            await _db.SaveChangesAsync(cancellationToken);
            return new LineConnectResult(true, null, null); // sin QR
        }

        // Linea YCloud (BSP oficial): no hay QR. Validar la API key contra la cuenta y marcar conectada.
        if (line.Provider == WhatsAppProvider.YCloud)
        {
            var ycloudKey = YCloudApiKey(line);
            if (ycloudKey is null) { return new LineConnectResult(false, null, "Falta la API key de la linea YCloud."); }
            var check = await _ycloud.CheckAsync(ycloudKey, line.YCloudPhoneNumberId, cancellationToken);
            var nowY = _timeProvider.GetUtcNow();
            if (!check.IsValid)
            {
                line.Status = WhatsAppLineStatus.Failed;
                line.LastStatusAt = nowY;
                await _db.SaveChangesAsync(cancellationToken);
                return new LineConnectResult(false, null, check.Error ?? "No se pudo validar la linea con YCloud.");
            }
            line.Status = WhatsAppLineStatus.Connected;
            line.LastStatusAt = nowY;
            line.LastConnectedAt = nowY;
            if (string.IsNullOrWhiteSpace(line.PhoneNumber) && !string.IsNullOrWhiteSpace(check.VerifiedPhone)) { line.PhoneNumber = check.VerifiedPhone; }
            if (string.IsNullOrWhiteSpace(line.YCloudWabaId) && !string.IsNullOrWhiteSpace(check.WabaId)) { line.YCloudWabaId = check.WabaId; }
            _audit.Write(actorUserId, "whatsapp-line.connect", nameof(WhatsAppLine), line.Id,
                previousValue: null, newValue: new { provider = "YCloud", phone = line.YCloudPhoneNumberId }, tenantId: line.TenantId);
            await _db.SaveChangesAsync(cancellationToken);
            return new LineConnectResult(true, null, null); // sin QR
        }

        var server = await ResolveServerAsync(cancellationToken);
        if (server is null)
        {
            return new LineConnectResult(false, null, "No hay servidor Evolution configurado (ni maestro ni propio).");
        }

        var (baseUrl, apiKey) = server.Value;
        var result = await _client.CreateInstanceAsync(baseUrl, apiKey, EvoInstance(line), cancellationToken);
        if (!result.Ok)
        {
            line.Status = WhatsAppLineStatus.Failed;
            line.LastStatusAt = _timeProvider.GetUtcNow();
            await _db.SaveChangesAsync(cancellationToken);
            return new LineConnectResult(false, null, result.Error);
        }

        line.Status = WhatsAppLineStatus.Connecting;
        line.LastStatusAt = _timeProvider.GetUtcNow();
        _audit.Write(actorUserId, "whatsapp-line.connect", nameof(WhatsAppLine), line.Id,
            previousValue: null, newValue: new { instance = EvoInstance(line) }, tenantId: line.TenantId);
        await _db.SaveChangesAsync(cancellationToken);

        // Configura el webhook entrante para recibir mensajes en caliente (si esta configurado).
        var (webhookUrl, webhookToken) = await EffectiveWebhookAsync(cancellationToken);
        if (webhookUrl is not null && webhookToken is not null)
        {
            await _client.SetWebhookAsync(baseUrl, apiKey, EvoInstance(line), webhookUrl, webhookToken, cancellationToken);
        }

        return new LineConnectResult(true, result.QrBase64, null);
    }

    public async Task<WhatsAppLineDto?> RefreshAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return null;
        }

        if (line.Provider == WhatsAppProvider.Emulator)
        {
            if (line.Status != WhatsAppLineStatus.Connected)
            {
                line.Status = WhatsAppLineStatus.Connected;
                line.LastStatusAt = _timeProvider.GetUtcNow();
                await _db.SaveChangesAsync(cancellationToken);
            }
            return Map(line);
        }

        if (line.Provider == WhatsAppProvider.Cloud)
        {
            var creds = CloudCreds(line);
            if (creds is null) { return Map(line); }
            var check = await _cloud.CheckAsync(creds.Value.phoneNumberId, creds.Value.token, cancellationToken);
            var mappedCloud = check.Ok ? WhatsAppLineStatus.Connected : WhatsAppLineStatus.Failed;
            if (mappedCloud != line.Status)
            {
                var now = _timeProvider.GetUtcNow();
                line.Status = mappedCloud;
                line.LastStatusAt = now;
                if (mappedCloud == WhatsAppLineStatus.Connected) { line.LastConnectedAt = now; }
                await _db.SaveChangesAsync(cancellationToken);
            }
            return Map(line);
        }

        if (line.Provider == WhatsAppProvider.YCloud)
        {
            var key = YCloudApiKey(line);
            if (key is null) { return Map(line); }
            var check = await _ycloud.CheckAsync(key, line.YCloudPhoneNumberId, cancellationToken);
            var mappedY = check.IsValid ? WhatsAppLineStatus.Connected : WhatsAppLineStatus.Failed;
            if (mappedY != line.Status)
            {
                var now = _timeProvider.GetUtcNow();
                line.Status = mappedY;
                line.LastStatusAt = now;
                if (mappedY == WhatsAppLineStatus.Connected) { line.LastConnectedAt = now; }
                await _db.SaveChangesAsync(cancellationToken);
            }
            return Map(line);
        }

        var server = await ResolveServerAsync(cancellationToken);
        if (server is null)
        {
            return Map(line);
        }

        var (baseUrl, apiKey) = server.Value;
        var state = await _client.GetStateAsync(baseUrl, apiKey, EvoInstance(line), cancellationToken);
        if (state.Ok)
        {
            var mapped = state.State?.ToLowerInvariant() switch
            {
                "open" => WhatsAppLineStatus.Connected,
                "connecting" => WhatsAppLineStatus.Connecting,
                "close" => WhatsAppLineStatus.Disconnected,
                _ => line.Status
            };
            if (mapped != line.Status)
            {
                var now = _timeProvider.GetUtcNow();
                line.Status = mapped;
                line.LastStatusAt = now;
                if (mapped == WhatsAppLineStatus.Connected) { line.LastConnectedAt = now; }
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        return Map(line);
    }

    public async Task<bool> DisconnectAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return false;
        }
        if (line.Provider == WhatsAppProvider.Evolution)
        {
            var server = await ResolveServerAsync(cancellationToken);
            if (server is not null)
            {
                var (baseUrl, apiKey) = server.Value;
                await _client.DeleteInstanceAsync(baseUrl, apiKey, EvoInstance(line), cancellationToken);
            }
        }

        line.Status = WhatsAppLineStatus.Disconnected;
        line.LastStatusAt = _timeProvider.GetUtcNow();
        _audit.Write(actorUserId, "whatsapp-line.disconnect", nameof(WhatsAppLine), line.Id,
            previousValue: null, newValue: new { instance = EvoInstance(line) }, tenantId: line.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return false;
        }

        // Borra la instancia en Evolution (best-effort) antes de quitar la fila. Las lineas Cloud no tienen instancia remota.
        if (line.Provider == WhatsAppProvider.Evolution)
        {
            var server = await ResolveServerAsync(cancellationToken);
            if (server is not null)
            {
                var (baseUrl, apiKey) = server.Value;
                try { await _client.DeleteInstanceAsync(baseUrl, apiKey, EvoInstance(line), cancellationToken); }
                catch { /* la instancia puede no existir */ }
            }
        }

        _audit.Write(actorUserId, "whatsapp-line.delete", nameof(WhatsAppLine), line.Id,
            previousValue: new { line.InstanceName, line.Status }, newValue: null, tenantId: line.TenantId);

        _db.WhatsAppLines.Remove(line);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<LineSendResult> SendTestAsync(Guid lineId, string phone, string text, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(text))
        {
            return new LineSendResult(false, "Indica el numero y el mensaje.");
        }
        // IgnoreQueryFilters: tambien lo invoca el agente desde el despachador (disparado por webhook),
        // donde no hay usuario/tenant en sesion; el lineId ya viene resuelto en el tenant correcto.
        var line = await _db.WhatsAppLines.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return new LineSendResult(false, "La linea no existe.");
        }
        if (line.Status != WhatsAppLineStatus.Connected)
        {
            return new LineSendResult(false, "La linea no esta conectada.");
        }
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        bool ok; string? error; string? messageId;
        if (line.Provider == WhatsAppProvider.Emulator)
        {
            // Canal emulado: no se envia a ningun lado; exito sintetico (el mensaje queda en la conversacion/bitacora).
            (ok, error, messageId) = (true, null, "emu-" + Guid.NewGuid().ToString("N"));
        }
        else if (line.Provider == WhatsAppProvider.Cloud)
        {
            var creds = CloudCreds(line);
            if (creds is null) { return new LineSendResult(false, "Faltan credenciales Cloud en la linea."); }
            var r = await _cloud.SendTextAsync(creds.Value.phoneNumberId, creds.Value.token, digits, text.Trim(), cancellationToken);
            (ok, error, messageId) = (r.Ok, r.Error, r.MessageId);
        }
        else if (line.Provider == WhatsAppProvider.YCloud)
        {
            var key = YCloudApiKey(line);
            if (key is null || string.IsNullOrWhiteSpace(line.YCloudPhoneNumberId)) { return new LineSendResult(false, "Faltan la API key o el emisor de la linea YCloud."); }
            var r = await _ycloud.SendTextAsync(key, line.YCloudPhoneNumberId!, digits, text.Trim(), cancellationToken);
            (ok, error, messageId) = (r.IsSuccess, r.Error, r.MessageId);
        }
        else
        {
            var server = await ResolveServerAsync(cancellationToken);
            if (server is null) { return new LineSendResult(false, "No hay servidor Evolution configurado."); }
            var (baseUrl, apiKey) = server.Value;
            var r = await _client.SendTextAsync(baseUrl, apiKey, EvoInstance(line), digits, text.Trim(), cancellationToken);
            (ok, error, messageId) = (r.Ok, r.Error, r.MessageId);
        }

        _audit.Write(actorUserId, "whatsapp-line.test-send", nameof(WhatsAppLine), line.Id,
            previousValue: null, newValue: new { to = digits, ok }, tenantId: line.TenantId);

        return new LineSendResult(ok, error, messageId);
    }

    public async Task<LineSendResult> SendMediaAsync(Guid lineId, string phone, MessageMediaType mediaType, string base64, string? mimeType, string? fileName, string? caption, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var (err, line, digits) = await ReadyAsync(lineId, phone, cancellationToken);
        if (err is not null || line is null) { return new LineSendResult(false, err); }
        var mt = mediaType switch
        {
            MessageMediaType.Image => "image",
            MessageMediaType.Video => "video",
            MessageMediaType.Document => "document",
            MessageMediaType.Audio => "audio",
            _ => null
        };
        if (mt is null) { return new LineSendResult(false, "Tipo de adjunto no soportado."); }

        if (line.Provider == WhatsAppProvider.Emulator)
        {
            return new LineSendResult(true, null, "emu-" + Guid.NewGuid().ToString("N"));
        }

        if (line.Provider == WhatsAppProvider.Cloud)
        {
            var creds = CloudCreds(line);
            if (creds is null) { return new LineSendResult(false, "Faltan credenciales Cloud en la linea."); }
            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64); }
            catch { return new LineSendResult(false, "El adjunto no es base64 valido."); }
            var r = await _cloud.SendMediaAsync(creds.Value.phoneNumberId, creds.Value.token, digits, mt, bytes, mimeType, fileName, caption, cancellationToken);
            return new LineSendResult(r.Ok, r.Error, r.MessageId);
        }

        if (line.Provider == WhatsAppProvider.YCloud)
        {
            // YCloud envia media por URL publica; el runtime de ECOREX entrega en base64. Fuera de alcance por ahora.
            return new LineSendResult(false, "YCloud envia media por URL publica; el envio por archivo (base64) no esta soportado en este corte.");
        }

        var server = await ResolveServerAsync(cancellationToken);
        if (server is null) { return new LineSendResult(false, "No hay servidor Evolution configurado."); }
        var (baseUrl, apiKey) = server.Value;
        var instance = EvoInstance(line);
        var result = mediaType == MessageMediaType.Audio
            ? await _client.SendAudioAsync(baseUrl, apiKey, instance, digits, base64, cancellationToken)
            : await _client.SendMediaAsync(baseUrl, apiKey, instance, digits, mt, base64, mimeType, fileName, caption, cancellationToken);
        return new LineSendResult(result.Ok, result.Error, result.MessageId);
    }

    public async Task<LineSendResult> SendLocationAsync(Guid lineId, string phone, double latitude, double longitude, string? name, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var (err, line, digits) = await ReadyAsync(lineId, phone, cancellationToken);
        if (err is not null || line is null) { return new LineSendResult(false, err); }

        if (line.Provider == WhatsAppProvider.Emulator)
        {
            return new LineSendResult(true, null, "emu-" + Guid.NewGuid().ToString("N"));
        }

        if (line.Provider == WhatsAppProvider.Cloud)
        {
            var creds = CloudCreds(line);
            if (creds is null) { return new LineSendResult(false, "Faltan credenciales Cloud en la linea."); }
            var rc = await _cloud.SendLocationAsync(creds.Value.phoneNumberId, creds.Value.token, digits, latitude, longitude, name, null, cancellationToken);
            return new LineSendResult(rc.Ok, rc.Error, rc.MessageId);
        }

        if (line.Provider == WhatsAppProvider.YCloud)
        {
            return new LineSendResult(false, "YCloud no soporta el envio de ubicacion en este corte.");
        }

        var server = await ResolveServerAsync(cancellationToken);
        if (server is null) { return new LineSendResult(false, "No hay servidor Evolution configurado."); }
        var (baseUrl, apiKey) = server.Value;
        var result = await _client.SendLocationAsync(baseUrl, apiKey, EvoInstance(line), digits, latitude, longitude, name, null, cancellationToken);
        return new LineSendResult(result.Ok, result.Error, result.MessageId);
    }

    public async Task<Ecorex.Application.Admin.EvolutionMediaResult> FetchInboundMediaAsync(Guid lineId, string messageKeyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageKeyId)) { return new(false, null, null, "Falta el id del mensaje."); }
        // IgnoreQueryFilters: lo llama el webhook entrante (sin contexto de tenant en sesion).
        var line = await _db.WhatsAppLines.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null) { return new(false, null, null, "La linea no existe."); }
        if (line.Provider != WhatsAppProvider.Evolution) { return new(false, null, null, "La descarga de media por id solo aplica a lineas Evolution."); }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is null) { return new(false, null, null, "No hay servidor Evolution configurado."); }
        var (baseUrl, apiKey) = server.Value;
        return await _client.GetBase64FromMediaMessageAsync(baseUrl, apiKey, EvoInstance(line), messageKeyId, cancellationToken);
    }

    public async Task<LineSendResult> DeleteMessageForEveryoneAsync(Guid lineId, string phone, string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId)) { return new LineSendResult(false, "El mensaje no tiene id de WhatsApp (no se puede eliminar para todos)."); }
        var line = await _db.WhatsAppLines.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null) { return new LineSendResult(false, "La linea no existe."); }
        if (line.Provider == WhatsAppProvider.Cloud) { return new LineSendResult(false, "Meta Cloud no permite eliminar mensajes; solo lineas Evolution."); }
        if (line.Status != WhatsAppLineStatus.Connected) { return new LineSendResult(false, "La linea no esta conectada."); }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is null) { return new LineSendResult(false, "No hay servidor Evolution configurado."); }
        var (baseUrl, apiKey) = server.Value;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        var remoteJid = $"{digits}@s.whatsapp.net";
        var result = await _client.DeleteMessageForEveryoneAsync(baseUrl, apiKey, EvoInstance(line), remoteJid, messageId, fromMe: true, cancellationToken);
        return new LineSendResult(result.Ok, result.Error);
    }

    // Resuelve linea conectada + numero normalizado (agnostico de proveedor). Error no nulo si algo falta.
    private async Task<(string? Error, WhatsAppLine? Line, string Digits)> ReadyAsync(Guid lineId, string phone, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone)) { return ("Indica el numero.", null, ""); }
        // IgnoreQueryFilters: el agente (despachador disparado por webhook) envia adjuntos/ubicacion sin tenant en sesion.
        var line = await _db.WhatsAppLines.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line is null) { return ("La linea no existe.", null, ""); }
        if (line.Status != WhatsAppLineStatus.Connected) { return ("La linea no esta conectada.", null, ""); }
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return (null, line, digits);
    }

    // Credenciales Cloud de la linea (phone_number_id + token descifrado), o null si faltan.
    private (string phoneNumberId, string token)? CloudCreds(WhatsAppLine line)
    {
        if (string.IsNullOrWhiteSpace(line.CloudPhoneNumberId) || string.IsNullOrWhiteSpace(line.CloudAccessTokenEncrypted)) { return null; }
        try { return (line.CloudPhoneNumberId!, _secretProtector.Unprotect(line.CloudAccessTokenEncrypted!)); }
        catch { return null; }
    }

    // API key YCloud de la linea, descifrada, o null si falta / no se puede descifrar.
    private string? YCloudApiKey(WhatsAppLine line)
    {
        if (string.IsNullOrWhiteSpace(line.YCloudApiKeyEncrypted)) { return null; }
        try { return _secretProtector.Unprotect(line.YCloudApiKeyEncrypted!); }
        catch { return null; }
    }

    public async Task<int> ApplyWebhookToConnectedLinesAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var (webhookUrl, webhookToken) = await EffectiveWebhookAsync(cancellationToken);
        if (webhookUrl is null || webhookToken is null) { return 0; }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is null) { return 0; }
        var (baseUrl, apiKey) = server.Value;

        // Solo lineas Evolution: las Cloud usan el webhook a nivel de App de Meta (no por instancia).
        var lines = await _db.WhatsAppLines.Where(l => l.Status == WhatsAppLineStatus.Connected && l.Provider == WhatsAppProvider.Evolution).ToListAsync(cancellationToken);
        var applied = 0;
        foreach (var line in lines)
        {
            var res = await _client.SetWebhookAsync(baseUrl, apiKey, EvoInstance(line), webhookUrl, webhookToken, cancellationToken);
            if (res.Ok) { applied++; }
        }
        return applied;
    }

    // URL + token efectivos del webhook segun el modo configurado (dev usa la URL activa del tunel).
    private async Task<(string? Url, string? Token)> EffectiveWebhookAsync(CancellationToken ct)
    {
        var master = await _db.EvolutionMasterConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (master is null || string.IsNullOrWhiteSpace(master.WebhookToken)) { return (null, null); }
        var baseUrl = string.Equals(master.WebhookMode, "Production", StringComparison.OrdinalIgnoreCase)
            ? master.WebhookPublicUrl
            : master.WebhookActiveUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) { return (null, null); }
        return ($"{baseUrl!.TrimEnd('/')}/webhooks/evolution", master.WebhookToken);
    }

    // Servidor efectivo (URL + API key descifrada) segun la eleccion del tenant.
    private async Task<(string baseUrl, string apiKey)?> ResolveServerAsync(CancellationToken ct)
    {
        var cfg = await _db.TenantEvolutionConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg is not null && !cfg.UseMasterServer)
        {
            if (string.IsNullOrWhiteSpace(cfg.BaseUrl) || string.IsNullOrWhiteSpace(cfg.ApiTokenEncrypted)) { return null; }
            return (cfg.BaseUrl!, _secretProtector.Unprotect(cfg.ApiTokenEncrypted!));
        }
        var master = await _db.EvolutionMasterConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (master is null || string.IsNullOrWhiteSpace(master.BaseUrl) || string.IsNullOrWhiteSpace(master.ApiKeyEncrypted)) { return null; }
        return (master.BaseUrl!, _secretProtector.Unprotect(master.ApiKeyEncrypted!));
    }

    // Nombre de instancia unico en el servidor compartido: ecorex_<tenant>_<linea>.
    private static string EvoInstance(WhatsAppLine line) => $"ecorex_{line.TenantId:N}_{line.Id:N}";

    private static string? NormalizeBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var url = raw.Trim().TrimEnd('/');
        if (url.EndsWith("/manager", StringComparison.OrdinalIgnoreCase)) { url = url[..^"/manager".Length]; }
        return url.TrimEnd('/');
    }

    private string Mask(string encrypted)
    {
        string value;
        try { value = _secretProtector.Unprotect(encrypted); }
        catch { return "(re-ingresar)"; }
        return value.Length <= 4 ? "****" : $"{new string('*', Math.Min(value.Length - 4, 8))}{value[^4..]}";
    }

    private static WhatsAppLineDto Map(WhatsAppLine l) =>
        new(l.Id, l.InstanceName, l.PhoneNumber, l.Status, l.AssignedToTenantUserId, l.LastConnectedAt, l.LastStatusAt,
            l.Provider, l.CloudPhoneNumberId, l.CloudBusinessAccountId, !string.IsNullOrEmpty(l.CloudAccessTokenEncrypted),
            l.YCloudPhoneNumberId, l.YCloudWabaId, !string.IsNullOrEmpty(l.YCloudApiKeyEncrypted));
}
