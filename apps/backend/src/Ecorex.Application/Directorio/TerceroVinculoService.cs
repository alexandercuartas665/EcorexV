using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Implementacion de <see cref="ITerceroVinculoService"/> (Capa 8, regla 3.1). Combina los vinculos M:N
/// con cargo (TerceroVinculo) y el enlace primario legado (Tercero.EmpresaId) para el panel bidireccional.
/// Aislamiento por el filtro global (no se filtra a mano por TenantId; se estampa al crear).
/// </summary>
public sealed class TerceroVinculoService : ITerceroVinculoService
{
    private const int MaxBusqueda = 15;

    private readonly IApplicationDbContext _app;
    private readonly IDirectorioModularDbContext _db;
    private readonly ITenantContext _tenant;

    public TerceroVinculoService(IApplicationDbContext app, IDirectorioModularDbContext db, ITenantContext tenant)
    {
        _app = app;
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<TerceroVinculoDto>> ListDeAsync(Guid terceroId, CancellationToken cancellationToken = default)
    {
        var t = await _app.Terceros.AsNoTracking().FirstOrDefaultAsync(x => x.Id == terceroId, cancellationToken);
        if (t is null) { return Array.Empty<TerceroVinculoDto>(); }

        var vinculos = await _db.TerceroVinculos.AsNoTracking()
            .Where(v => v.PersonaId == terceroId || v.OrganizacionId == terceroId)
            .ToListAsync(cancellationToken);

        // Enlace primario legado: persona -> su empresa (EmpresaId); empresa -> sus contactos con EmpresaId.
        Guid? empresaLegado = t.Tipo == TerceroTipo.Persona ? t.EmpresaId : null;
        var personasLegado = t.Tipo == TerceroTipo.Empresa
            ? (await _app.Terceros.AsNoTracking()
                .Where(x => x.EmpresaId == terceroId && x.Estado != TerceroEstado.Inactivo)
                .Select(x => new { x.Id, x.Nombre, x.Cargo }).ToListAsync(cancellationToken))
            : new();

        // Nombres/tipos de todos los "otros" en un solo query.
        var otroIds = vinculos.Select(v => v.PersonaId == terceroId ? v.OrganizacionId : v.PersonaId).ToList();
        if (empresaLegado is Guid el) { otroIds.Add(el); }
        var infos = otroIds.Count == 0
            ? new Dictionary<Guid, (string Nombre, TerceroTipo Tipo)>()
            : (await _app.Terceros.AsNoTracking().Where(x => otroIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Nombre, x.Tipo }).ToListAsync(cancellationToken))
                .ToDictionary(x => x.Id, x => (x.Nombre, x.Tipo));

        var result = new List<TerceroVinculoDto>();
        var vistos = new HashSet<Guid>();

        foreach (var v in vinculos)
        {
            var otroId = v.PersonaId == terceroId ? v.OrganizacionId : v.PersonaId;
            if (!vistos.Add(otroId)) { continue; }
            var nombre = infos.TryGetValue(otroId, out var i) ? i.Nombre : "(desconocido)";
            var esEmpresa = infos.TryGetValue(otroId, out var j) && j.Tipo == TerceroTipo.Empresa;
            result.Add(new TerceroVinculoDto(v.Id, otroId, nombre, esEmpresa, v.Cargo, v.Principal, false));
        }

        if (empresaLegado is Guid emp && vistos.Add(emp))
        {
            var nombre = infos.TryGetValue(emp, out var i) ? i.Nombre : "(desconocido)";
            result.Add(new TerceroVinculoDto(null, emp, nombre, true, t.Cargo, true, true));
        }
        foreach (var p in personasLegado)
        {
            if (!vistos.Add(p.Id)) { continue; }
            result.Add(new TerceroVinculoDto(null, p.Id, p.Nombre, false, p.Cargo, true, true));
        }

        return result;
    }

    public async Task<string?> AgregarAsync(Guid personaId, Guid organizacionId, string? cargo, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return "No hay tenant activo."; }
        if (personaId == organizacionId) { return "No se puede vincular un tercero consigo mismo."; }

        var persona = await _app.Terceros.FirstOrDefaultAsync(x => x.Id == personaId, cancellationToken);
        var org = await _app.Terceros.FirstOrDefaultAsync(x => x.Id == organizacionId, cancellationToken);
        if (persona is null || org is null) { return "Alguno de los terceros no existe."; }
        if (persona.Tipo != TerceroTipo.Persona) { return "El primer tercero debe ser una persona."; }
        if (org.Tipo != TerceroTipo.Empresa) { return "El segundo tercero debe ser una organizacion."; }

        var cargoLimpio = string.IsNullOrWhiteSpace(cargo) ? null : cargo!.Trim();

        var existente = await _db.TerceroVinculos.FirstOrDefaultAsync(
            v => v.PersonaId == personaId && v.OrganizacionId == organizacionId, cancellationToken);
        if (existente is not null)
        {
            existente.Cargo = cargoLimpio ?? existente.Cargo;
            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }

        // Si ya es el enlace primario legado, no se duplica: solo se refresca el cargo del legado.
        if (persona.EmpresaId == organizacionId)
        {
            if (cargoLimpio is not null) { persona.Cargo = cargoLimpio; await _app.SaveChangesAsync(cancellationToken); }
            return null;
        }

        _db.TerceroVinculos.Add(new TerceroVinculo
        {
            TenantId = tenantId,
            PersonaId = personaId,
            OrganizacionId = organizacionId,
            Cargo = cargoLimpio,
            Principal = persona.EmpresaId is null   // sin enlace primario previo -> este pasa a ser el principal
        });
        await _db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> QuitarAsync(Guid personaId, Guid organizacionId, CancellationToken cancellationToken = default)
    {
        var v = await _db.TerceroVinculos.FirstOrDefaultAsync(
            x => x.PersonaId == personaId && x.OrganizacionId == organizacionId, cancellationToken);
        if (v is not null)
        {
            _db.TerceroVinculos.Remove(v);
            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }

        // Enlace primario legado: quitarlo = limpiar EmpresaId de la persona.
        var persona = await _app.Terceros.FirstOrDefaultAsync(x => x.Id == personaId, cancellationToken);
        if (persona is not null && persona.EmpresaId == organizacionId)
        {
            persona.EmpresaId = null;
            await _app.SaveChangesAsync(cancellationToken);
            return null;
        }
        return "El vinculo no existe.";
    }

    public async Task<IReadOnlyList<TerceroBuscarDto>> BuscarAsync(string term, TerceroTipo tipo, Guid excluir, CancellationToken cancellationToken = default)
    {
        var q = (term ?? string.Empty).Trim().ToLowerInvariant();
        if (q.Length < 2) { return Array.Empty<TerceroBuscarDto>(); }

        return await _app.Terceros.AsNoTracking()
            .Where(t => t.DirectoryEngine == DirectoryEngine.Modular && t.Tipo == tipo
                && t.Id != excluir && t.Estado != TerceroEstado.Inactivo
                && (t.Nombre.ToLower().Contains(q) || (t.IdValor != null && t.IdValor.ToLower().Contains(q))))
            .OrderBy(t => t.Nombre).Take(MaxBusqueda)
            .Select(t => new TerceroBuscarDto(t.Id, t.Nombre, t.Tipo == TerceroTipo.Empresa, t.IdValor))
            .ToListAsync(cancellationToken);
    }
}
