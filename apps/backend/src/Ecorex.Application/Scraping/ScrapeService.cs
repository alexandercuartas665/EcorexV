using System.Diagnostics;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Scraping;

public sealed class ScrapeService : IScrapeService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IScrapeFetcher _fetcher;
    private readonly ScrapeGuardOptions _options;
    private readonly TimeProvider _timeProvider;

    public ScrapeService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        IScrapeFetcher fetcher,
        ScrapeGuardOptions options,
        TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _fetcher = fetcher;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ScrapeSourceDto>> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        var since30d = _timeProvider.GetUtcNow().AddDays(-30);
        return await _db.ScrapeSources
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new ScrapeSourceDto(
                s.Id, s.Name, s.Url, s.Selector, s.Kind, s.Status, s.LastRunAt, s.LastResultSummary,
                _db.ScrapeRuns.Count(r => r.SourceId == s.Id),
                _db.ScrapeRuns.Count(r => r.SourceId == s.Id && r.CreatedAt >= since30d && r.Status == ScrapeRunStatus.Success),
                _db.ScrapeRuns.Count(r => r.SourceId == s.Id && r.CreatedAt >= since30d),
                _db.ScrapeRuns.Where(r => r.SourceId == s.Id).Sum(r => (long)r.ItemCount),
                s.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ScrapeSourceDto?> GetSourceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var since30d = _timeProvider.GetUtcNow().AddDays(-30);
        return await _db.ScrapeSources
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new ScrapeSourceDto(
                s.Id, s.Name, s.Url, s.Selector, s.Kind, s.Status, s.LastRunAt, s.LastResultSummary,
                _db.ScrapeRuns.Count(r => r.SourceId == s.Id),
                _db.ScrapeRuns.Count(r => r.SourceId == s.Id && r.CreatedAt >= since30d && r.Status == ScrapeRunStatus.Success),
                _db.ScrapeRuns.Count(r => r.SourceId == s.Id && r.CreatedAt >= since30d),
                _db.ScrapeRuns.Where(r => r.SourceId == s.Id).Sum(r => (long)r.ItemCount),
                s.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ScrapeOpResult<ScrapeSourceDto>> SaveSourceAsync(
        SaveScrapeSourceRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return ScrapeOpResult<ScrapeSourceDto>.Invalid("No hay un tenant activo en la sesion.");
        }

        var name = (request.Name ?? "").Trim();
        if (name.Length == 0)
        {
            return ScrapeOpResult<ScrapeSourceDto>.Invalid("El nombre de la fuente es obligatorio.");
        }
        if (name.Length > 200)
        {
            return ScrapeOpResult<ScrapeSourceDto>.Invalid("El nombre supera los 200 caracteres.");
        }

        // Validacion sintactica de la URL al guardar (esquema, credenciales embebidas).
        // El guard COMPLETO (DNS + rangos privados + puertos) corre en CADA ejecucion:
        // validar solo al guardar dejaria pasar cambios de DNS posteriores.
        var url = (request.Url ?? "").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return ScrapeOpResult<ScrapeSourceDto>.Invalid("La URL debe ser absoluta y usar http o https.");
        }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return ScrapeOpResult<ScrapeSourceDto>.Invalid("URLs con credenciales embebidas (user@host) no estan permitidas.");
        }
        if (url.Length > 1000)
        {
            return ScrapeOpResult<ScrapeSourceDto>.Invalid("La URL supera los 1000 caracteres.");
        }

        var selector = string.IsNullOrWhiteSpace(request.Selector) ? null : request.Selector.Trim();
        if (request.Kind == ScrapeSourceKind.Html && selector is null)
        {
            return ScrapeOpResult<ScrapeSourceDto>.Invalid("Las fuentes HTML requieren un selector CSS (ej. article.product-card h3).");
        }
        if (selector is { Length: > 300 })
        {
            return ScrapeOpResult<ScrapeSourceDto>.Invalid("El selector supera los 300 caracteres.");
        }

        var duplicated = await _db.ScrapeSources
            .AnyAsync(s => s.Name == name && s.Id != request.Id, cancellationToken);
        if (duplicated)
        {
            return ScrapeOpResult<ScrapeSourceDto>.Invalid($"Ya existe una fuente llamada \"{name}\".");
        }

        ScrapeSource source;
        if (request.Id is Guid id)
        {
            var existing = await _db.ScrapeSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (existing is null)
            {
                return ScrapeOpResult<ScrapeSourceDto>.NotFound();
            }
            source = existing;
        }
        else
        {
            source = new ScrapeSource { TenantId = tenantId };
            _db.ScrapeSources.Add(source);
        }

        source.Name = name;
        source.Url = url;
        source.Kind = request.Kind;
        source.Selector = request.Kind == ScrapeSourceKind.Html ? selector : null;
        source.Status = request.Status;
        await _db.SaveChangesAsync(cancellationToken);

        var dto = await GetSourceAsync(source.Id, cancellationToken);
        return ScrapeOpResult<ScrapeSourceDto>.Ok(dto!);
    }

    public async Task<ScrapeOpResult<bool>> DeleteSourceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = await _db.ScrapeSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null)
        {
            return ScrapeOpResult<bool>.NotFound();
        }

        var hasRuns = await _db.ScrapeRuns.AnyAsync(r => r.SourceId == id, cancellationToken);
        if (hasRuns)
        {
            return ScrapeOpResult<bool>.Invalid(
                "La fuente tiene corridas en el historial; desactivala en lugar de eliminarla.");
        }

        _db.ScrapeSources.Remove(source);
        await _db.SaveChangesAsync(cancellationToken);
        return ScrapeOpResult<bool>.Ok(true);
    }

    public async Task<IReadOnlyList<ScrapeRunDto>> ListRunsAsync(
        Guid sourceId, int take = 10, CancellationToken cancellationToken = default)
    {
        return await _db.ScrapeRuns
            .AsNoTracking()
            .Where(r => r.SourceId == sourceId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .Select(r => new ScrapeRunDto(
                r.Id, r.SourceId, r.Status, r.ItemCount, r.DurationMs, r.ErrorMessage, r.ResultJson, r.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ScrapeOpResult<ScrapeRunDto>> RunAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return ScrapeOpResult<ScrapeRunDto>.Invalid("No hay un tenant activo en la sesion.");
        }

        // El filtro global por tenant oculta las fuentes ajenas: correr la fuente de otro
        // tenant es imposible por construccion (NotFound).
        var source = await _db.ScrapeSources.FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken);
        if (source is null)
        {
            return ScrapeOpResult<ScrapeRunDto>.NotFound();
        }

        var stopwatch = Stopwatch.StartNew();
        string? errorMessage = null;
        string? resultJson = null;
        var itemCount = 0;

        var fetch = await _fetcher.FetchAsync(source.Url, cancellationToken);
        if (!fetch.Ok)
        {
            errorMessage = fetch.Error;
        }
        else
        {
            var parse = source.Kind == ScrapeSourceKind.Json
                ? ScrapeContentParser.ParseJson(fetch.Body!, _options.MaxPreviewItems)
                : ScrapeContentParser.ParseHtml(fetch.Body!, source.Selector, _options.MaxPreviewItems);
            if (!parse.Ok)
            {
                errorMessage = parse.Error;
            }
            else
            {
                itemCount = parse.ItemCount;
                resultJson = ScrapeContentParser.BuildResultJson(parse, _options.MaxResultJsonBytes);
            }
        }

        stopwatch.Stop();
        var now = _timeProvider.GetUtcNow();
        var failed = errorMessage is not null;

        var run = new ScrapeRun
        {
            TenantId = tenantId,
            SourceId = source.Id,
            Status = failed ? ScrapeRunStatus.Failed : ScrapeRunStatus.Success,
            ItemCount = itemCount,
            DurationMs = (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue),
            ErrorMessage = errorMessage is { Length: > 1000 } ? errorMessage[..1000] : errorMessage,
            ResultJson = resultJson
        };
        _db.ScrapeRuns.Add(run);

        source.LastRunAt = now;
        source.LastResultSummary = failed
            ? Truncate($"Fallo: {errorMessage}", 400)
            : $"{itemCount} items en {run.DurationMs} ms";
        // El fallo marca la fuente en Error; una corrida exitosa la devuelve a Active.
        // Inactive es una decision del usuario y no se pisa.
        if (failed)
        {
            source.Status = ScrapeSourceStatus.Error;
        }
        else if (source.Status == ScrapeSourceStatus.Error)
        {
            source.Status = ScrapeSourceStatus.Active;
        }

        // REGLA 4 (CLAUDE.md): corrida + actualizacion de la fuente en UNA transaccion.
        await using (var transaction = await _db.BeginTransactionAsync(cancellationToken))
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return ScrapeOpResult<ScrapeRunDto>.Ok(new ScrapeRunDto(
            run.Id, run.SourceId, run.Status, run.ItemCount, run.DurationMs,
            run.ErrorMessage, run.ResultJson, run.CreatedAt == default ? now : run.CreatedAt));
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
