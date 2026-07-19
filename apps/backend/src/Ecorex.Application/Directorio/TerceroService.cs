using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Implementacion de ITerceroService (Directorio General, modulo 000232). Aislamiento por tenant
/// via filtro global (nunca se filtra a mano por TenantId); el alta estampa el TenantId del
/// contexto. La baja es logica (Estado = Inactivo). Los contactos embebidos y las personas
/// asignadas a una empresa cuentan como "contactos" de esa empresa.
/// </summary>
public sealed class TerceroService : ITerceroService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public TerceroService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<TerceroListItemDto>> ListAsync(
        TerceroListFilter filter, CancellationToken cancellationToken = default)
    {
        // Solo empresas + personas individuales: las personas asignadas a una empresa (EmpresaId
        // != null) se ocultan (cuentan como contacto de la empresa).
        var query = _db.Terceros.AsNoTracking().Where(t => t.EmpresaId == null);

        if (!filter.IncludeInactive)
        {
            query = query.Where(t => t.Estado != TerceroEstado.Inactivo);
        }

        query = filter.Naturaleza switch
        {
            TerceroTabNaturaleza.Empresas => query.Where(t => t.Tipo == TerceroTipo.Empresa),
            TerceroTabNaturaleza.Contactos => query.Where(t => t.Tipo == TerceroTipo.Persona),
            _ => query
        };

        query = filter.Tipo switch
        {
            TerceroTabTipo.Clientes => query.Where(t => (t.Perfiles & TerceroPerfil.Cliente) == TerceroPerfil.Cliente),
            TerceroTabTipo.Proveedores => query.Where(t => (t.Perfiles & TerceroPerfil.Proveedor) == TerceroPerfil.Proveedor),
            TerceroTabTipo.Empleados => query.Where(t => (t.Perfiles & TerceroPerfil.Empleado) == TerceroPerfil.Empleado),
            _ => query
        };

        var term = filter.Busqueda?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(t =>
                t.Nombre.ToLower().Contains(term)
                || (t.IdValor != null && t.IdValor.ToLower().Contains(term))
                || (t.Vendedor != null && t.Vendedor.ToLower().Contains(term))
                || (t.Ciudad != null && t.Ciudad.ToLower().Contains(term)));
        }

        var rows = await query
            .OrderBy(t => t.Nombre)
            .Select(t => new
            {
                t.Id,
                t.Nombre,
                t.Tipo,
                t.Perfiles,
                t.IdTipo,
                t.IdValor,
                t.Vendedor,
                t.Estado,
                t.Ciudad,
                t.Sector,
                t.Cargo,
                t.FichasJson,
                // Contactos = contactos embebidos + personas reasignadas a esta empresa.
                Contactos = _db.TerceroContactos.Count(c => c.TerceroId == t.Id)
                    + _db.Terceros.Count(p => p.EmpresaId == t.Id)
            })
            .ToListAsync(cancellationToken);

        // Claves marcadas "ofrecer como filtro" (ADR-0029). Se consultan una vez, no por fila.
        var filterKeys = await _db.TerceroFieldDefinitions
            .AsNoTracking()
            .Where(f => f.ShowInFilter)
            .Select(f => f.FieldKey)
            .ToListAsync(cancellationToken);

        return rows.Select(t => new TerceroListItemDto(
            t.Id,
            t.Nombre,
            t.Tipo,
            t.Perfiles,
            FormatIdentificacion(t.IdTipo, t.IdValor),
            t.Vendedor,
            t.Estado,
            t.Ciudad,
            t.Tipo == TerceroTipo.Empresa ? t.Sector : t.Cargo,
            t.Contactos,
            t.Tipo == TerceroTipo.Empresa,
            t.Tipo == TerceroTipo.Persona,
            ExtractFilterables(t.FichasJson, filterKeys))).ToList();
    }

    /// <summary>
    /// Saca del FichasJson solo los valores de las claves filtrables. Devuelve null si no hay ninguna
    /// marcada, para no cargar el listado con un diccionario vacio por fila.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? ExtractFilterables(
        string? fichasJson, IReadOnlyCollection<string> filterKeys)
    {
        if (filterKeys.Count == 0 || string.IsNullOrWhiteSpace(fichasJson)) { return null; }

        try
        {
            var fichas = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(fichasJson);
            if (fichas is null) { return null; }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (_, campos) in fichas)
            {
                foreach (var (key, value) in campos)
                {
                    if (filterKeys.Contains(key) && !string.IsNullOrWhiteSpace(value)) { result[key] = value; }
                }
            }
            return result.Count > 0 ? result : null;
        }
        catch (JsonException)
        {
            // Un tercero con el JSON corrupto no debe tumbar el listado entero.
            return null;
        }
    }

    public async Task<TerceroDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var t = await _db.Terceros.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (t is null) { return null; }

        var contactos = await _db.TerceroContactos.AsNoTracking()
            .Where(c => c.TerceroId == id)
            .OrderBy(c => c.Nombre)
            .Select(c => new TerceroContactoDto(c.Id, c.TerceroId, c.Nombre, c.Cargo, c.Email, c.Telefono))
            .ToListAsync(cancellationToken);

        string? empresaNombre = null;
        if (t.EmpresaId is Guid empresaId)
        {
            empresaNombre = await _db.Terceros.AsNoTracking()
                .Where(e => e.Id == empresaId).Select(e => e.Nombre).FirstOrDefaultAsync(cancellationToken);
        }

        return ToDetail(t, empresaNombre, contactos);
    }

    public async Task<TerceroResult<TerceroDetailDto>> CreateAsync(
        SaveTerceroRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TerceroResult<TerceroDetailDto>.Invalid("No hay tenant activo.");
        }
        var nombre = (request.Nombre ?? string.Empty).Trim();
        if (nombre.Length == 0)
        {
            return TerceroResult<TerceroDetailDto>.Invalid("El nombre es obligatorio.");
        }
        if (nombre.Length > 200)
        {
            return TerceroResult<TerceroDetailDto>.Invalid("El nombre no puede superar 200 caracteres.");
        }

        var fichasError = ValidateFichas(request.FichasJson);
        if (fichasError is not null)
        {
            return TerceroResult<TerceroDetailDto>.Invalid(fichasError);
        }

        // Una empresa nunca pertenece a otra empresa.
        Guid? empresaId = request.EmpresaId;
        if (request.Tipo == TerceroTipo.Empresa)
        {
            empresaId = null;
        }
        else if (empresaId is Guid targetEmpresaId)
        {
            var empresaError = await ValidateEmpresaTargetAsync(targetEmpresaId, cancellationToken);
            if (empresaError is not null)
            {
                return TerceroResult<TerceroDetailDto>.Invalid(empresaError);
            }
        }

        var entity = new Tercero { TenantId = tenantId };
        ApplyRequest(entity, request, nombre, empresaId);
        _db.Terceros.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return TerceroResult<TerceroDetailDto>.Ok(ToDetail(entity, null, Array.Empty<TerceroContactoDto>()));
    }

    public async Task<TerceroResult<TerceroDetailDto>> UpdateAsync(
        Guid id, SaveTerceroRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Terceros.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return TerceroResult<TerceroDetailDto>.NotFound("El tercero no existe.");
        }
        var nombre = (request.Nombre ?? string.Empty).Trim();
        if (nombre.Length == 0)
        {
            return TerceroResult<TerceroDetailDto>.Invalid("El nombre es obligatorio.");
        }
        if (nombre.Length > 200)
        {
            return TerceroResult<TerceroDetailDto>.Invalid("El nombre no puede superar 200 caracteres.");
        }

        var fichasError = ValidateFichas(request.FichasJson);
        if (fichasError is not null)
        {
            return TerceroResult<TerceroDetailDto>.Invalid(fichasError);
        }

        Guid? empresaId = request.EmpresaId;
        if (request.Tipo == TerceroTipo.Empresa)
        {
            empresaId = null;
        }
        else if (empresaId is Guid targetEmpresaId)
        {
            if (targetEmpresaId == id)
            {
                return TerceroResult<TerceroDetailDto>.Invalid("Un tercero no puede asignarse a si mismo.");
            }
            var empresaError = await ValidateEmpresaTargetAsync(targetEmpresaId, cancellationToken);
            if (empresaError is not null)
            {
                return TerceroResult<TerceroDetailDto>.Invalid(empresaError);
            }
        }

        ApplyRequest(entity, request, nombre, empresaId);
        await _db.SaveChangesAsync(cancellationToken);

        string? empresaNombre = null;
        if (entity.EmpresaId is Guid eid)
        {
            empresaNombre = await _db.Terceros.AsNoTracking()
                .Where(e => e.Id == eid).Select(e => e.Nombre).FirstOrDefaultAsync(cancellationToken);
        }
        var contactos = await _db.TerceroContactos.AsNoTracking()
            .Where(c => c.TerceroId == id)
            .OrderBy(c => c.Nombre)
            .Select(c => new TerceroContactoDto(c.Id, c.TerceroId, c.Nombre, c.Cargo, c.Email, c.Telefono))
            .ToListAsync(cancellationToken);
        return TerceroResult<TerceroDetailDto>.Ok(ToDetail(entity, empresaNombre, contactos));
    }

    public async Task<TerceroResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Terceros.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return TerceroResult<bool>.NotFound("El tercero no existe.");
        }
        // Baja logica (soft-delete): nunca DELETE fisico de un agregado del directorio.
        entity.Estado = TerceroEstado.Inactivo;
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<bool>.Ok(true);
    }

    public async Task<TerceroResult<bool>> AssignToEmpresaAsync(
        Guid personaId, Guid empresaId, CancellationToken cancellationToken = default)
    {
        var persona = await _db.Terceros.FirstOrDefaultAsync(x => x.Id == personaId, cancellationToken);
        if (persona is null)
        {
            return TerceroResult<bool>.NotFound("La persona no existe.");
        }
        if (persona.Tipo != TerceroTipo.Persona)
        {
            return TerceroResult<bool>.Invalid("Solo una persona natural se puede asignar a una empresa.");
        }
        if (empresaId == personaId)
        {
            return TerceroResult<bool>.Invalid("Un tercero no puede asignarse a si mismo.");
        }
        var empresaError = await ValidateEmpresaTargetAsync(empresaId, cancellationToken);
        if (empresaError is not null)
        {
            return TerceroResult<bool>.Invalid(empresaError);
        }

        persona.EmpresaId = empresaId;
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<bool>.Ok(true);
    }

    public async Task<TerceroResult<bool>> UnassignFromEmpresaAsync(
        Guid personaId, CancellationToken cancellationToken = default)
    {
        var persona = await _db.Terceros.FirstOrDefaultAsync(x => x.Id == personaId, cancellationToken);
        if (persona is null)
        {
            return TerceroResult<bool>.NotFound("La persona no existe.");
        }
        persona.EmpresaId = null;
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<bool>.Ok(true);
    }

    public async Task<IReadOnlyList<TerceroContactoDto>> ListContactosAsync(
        Guid terceroId, CancellationToken cancellationToken = default)
        => await _db.TerceroContactos.AsNoTracking()
            .Where(c => c.TerceroId == terceroId)
            .OrderBy(c => c.Nombre)
            .Select(c => new TerceroContactoDto(c.Id, c.TerceroId, c.Nombre, c.Cargo, c.Email, c.Telefono))
            .ToListAsync(cancellationToken);

    public async Task<TerceroResult<TerceroContactoDto>> AddContactoAsync(
        Guid terceroId, SaveContactoRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TerceroResult<TerceroContactoDto>.Invalid("No hay tenant activo.");
        }
        var empresa = await _db.Terceros.AsNoTracking().FirstOrDefaultAsync(x => x.Id == terceroId, cancellationToken);
        if (empresa is null)
        {
            return TerceroResult<TerceroContactoDto>.NotFound("El tercero no existe.");
        }
        var nombre = (request.Nombre ?? string.Empty).Trim();
        if (nombre.Length == 0)
        {
            return TerceroResult<TerceroContactoDto>.Invalid("El nombre del contacto es obligatorio.");
        }

        var contacto = new TerceroContacto
        {
            TenantId = tenantId,
            TerceroId = terceroId,
            Nombre = nombre,
            Cargo = Normalize(request.Cargo),
            Email = Normalize(request.Email),
            Telefono = Normalize(request.Telefono)
        };
        _db.TerceroContactos.Add(contacto);
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<TerceroContactoDto>.Ok(new TerceroContactoDto(
            contacto.Id, contacto.TerceroId, contacto.Nombre, contacto.Cargo, contacto.Email, contacto.Telefono));
    }

    public async Task<TerceroResult<TerceroContactoDto>> UpdateContactoAsync(
        Guid contactoId, SaveContactoRequest request, CancellationToken cancellationToken = default)
    {
        var contacto = await _db.TerceroContactos.FirstOrDefaultAsync(x => x.Id == contactoId, cancellationToken);
        if (contacto is null)
        {
            return TerceroResult<TerceroContactoDto>.NotFound("El contacto no existe.");
        }
        var nombre = (request.Nombre ?? string.Empty).Trim();
        if (nombre.Length == 0)
        {
            return TerceroResult<TerceroContactoDto>.Invalid("El nombre del contacto es obligatorio.");
        }
        contacto.Nombre = nombre;
        contacto.Cargo = Normalize(request.Cargo);
        contacto.Email = Normalize(request.Email);
        contacto.Telefono = Normalize(request.Telefono);
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<TerceroContactoDto>.Ok(new TerceroContactoDto(
            contacto.Id, contacto.TerceroId, contacto.Nombre, contacto.Cargo, contacto.Email, contacto.Telefono));
    }

    public async Task<TerceroResult<bool>> DeleteContactoAsync(
        Guid contactoId, CancellationToken cancellationToken = default)
    {
        var contacto = await _db.TerceroContactos.FirstOrDefaultAsync(x => x.Id == contactoId, cancellationToken);
        if (contacto is null)
        {
            return TerceroResult<bool>.NotFound("El contacto no existe.");
        }
        // Contacto embebido: no es un agregado propio, se elimina fisicamente.
        _db.TerceroContactos.Remove(contacto);
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<bool>.Ok(true);
    }

    // ---- Notas / gestiones "Contacto cliente" ----

    public async Task<IReadOnlyList<TerceroNotaDto>> ListNotasAsync(
        Guid terceroId, CancellationToken cancellationToken = default)
        => await _db.TerceroNotas.AsNoTracking()
            .Where(n => n.TerceroId == terceroId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new TerceroNotaDto(
                n.Id, n.TerceroId, n.Texto, n.Accion, n.Categoria, n.Subcategoria, n.Autor, n.CreatedAt,
                n.ConceptoActividadId,
                n.ConceptoActividadId == null ? null : _db.ConceptosActividad.Where(c => c.Id == n.ConceptoActividadId).Select(c => c.Name).FirstOrDefault(),
                n.FormResponseId, n.Valor))
            .ToListAsync(cancellationToken);

    public async Task<TerceroResult<TerceroNotaDto>> AddNotaAsync(
        Guid terceroId, SaveNotaRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TerceroResult<TerceroNotaDto>.Invalid("No hay tenant activo.");
        }
        var tercero = await _db.Terceros.AsNoTracking().FirstOrDefaultAsync(x => x.Id == terceroId, cancellationToken);
        if (tercero is null)
        {
            return TerceroResult<TerceroNotaDto>.NotFound("El tercero no existe.");
        }
        var texto = (request.Texto ?? string.Empty).Trim();
        if (texto.Length == 0)
        {
            return TerceroResult<TerceroNotaDto>.Invalid("El texto de la nota es obligatorio.");
        }

        var nota = new TerceroNota
        {
            TenantId = tenantId,
            TerceroId = terceroId,
            Texto = texto,
            Accion = Normalize(request.Accion) ?? "Nota",
            Categoria = Normalize(request.Categoria),
            Subcategoria = Normalize(request.Subcategoria),
            Autor = Normalize(request.Autor),
            ConceptoActividadId = request.ConceptoActividadId,
            FormResponseId = request.FormResponseId,
            Valor = request.Valor
        };
        _db.TerceroNotas.Add(nota);
        await _db.SaveChangesAsync(cancellationToken);
        var conceptoNombre = request.ConceptoActividadId is Guid cid
            ? await _db.ConceptosActividad.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        return TerceroResult<TerceroNotaDto>.Ok(new TerceroNotaDto(
            nota.Id, nota.TerceroId, nota.Texto, nota.Accion, nota.Categoria, nota.Subcategoria, nota.Autor, nota.CreatedAt,
            nota.ConceptoActividadId, conceptoNombre, nota.FormResponseId, nota.Valor));
    }

    public async Task<TerceroResult<bool>> DeleteNotaAsync(
        Guid notaId, CancellationToken cancellationToken = default)
    {
        var nota = await _db.TerceroNotas.FirstOrDefaultAsync(x => x.Id == notaId, cancellationToken);
        if (nota is null)
        {
            return TerceroResult<bool>.NotFound("La nota no existe.");
        }
        _db.TerceroNotas.Remove(nota);
        await _db.SaveChangesAsync(cancellationToken);
        return TerceroResult<bool>.Ok(true);
    }

    public async Task<TerceroKpisDto> GetKpisAsync(CancellationToken cancellationToken = default)
    {
        var activos = _db.Terceros.AsNoTracking().Where(t => t.Estado != TerceroEstado.Inactivo);
        var clientes = await activos.CountAsync(
            t => (t.Perfiles & TerceroPerfil.Cliente) == TerceroPerfil.Cliente, cancellationToken);
        var empresas = await activos.CountAsync(t => t.Tipo == TerceroTipo.Empresa, cancellationToken);
        var personas = await activos.CountAsync(t => t.Tipo == TerceroTipo.Persona, cancellationToken);
        // Contactos asociados = contactos embebidos + personas reasignadas a una empresa.
        var contactosEmbebidos = await _db.TerceroContactos.AsNoTracking().CountAsync(cancellationToken);
        var personasAsignadas = await activos.CountAsync(t => t.EmpresaId != null, cancellationToken);
        return new TerceroKpisDto(clientes, empresas, personas, contactosEmbebidos + personasAsignadas);
    }

    // ---- Internos ----

    private async Task<string?> ValidateEmpresaTargetAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var empresa = await _db.Terceros.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == empresaId, cancellationToken);
        if (empresa is null)
        {
            return "La empresa destino no existe.";
        }
        if (empresa.Tipo != TerceroTipo.Empresa)
        {
            return "El destino de la asignacion debe ser una empresa.";
        }
        return null;
    }

    private static void ApplyRequest(Tercero entity, SaveTerceroRequest request, string nombre, Guid? empresaId)
    {
        entity.Nombre = nombre;
        entity.Tipo = request.Tipo;
        entity.Perfiles = request.Perfiles;
        entity.Estado = request.Estado;
        entity.Vendedor = Normalize(request.Vendedor);
        entity.Ciudad = Normalize(request.Ciudad);
        entity.IdTipo = request.IdTipo;
        entity.IdValor = request.IdTipo == TerceroIdTipo.Ninguno ? null : Normalize(request.IdValor);
        entity.Sector = Normalize(request.Sector);
        entity.Cargo = Normalize(request.Cargo);
        entity.Email = Normalize(request.Email);
        entity.Telefono = Normalize(request.Telefono);
        entity.EmpresaId = empresaId;
        entity.FichasJson = Normalize(request.FichasJson);
    }

    private static TerceroDetailDto ToDetail(
        Tercero t, string? empresaNombre, IReadOnlyList<TerceroContactoDto> contactos) => new(
        t.Id,
        t.Nombre,
        t.Tipo,
        t.Perfiles,
        t.Estado,
        t.Vendedor,
        t.Ciudad,
        t.IdTipo,
        t.IdValor,
        FormatIdentificacion(t.IdTipo, t.IdValor),
        t.Sector,
        t.Cargo,
        t.Email,
        t.Telefono,
        t.EmpresaId,
        empresaNombre,
        t.Tipo == TerceroTipo.Empresa,
        t.Tipo == TerceroTipo.Persona,
        t.FichasJson,
        ParseFichas(t.FichasJson),
        contactos);

    private static string FormatIdentificacion(TerceroIdTipo tipo, string? valor)
    {
        if (tipo == TerceroIdTipo.Ninguno || string.IsNullOrWhiteSpace(valor))
        {
            return "Sin identificacion";
        }
        return tipo switch
        {
            TerceroIdTipo.Nit => $"NIT {valor}",
            TerceroIdTipo.Identificacion => $"CC {valor}",
            TerceroIdTipo.Correo => valor,
            TerceroIdTipo.Telefono => $"Tel {valor}",
            _ => valor
        };
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Valida que FichasJson (si viene) sea un objeto JSON. Devuelve el error o null.</summary>
    private static string? ValidateFichas(string? fichasJson)
    {
        if (string.IsNullOrWhiteSpace(fichasJson)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(fichasJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return "Las fichas deben ser un objeto JSON (ficha -> campos).";
            }
        }
        catch (JsonException ex)
        {
            return $"Fichas: JSON invalido ({ex.Message}).";
        }
        return null;
    }

    /// <summary>
    /// Parsea FichasJson a un diccionario ficha -&gt; (campo -&gt; valor). Defensivo: ante JSON
    /// invalido o formas inesperadas devuelve un diccionario vacio (el detalle nunca falla por esto).
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> ParseFichas(string? fichasJson)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(fichasJson)) { return result; }
        try
        {
            using var doc = JsonDocument.Parse(fichasJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return result; }
            foreach (var ficha in doc.RootElement.EnumerateObject())
            {
                var campos = new Dictionary<string, string?>(StringComparer.Ordinal);
                if (ficha.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var campo in ficha.Value.EnumerateObject())
                    {
                        campos[campo.Name] = campo.Value.ValueKind switch
                        {
                            JsonValueKind.String => campo.Value.GetString(),
                            JsonValueKind.Null => null,
                            _ => campo.Value.GetRawText()
                        };
                    }
                }
                result[ficha.Name] = campos;
            }
        }
        catch (JsonException)
        {
            return new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.Ordinal);
        }
        return result;
    }
}
