using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Scheduling;

/// <inheritdoc />
public sealed class ScheduledJobService : IScheduledJobService
{
    private const string SequenceCode = "PAC"; // Programacion de ACtividades (consecutivo del origen).

    private readonly IApplicationDbContext _db;
    private readonly ISequenceService _sequences;

    public ScheduledJobService(IApplicationDbContext db, ISequenceService sequences)
    {
        _db = db;
        _sequences = sequences;
    }

    public async Task<IReadOnlyList<ScheduledJobListItemDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await _db.ScheduledJobs
            .Include(j => j.Rules.OrderBy(r => r.SortOrder))
            .Include(j => j.Channels)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

        // Nombres de categoria/subcategoria (solo Activity) para la sub-etiqueta de la fila.
        var catIds = jobs.Where(j => j.CategoryId is not null).Select(j => j.CategoryId!.Value).Distinct().ToList();
        var subIds = jobs.Where(j => j.SubcategoryId is not null).Select(j => j.SubcategoryId!.Value).Distinct().ToList();
        var cats = catIds.Count == 0 ? new Dictionary<Guid, string>() : await _db.ActividadCategorias
            .Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Nombre, cancellationToken);
        var subs = subIds.Count == 0 ? new Dictionary<Guid, string>() : await _db.ActividadSubcategorias
            .Where(s => subIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Nombre, cancellationToken);

        return jobs.Select(j => new ScheduledJobListItemDto(
            j.Id, j.Code, j.Type, j.Name,
            SubLabel: j.Type == ScheduledJobType.Activity
                ? $"{Lookup(cats, j.CategoryId)} - {Lookup(subs, j.SubcategoryId)}".Trim(' ', '-')
                : null,
            RuleSummary: ListSummary(j.Rules),
            Channels: j.Channels.Select(c => ChannelLabel(c.Channel)).ToList(),
            Status: j.Status)).ToList();
    }

    public async Task<ScheduledJobDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var j = await _db.ScheduledJobs
            .Include(x => x.Rules.OrderBy(r => r.SortOrder))
            .Include(x => x.Channels)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (j is null) { return null; }

        return new ScheduledJobDetailDto(
            j.Id, j.Code, j.Name, j.Type, j.Status, j.CategoryId, j.SubcategoryId,
            j.AssigneeTenantUserId, j.Version,
            j.Rules.OrderBy(r => r.SortOrder).Select(ToRuleDto).ToList(),
            j.Channels.Select(c => c.Channel).ToList());
    }

    public async Task<ScheduledJobSaveResult> SaveAsync(Guid? id, SaveScheduledJobRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0) { return ScheduledJobSaveResult.Fail("El nombre es obligatorio."); }
        if (request.Rules.Count == 0) { return ScheduledJobSaveResult.Fail("Agrega al menos una regla de ejecucion."); }
        if (request.Channels.Count == 0) { return ScheduledJobSaveResult.Fail("Elige al menos un canal de transmision."); }
        if (request.Type == ScheduledJobType.Activity && (request.CategoryId is null || request.SubcategoryId is null))
        {
            return ScheduledJobSaveResult.Fail("Selecciona la categoria y sub-categoria de la actividad.");
        }

        var catId = request.Type == ScheduledJobType.Activity ? request.CategoryId : null;
        var subId = request.Type == ScheduledJobType.Activity ? request.SubcategoryId : null;

        if (id is null)
        {
            // Consecutivo PAC: Ensure fuera de la transaccion (patron ISequenceService), Next dentro.
            await _sequences.EnsureSequenceAsync(SequenceCode, cancellationToken);
            await using var tx = await _db.BeginTransactionAsync(cancellationToken);
            var code = await _sequences.NextAsync(SequenceCode, SequenceCode + "-", 6, cancellationToken);
            var job = new ScheduledJob
            {
                Code = code,
                Name = name,
                Type = request.Type,
                Status = ScheduledJobStatus.Active,
                Priority = ScheduledJobPriority.Normal,
                CategoryId = catId,
                SubcategoryId = subId,
                AssigneeTenantUserId = request.AssigneeTenantUserId,
            };
            ApplyRules(job, request.Rules);
            ApplyChannels(job, request.Channels);
            _db.ScheduledJobs.Add(job);
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return ScheduledJobSaveResult.Ok(job.Id);
        }

        var existing = await _db.ScheduledJobs
            .Include(x => x.Rules)
            .Include(x => x.Channels)
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
        if (existing is null) { return ScheduledJobSaveResult.Fail("La programacion ya no existe."); }

        // Concurrencia optimista (ADR-0013): EF usa el valor ORIGINAL en el WHERE del UPDATE.
        _db.ScheduledJobs.Entry(existing).Property(x => x.Version).OriginalValue = request.Version;

        existing.Name = name;
        existing.Type = request.Type;
        existing.CategoryId = catId;
        existing.SubcategoryId = subId;
        existing.AssigneeTenantUserId = request.AssigneeTenantUserId;

        // Reemplazo TOTAL de reglas y canales. Se opera sobre los DbSet y NUNCA sobre las
        // colecciones de navegacion del padre: vaciar la nav de una relacion con cascada marca los
        // hijos como HUERFANOS y EF emite un SEGUNDO DELETE sobre filas que RemoveRange ya borro
        // -> "affected 0 rows" -> DbUpdateConcurrencyException espuria (bug real cazado por los tests).
        _db.ScheduledJobRules.RemoveRange(existing.Rules.ToList());
        _db.ScheduledJobChannels.RemoveRange(existing.Channels.ToList());
        var order = 0;
        foreach (var r in request.Rules)
        {
            _db.ScheduledJobRules.Add(NewRule(existing.Id, r, order++));
        }
        foreach (var ch in request.Channels.Distinct())
        {
            _db.ScheduledJobChannels.Add(new ScheduledJobChannel { JobId = existing.Id, Channel = ch });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ScheduledJobSaveResult.Fail("La programacion cambio en otra sesion; recarga y reintenta.");
        }
        return ScheduledJobSaveResult.Ok(existing.Id);
    }

    public async Task ToggleStatusAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await _db.ScheduledJobs.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (job is null) { return; }
        job.Status = job.Status == ScheduledJobStatus.Active ? ScheduledJobStatus.Paused : ScheduledJobStatus.Active;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await _db.ScheduledJobs
            .Include(x => x.Rules)
            .Include(x => x.Channels)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (job is null) { return; }
        _db.ScheduledJobRules.RemoveRange(job.Rules);
        _db.ScheduledJobChannels.RemoveRange(job.Channels);
        _db.ScheduledJobs.Remove(job);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduledJobCategoryDto>> GetConceptCatalogAsync(CancellationToken cancellationToken = default)
    {
        var cats = await _db.ActividadCategorias
            .Where(c => !c.IsArchived)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Nombre)
            .Select(c => new { c.Id, c.Nombre })
            .ToListAsync(cancellationToken);
        var subs = await _db.ActividadSubcategorias
            .Where(s => !s.IsArchived)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Nombre)
            .Select(s => new { s.Id, s.CategoriaId, s.Nombre })
            .ToListAsync(cancellationToken);

        return cats.Select(c => new ScheduledJobCategoryDto(
            c.Id, c.Nombre,
            subs.Where(s => s.CategoriaId == c.Id)
                .Select(s => new ScheduledJobSubcategoryDto(s.Id, s.Nombre)).ToList())).ToList();
    }

    // ---- helpers ----

    private static string Lookup(Dictionary<Guid, string> map, Guid? id)
        => id is Guid g && map.TryGetValue(g, out var v) ? v : "";

    /// <summary>Normaliza una regla del request a la entidad (limpia los campos que no aplican a la frecuencia).</summary>
    private static ScheduledJobRule NewRule(Guid jobId, ScheduledJobRuleDto r, int sortOrder) => new()
    {
        JobId = jobId,
        SortOrder = sortOrder,
        Frequency = r.Frequency,
        IntervalNum = r.IntervalNum < 1 ? 1 : r.IntervalNum,
        Weekdays = r.Frequency == ScheduledJobFrequency.Weekly ? NullIfEmpty(r.Weekdays) : null,
        MonthOrdinal = r.Frequency == ScheduledJobFrequency.Monthly ? NullIfEmpty(r.MonthOrdinal) : null,
        MonthWeekday = r.Frequency == ScheduledJobFrequency.Monthly ? NullIfEmpty(r.MonthWeekday) : null,
        DayOfMonth = r.Frequency == ScheduledJobFrequency.Monthly ? r.DayOfMonth : null,
        AtTime = NullIfEmpty(r.AtTime),
        RepeatIntraday = r.Frequency != ScheduledJobFrequency.Once && r.RepeatIntraday,
        RepeatEveryHours = r.RepeatIntraday ? r.RepeatEveryHours : null,
        RepeatFrom = r.RepeatIntraday ? NullIfEmpty(r.RepeatFrom) : null,
        RepeatTo = r.RepeatIntraday ? NullIfEmpty(r.RepeatTo) : null,
        ValidFrom = r.ValidFrom,
        ValidTo = r.ValidTo,
        Description = NullIfEmpty(r.Description),
    };

    /// <summary>Solo en CREAR: el padre esta Added, asi que las navs son la via natural del grafo.</summary>
    private static void ApplyRules(ScheduledJob job, IReadOnlyList<ScheduledJobRuleDto> rules)
    {
        var i = 0;
        foreach (var r in rules)
        {
            job.Rules.Add(NewRule(job.Id, r, i++));
        }
    }

    private static void ApplyChannels(ScheduledJob job, IReadOnlyList<ScheduledJobChannelType> channels)
    {
        foreach (var ch in channels.Distinct())
        {
            job.Channels.Add(new ScheduledJobChannel { JobId = job.Id, Channel = ch });
        }
    }

    private static ScheduledJobRuleDto ToRuleDto(ScheduledJobRule r) => new(
        r.Frequency, r.IntervalNum, r.Weekdays, r.MonthOrdinal, r.MonthWeekday, r.DayOfMonth,
        r.AtTime, r.RepeatIntraday, r.RepeatEveryHours, r.RepeatFrom, r.RepeatTo,
        r.ValidFrom, r.ValidTo, r.Description);

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Etiqueta visible del canal (el enum es tecnico; el prototipo muestra "Correo"/"SMS").</summary>
    private static string ChannelLabel(ScheduledJobChannelType c) => c switch
    {
        ScheduledJobChannelType.Email => "Correo",
        ScheduledJobChannelType.WhatsApp => "WhatsApp",
        ScheduledJobChannelType.Slack => "Slack",
        ScheduledJobChannelType.Sms => "SMS",
        _ => c.ToString(),
    };

    /// <summary>Resumen corto de las reglas para la columna REGLA (1 regla -> detalle; N -> "N reglas").</summary>
    private static string ListSummary(ICollection<ScheduledJobRule> rules)
    {
        if (rules.Count == 0) { return "Sin reglas"; }
        if (rules.Count > 1) { return $"{rules.Count} reglas"; }
        return SummarizeRule(rules.First());
    }

    private static string SummarizeRule(ScheduledJobRule r)
    {
        string baseS = r.Frequency switch
        {
            ScheduledJobFrequency.Once => $"Una vez el {r.ValidFrom?.ToString("dd/MM/yyyy") ?? "-"} a las {r.AtTime ?? "-"}",
            ScheduledJobFrequency.Daily => $"Cada {r.IntervalNum} dia(s)",
            ScheduledJobFrequency.Weekly => $"Cada {r.IntervalNum} sem. los {r.Weekdays}",
            ScheduledJobFrequency.Monthly => $"El {(r.MonthOrdinal ?? "").ToLowerInvariant()} {r.MonthWeekday} cada {r.IntervalNum} mes(es)",
            _ => "",
        };
        if (r.Frequency != ScheduledJobFrequency.Once)
        {
            baseS += r.RepeatIntraday
                ? $" - cada {r.RepeatEveryHours}h de {r.RepeatFrom} a {r.RepeatTo}"
                : $" a las {r.AtTime}";
        }
        return baseS;
    }
}
