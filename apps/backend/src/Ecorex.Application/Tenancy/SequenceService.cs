using System.Globalization;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Implementacion portable del consecutivo por tenant (ADR-0013). Estrategia: UPDATE
/// condicional atomico (compare-and-swap) con retry via ExecuteUpdateAsync, sin SQL crudo.
/// Bajo READ COMMITTED en ambos motores, el UPDATE del perdedor de la carrera bloquea hasta
/// el commit del ganador, re-evalua el predicado (next_value cambio), afecta 0 filas y el
/// loop reintenta con el valor fresco. Esto serializa la emision sobre la fila del
/// consecutivo sin duplicar numeros.
/// </summary>
public sealed class SequenceService : ISequenceService
{
    private const int MaxAttempts = 50;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public SequenceService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task EnsureSequenceAsync(string code, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("No hay tenant activo para emitir consecutivos.");
        }

        // El filtro global por tenant acota la consulta al tenant activo.
        if (await _db.TenantSequences.AsNoTracking().AnyAsync(s => s.Code == code, cancellationToken))
        {
            return;
        }

        var sequence = new TenantSequence { TenantId = tenantId, Code = code, NextValue = 1 };
        _db.TenantSequences.Add(sequence);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Otro proceso creo la fila primero (indice unico TenantId+Code): la carrera es
            // benigna. Se desengancha la entidad fallida para no envenenar el contexto.
            _db.TenantSequences.Entry(sequence).State = EntityState.Detached;
        }
    }

    public async Task<string> NextAsync(string code, string prefix, int padding, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            throw new InvalidOperationException("No hay tenant activo para emitir consecutivos.");
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var sequence = await _db.TenantSequences.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Code == code, cancellationToken);
            if (sequence is null)
            {
                // Fallback para llamadores que no aseguraron la fila. OJO: dentro de una
                // transaccion PostgreSQL una violacion de unicidad la abortaria; por eso
                // los casos de uso llaman EnsureSequenceAsync ANTES de abrir la transaccion.
                await EnsureSequenceAsync(code, cancellationToken);
                continue;
            }

            var current = sequence.NextValue;
            // CAS: solo incrementa si nadie lo movio desde la lectura. ExecuteUpdate corre
            // dentro de la transaccion ambiente del caso de uso (misma conexion del contexto).
            var updated = await _db.TenantSequences
                .Where(s => s.Id == sequence.Id && s.NextValue == current)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.NextValue, current + 1), cancellationToken);
            if (updated == 1)
            {
                return prefix + current.ToString(CultureInfo.InvariantCulture).PadLeft(padding, '0');
            }
        }

        throw new InvalidOperationException(
            $"No fue posible emitir el consecutivo '{code}' tras {MaxAttempts} intentos (contencion excesiva).");
    }

    public async Task<long> PeekAsync(string code, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            throw new InvalidOperationException("No hay tenant activo para leer consecutivos.");
        }
        // Filtro global por tenant. 0 => la fila aun no existe (nunca se ha emitido).
        return await _db.TenantSequences.AsNoTracking()
            .Where(s => s.Code == code)
            .Select(s => (long?)s.NextValue)
            .FirstOrDefaultAsync(cancellationToken) ?? 0L;
    }

    public async Task SetNextAsync(string code, long value, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            throw new InvalidOperationException("No hay tenant activo para fijar consecutivos.");
        }
        if (value < 1) { value = 1; }
        await EnsureSequenceAsync(code, cancellationToken);
        // UPDATE portable (sin SQL crudo), acotado por code; el filtro global limita al tenant activo.
        await _db.TenantSequences
            .Where(s => s.Code == code)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.NextValue, value), cancellationToken);
    }
}
