using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.DataContainers;

/// <inheritdoc cref="IDataRelationLinkService"/>
public sealed class DataRelationLinkService : IDataRelationLinkService
{
    private readonly IApplicationDbContext _db;
    private readonly IDataContainerService _containers;

    public DataRelationLinkService(IApplicationDbContext db, IDataContainerService containers)
    {
        _db = db;
        _containers = containers;
    }

    public async Task<IReadOnlyList<RowRelationDto>> ListForRowAsync(
        Guid containerId, Guid? rowId, CancellationToken ct = default)
    {
        // Aristas donde ESTA tabla es el origen: son las que el editor de su fila puede fijar.
        var relations = await _db.DataModelRelations.AsNoTracking()
            .Where(r => r.FromTableId == containerId)
            .Select(r => new { r.Id, r.Name, r.Kind, r.ToTableId })
            .ToListAsync(ct);
        if (relations.Count == 0) { return Array.Empty<RowRelationDto>(); }

        var toTableIds = relations.Select(r => r.ToTableId).Distinct().ToList();
        var tableNames = await _db.DataContainers.AsNoTracking()
            .Where(c => toTableIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        // Vinculos actuales de la fila (si es nueva, no hay ninguno).
        var linksByRelation = new Dictionary<Guid, List<Guid>>();
        if (rowId is Guid rid)
        {
            var links = await _db.DataModelRelationLinks.AsNoTracking()
                .Where(l => l.FromRowId == rid)
                .Select(l => new { l.RelationId, l.ToRowId })
                .ToListAsync(ct);
            linksByRelation = links.GroupBy(l => l.RelationId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.ToRowId).ToList());
        }

        var result = new List<RowRelationDto>(relations.Count);
        foreach (var r in relations)
        {
            // Opciones del selector: las filas de la tabla destino con su etiqueta legible.
            var options = await _containers.ListRowOptionsAsync(r.ToTableId, ct: ct);
            result.Add(new RowRelationDto(
                RelationId: r.Id,
                Name: r.Name,
                Kind: r.Kind,
                ToTableId: r.ToTableId,
                ToTableName: tableNames.TryGetValue(r.ToTableId, out var n) ? n : "(tabla)",
                LinkedRowIds: linksByRelation.TryGetValue(r.Id, out var l) ? l : Array.Empty<Guid>(),
                Options: options));
        }
        return result;
    }

    public async Task<RelationLinkResult> SetLinksAsync(
        Guid relationId, Guid fromRowId, IReadOnlyList<Guid> toRowIds, CancellationToken ct = default)
    {
        var relation = await _db.DataModelRelations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == relationId, ct);
        if (relation is null) { return RelationLinkResult.NotFound("La relacion no existe."); }

        var wanted = toRowIds.Distinct().ToList();

        // La cardinalidad no la puede imponer el esquema (N:1 y N:N comparten tabla): se valida aqui.
        if (relation.Kind == DataModelRelationKind.ManyToOne && wanted.Count > 1)
        {
            return RelationLinkResult.Invalid(
                $"'{relation.Name ?? "La relacion"}' es N:1: admite un solo destino.");
        }

        // La fila origen debe ser de la tabla ORIGEN de la arista.
        var fromOk = await _db.DataContainerRows.AsNoTracking()
            .AnyAsync(r => r.Id == fromRowId && r.ContainerId == relation.FromTableId, ct);
        if (!fromOk) { return RelationLinkResult.NotFound("La fila de origen no existe en la tabla de la relacion."); }

        // Cada destino debe ser de la tabla DESTINO (y del tenant: lo garantiza el filtro global).
        if (wanted.Count > 0)
        {
            var validTargets = await _db.DataContainerRows.AsNoTracking()
                .Where(r => wanted.Contains(r.Id) && r.ContainerId == relation.ToTableId)
                .CountAsync(ct);
            if (validTargets != wanted.Count)
            {
                return RelationLinkResult.Invalid("Algun destino no pertenece a la tabla destino de la relacion.");
            }
        }

        // Reemplazo del set: se borra lo que sobra y se agrega lo que falta (idempotente).
        var current = await _db.DataModelRelationLinks
            .Where(l => l.RelationId == relationId && l.FromRowId == fromRowId)
            .ToListAsync(ct);

        var toRemove = current.Where(l => !wanted.Contains(l.ToRowId)).ToList();
        if (toRemove.Count > 0) { _db.DataModelRelationLinks.RemoveRange(toRemove); }

        var existing = current.Select(l => l.ToRowId).ToHashSet();
        foreach (var target in wanted.Where(t => !existing.Contains(t)))
        {
            _db.DataModelRelationLinks.Add(new DataModelRelationLink
            {
                Id = Guid.CreateVersion7(),
                RelationId = relationId,
                FromRowId = fromRowId,
                ToRowId = target
            });
        }

        await _db.SaveChangesAsync(ct);
        return RelationLinkResult.Ok();
    }
}
