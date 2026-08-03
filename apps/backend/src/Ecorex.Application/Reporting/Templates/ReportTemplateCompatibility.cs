using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Templates;

/// <summary>
/// Evalua si una <see cref="ReportTemplate"/> es activable en el TENANT ACTIVO (ADR-0062). La
/// comprobacion de contenedor se hace SIEMPRE contra el DbContext tenant-scoped (filtro global), asi
/// que el resultado es propio de cada tenant: la misma plantilla puede ser compatible en A y no en B.
/// </summary>
public static class ReportTemplateCompatibility
{
    public sealed record Result(bool Ok, string? Reason);

    /// <summary>
    /// Native -> siempre OK. Container -> OK solo si el tenant activo tiene un contenedor RAIZ cuyo
    /// nombre coincide con RequiredContainerName (coincidencia tolerante e insensible a mayusculas:
    /// el nombre del contenedor contiene el requerido, o viceversa). Si no, se rechaza con mensaje claro.
    /// </summary>
    public static async Task<Result> EvaluateAsync(IApplicationDbContext db, ReportTemplate template, CancellationToken ct = default)
    {
        if (template.RequiredSourceKind == ReportTemplateSourceKind.Native)
        {
            return new Result(true, null);
        }

        var required = template.RequiredContainerName?.Trim();
        if (string.IsNullOrWhiteSpace(required))
        {
            return new Result(false, "La plantilla requiere un contenedor de datos pero no especifica su nombre.");
        }

        // Nombres de los contenedores RAIZ del tenant activo (el filtro global limita a lo suyo). Son
        // pocos; el emparejamiento tolerante se resuelve en memoria.
        var rootNames = await db.DataContainers.AsNoTracking()
            .Where(c => c.ParentContainerId == null)
            .Select(c => c.Name)
            .ToListAsync(ct);

        var reqUpper = required.ToUpperInvariant();
        var matched = rootNames.Any(n =>
        {
            var u = (n ?? string.Empty).ToUpperInvariant();
            return u.Contains(reqUpper) || (u.Length >= 3 && reqUpper.Contains(u));
        });

        return matched
            ? new Result(true, null)
            : new Result(false, $"Requiere el contenedor \"{required}\", que este tenant no tiene cargado.");
    }
}
