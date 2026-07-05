using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed partial class ContactLoaderService : IContactLoaderService
{
    /// <summary>Minimo de digitos para considerar un telefono comparable/valido.</summary>
    private const int MinPhoneDigits = 7;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public ContactLoaderService(IApplicationDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<ContactImportPreview> ValidateAsync(
        CsvTable table, ContactColumnMapping mapping, CancellationToken cancellationToken = default)
    {
        var rows = await EvaluateRowsAsync(table, mapping, cancellationToken);
        return new ContactImportPreview(
            rows.Count,
            rows.Count(r => r.Result.Status == ContactRowStatus.Valid),
            rows.Count(r => r.Result.Status == ContactRowStatus.Duplicate),
            rows.Count(r => r.Result.Status == ContactRowStatus.Invalid),
            rows.Select(r => r.Result).ToList());
    }

    public async Task<ContactImportResult?> ImportAsync(
        string fileName, CsvTable table, ContactColumnMapping mapping, Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId || mapping.Name is null)
        {
            return null;
        }

        // Etapa de entrada: la primera del embudo (menor SortOrder). Sin etapas no hay carga.
        var stage = await _db.PipelineStages
            .OrderBy(s => s.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);
        if (stage is null)
        {
            return null;
        }

        // Los leads importados quedan asignados al importador (mismo criterio que
        // LeadService.CreateAsync: un asesor con visibilidad OwnOnly los sigue viendo).
        Guid? assignedTo = null;
        if (_tenantContext.UserId is Guid importerUserId)
        {
            assignedTo = await _db.TenantUsers
                .Where(tu => tu.PlatformUserId == importerUserId)
                .Select(tu => (Guid?)tu.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var rows = await EvaluateRowsAsync(table, mapping, cancellationToken);
        var now = _timeProvider.GetUtcNow();

        // REGLA 4 (CLAUDE.md): operacion multi-tabla (Leads + LeadActivities + historial)
        // en UNA transaccion; si algo falla no queda carga a medias.
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var inserted = 0;
        foreach (var row in rows.Where(r => r.Result.Status == ContactRowStatus.Valid))
        {
            var fieldValues = new Dictionary<string, string?>();
            if (!string.IsNullOrWhiteSpace(row.Email))
            {
                fieldValues["email"] = row.Email;
            }

            if (!string.IsNullOrWhiteSpace(row.Company))
            {
                fieldValues["empresa"] = row.Company;
            }

            var lead = new Lead
            {
                TenantId = tenantId,
                ContactName = row.Result.Name!,
                ContactPhone = string.IsNullOrWhiteSpace(row.Result.Phone) ? null : row.Result.Phone,
                Destination = string.IsNullOrWhiteSpace(row.Destination) ? null : row.Destination,
                EstimatedValue = row.EstimatedValue,
                StageId = stage.Id,
                Status = LeadStatus.Open,
                StageChangedAt = now,
                AssignedToTenantUserId = assignedTo,
                FieldValuesJson = fieldValues.Count == 0 ? null : JsonSerializer.Serialize(fieldValues)
            };
            _db.Leads.Add(lead);
            _db.LeadActivities.Add(new LeadActivity
            {
                TenantId = tenantId,
                LeadId = lead.Id,
                ActivityType = "lead.imported",
                Description = $"Importado desde {fileName} (fila {row.Result.LineNumber}) a la etapa {stage.Name}"
            });
            inserted++;
        }

        var batch = new ContactImportBatch
        {
            TenantId = tenantId,
            FileName = fileName,
            TotalRows = rows.Count,
            Inserted = inserted,
            Duplicates = rows.Count(r => r.Result.Status == ContactRowStatus.Duplicate),
            Invalid = rows.Count(r => r.Result.Status == ContactRowStatus.Invalid)
        };
        _db.ContactImportBatches.Add(batch);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ContactImportResult(
            batch.Id, batch.TotalRows, batch.Inserted, batch.Duplicates, batch.Invalid,
            rows.Select(r => r.Result).ToList());
    }

    public async Task<IReadOnlyList<ContactImportBatchDto>> ListBatchesAsync(
        int take = 20, CancellationToken cancellationToken = default)
    {
        return await _db.ContactImportBatches
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAt)
            .Take(take)
            .Select(b => new ContactImportBatchDto(
                b.Id, b.FileName, b.TotalRows, b.Inserted, b.Duplicates, b.Invalid, b.CreatedAt,
                _db.PlatformUsers.Where(p => p.Id == b.CreatedBy)
                    .Select(p => p.DisplayName ?? p.Email).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    // =========================================================================
    // Validacion por fila (compartida por previsualizacion y carga)
    // =========================================================================

    private sealed record EvaluatedRow(
        ContactRowResult Result,
        string? Company,
        string? Destination,
        decimal? EstimatedValue,
        string? Email);

    private async Task<List<EvaluatedRow>> EvaluateRowsAsync(
        CsvTable table, ContactColumnMapping mapping, CancellationToken cancellationToken)
    {
        // Claves de duplicado existentes en el tenant: telefonos (solo digitos) de todos los
        // leads (activos y archivados) + emails guardados en los campos configurables. El
        // filtro global por tenant hace imposible comparar contra otros tenants.
        var existing = await _db.Leads
            .AsNoTracking()
            .Select(l => new { l.ContactPhone, l.FieldValuesJson })
            .ToListAsync(cancellationToken);

        var knownPhones = new HashSet<string>(StringComparer.Ordinal);
        var knownEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lead in existing)
        {
            var digits = PhoneDigits(lead.ContactPhone);
            if (digits.Length >= MinPhoneDigits)
            {
                knownPhones.Add(digits);
            }

            var email = EmailFromFieldValues(lead.FieldValuesJson);
            if (!string.IsNullOrWhiteSpace(email))
            {
                knownEmails.Add(email.Trim());
            }
        }

        var results = new List<EvaluatedRow>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            results.Add(EvaluateRow(row, mapping, knownPhones, knownEmails));
        }

        return results;
    }

    private static EvaluatedRow EvaluateRow(
        CsvRow row, ContactColumnMapping mapping,
        HashSet<string> knownPhones, HashSet<string> knownEmails)
    {
        var name = Cell(row, mapping.Name);
        var phone = Cell(row, mapping.Phone);
        var email = Cell(row, mapping.Email);
        var company = Cell(row, mapping.Company);
        var destination = Cell(row, mapping.Destination);
        var rawValue = Cell(row, mapping.EstimatedValue);

        ContactRowResult Fail(string message) =>
            new(row.LineNumber, name, phone, email, ContactRowStatus.Invalid, message);

        // --- Validaciones de forma ---
        if (string.IsNullOrWhiteSpace(name))
        {
            return new EvaluatedRow(Fail("El nombre del contacto esta vacio."), company, destination, null, email);
        }

        if (name.Length > 200)
        {
            return new EvaluatedRow(Fail("El nombre supera los 200 caracteres."), company, destination, null, email);
        }

        if (!string.IsNullOrWhiteSpace(email) && !EmailRegex().IsMatch(email))
        {
            return new EvaluatedRow(Fail($"Email invalido: {email}"), company, destination, null, email);
        }

        var phoneDigits = PhoneDigits(phone);
        if (!string.IsNullOrWhiteSpace(phone) && phoneDigits.Length < MinPhoneDigits)
        {
            return new EvaluatedRow(Fail($"Telefono invalido (menos de {MinPhoneDigits} digitos): {phone}"), company, destination, null, email);
        }

        decimal? estimatedValue = null;
        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            if (!TryParseMoney(rawValue, out var parsed))
            {
                return new EvaluatedRow(Fail($"Valor estimado no numerico: {rawValue}"), company, destination, null, email);
            }

            estimatedValue = parsed;
        }

        // --- Duplicados: contra los leads del tenant y contra filas anteriores del archivo.
        // Los sets se van alimentando, asi la SEGUNDA aparicion en el archivo es la duplicada.
        if (phoneDigits.Length >= MinPhoneDigits && !knownPhones.Add(phoneDigits))
        {
            return new EvaluatedRow(
                new ContactRowResult(row.LineNumber, name, phone, email, ContactRowStatus.Duplicate,
                    $"Ya existe un lead con el telefono {phone}."),
                company, destination, estimatedValue, email);
        }

        if (!string.IsNullOrWhiteSpace(email) && !knownEmails.Add(email.Trim()))
        {
            return new EvaluatedRow(
                new ContactRowResult(row.LineNumber, name, phone, email, ContactRowStatus.Duplicate,
                    $"Ya existe un lead con el email {email}."),
                company, destination, estimatedValue, email);
        }

        return new EvaluatedRow(
            new ContactRowResult(row.LineNumber, name, phone, email, ContactRowStatus.Valid, null),
            company, destination, estimatedValue, email);
    }

    private static string? Cell(CsvRow row, int? index) =>
        index is int i && i >= 0 && i < row.Fields.Count ? row.Fields[i].Trim() : null;

    /// <summary>
    /// Clave de comparacion de telefonos: solo digitos y, si hay mas de 10, los ULTIMOS 10.
    /// Asi "+57 300 123 4567" y "3001234567" cuentan como el mismo numero (prefijo de pais).
    /// </summary>
    private static string PhoneDigits(string? phone)
    {
        if (phone is null)
        {
            return "";
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length > 10 ? digits[^10..] : digits;
    }

    /// <summary>Acepta "3500000", "3.500.000", "3,500,000", "$ 3500000" y decimales con punto.</summary>
    private static bool TryParseMoney(string raw, out decimal value)
    {
        var cleaned = raw.Replace("$", "").Replace(" ", "");

        // Si hay coma y punto, el ultimo separador es el decimal; normalizamos a punto.
        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0)
        {
            cleaned = lastComma > lastDot
                ? cleaned.Replace(".", "").Replace(',', '.')
                : cleaned.Replace(",", "");
        }
        else if (lastComma >= 0)
        {
            // Solo comas: si parece separador de miles (grupos de 3) se eliminan; si no, decimal.
            var tail = cleaned.Length - lastComma - 1;
            cleaned = tail == 3 && cleaned.Count(c => c == ',') >= 1 && lastComma > 0
                ? cleaned.Replace(",", "")
                : cleaned.Replace(',', '.');
        }
        else if (lastDot >= 0)
        {
            var tail = cleaned.Length - lastDot - 1;
            if (tail == 3 && cleaned.Count(c => c == '.') >= 1)
            {
                cleaned = cleaned.Replace(".", "");
            }
        }

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static string? EmailFromFieldValues(string? fieldValuesJson)
    {
        if (string.IsNullOrWhiteSpace(fieldValuesJson))
        {
            return null;
        }

        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(fieldValuesJson);
            return values is not null && values.TryGetValue("email", out var email) ? email : null;
        }
        catch (JsonException)
        {
            return null; // Documento ajeno al formato esperado: no bloquea la carga.
        }
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
