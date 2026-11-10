using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Analisis;

/// <summary>Como esta compartido un libro (para el dialogo "Compartir").</summary>
public sealed record AnalisisCompartirEstado(
    AnalisisVisibilidad Visibilidad,
    IReadOnlyList<Guid> Usuarios,
    bool EnlaceActivo,
    DateTimeOffset? EnlaceCreado,
    DateTimeOffset? EnlaceExpira);

/// <summary>Usuario del tenant elegible para ver un libro privado.</summary>
public sealed record AnalisisUsuarioRef(Guid UserId, string Nombre, string Email);

/// <summary>Token de un enlace publico resuelto: el UNICO dato cross-tenant que sale de aqui.</summary>
public sealed record AnalisisEnlaceResuelto(Guid TenantId, Guid LibroId);

/// <summary>Resultado de generar/rotar un enlace: el token en claro SOLO se devuelve aqui (se guarda el hash).</summary>
public sealed record AnalisisEnlaceNuevo(bool Ok, string? Token, string? Error);

/// <summary>
/// Compartir un libro (ADR-0158 D7, Ola 6): visibilidad en el tenant + lista de usuarios, y el enlace
/// publico de solo lectura <c>/a/{token}</c> (token opaco de 32 bytes, solo se guarda su SHA-256; rotar
/// revoca el anterior; expiracion opcional). Cambiar la visibilidad: dueno o Admin. El enlace publico
/// expone datos del tenant fuera del sistema: SOLO Admin lo activa.
/// <see cref="ResolverAsync"/> es el UNICO punto cross-tenant (busca por hash exacto, inenumerable).
/// </summary>
public interface IAnalisisCompartirService
{
    Task<AnalisisCompartirEstado?> EstadoAsync(Guid libroId, AnalisisActor actor, CancellationToken ct = default);

    Task<IReadOnlyList<AnalisisUsuarioRef>> UsuariosAsync(CancellationToken ct = default);

    Task<AnalisisOpResult> CambiarVisibilidadAsync(Guid libroId, AnalisisVisibilidad visibilidad, IReadOnlyList<Guid> usuarios, AnalisisActor actor, CancellationToken ct = default);

    /// <summary>Crea (o rota) el enlace publico; deja el libro en visibilidad EnlacePublico.</summary>
    Task<AnalisisEnlaceNuevo> GenerarEnlaceAsync(Guid libroId, DateTimeOffset? expira, AnalisisActor actor, CancellationToken ct = default);

    /// <summary>Revoca el enlace; el libro vuelve a "Compartido en el tenant".</summary>
    Task<AnalisisOpResult> RevocarEnlaceAsync(Guid libroId, AnalisisActor actor, CancellationToken ct = default);

    /// <summary>Token -> tenant + libro, o null (invalido, revocado, vencido o libro ya no publico).</summary>
    Task<AnalisisEnlaceResuelto?> ResolverAsync(string? token, CancellationToken ct = default);

    /// <summary>Libro en solo lectura para el visor publico. Llamar con el tenant del token como ambient.</summary>
    Task<AnalisisLibroDetalle?> LibroPublicoAsync(Guid libroId, CancellationToken ct = default);

    /// <summary>
    /// Libro en solo lectura para el PDF (boton "Descargar PDF"), de CUALQUIER visibilidad. A diferencia
    /// de <see cref="LibroPublicoAsync"/> no exige enlace publico: el endpoint autorizado ya comprobo que
    /// el usuario puede verlo y acota por su tenant. Llamar con ese tenant como ambient.
    /// </summary>
    Task<AnalisisLibroDetalle?> LibroParaPdfAsync(Guid libroId, CancellationToken ct = default);
}

public sealed class AnalisisCompartirService : IAnalisisCompartirService
{
    private const int MaxToken = 64;
    private readonly IApplicationDbContextFactory _factory;
    private readonly TimeProvider _time;

    public AnalisisCompartirService(IApplicationDbContextFactory factory, TimeProvider? time = null)
    {
        _factory = factory;
        _time = time ?? TimeProvider.System;
    }

    public async Task<AnalisisCompartirEstado?> EstadoAsync(Guid libroId, AnalisisActor actor, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var libro = await db.AnalisisLibros.AsNoTracking().FirstOrDefaultAsync(l => l.Id == libroId, ct);
        if (libro is null || !AnalisisService.PuedeEditar(libro, actor))
        {
            return null;
        }

        var ahora = _time.GetUtcNow();
        var token = await db.AnalisisLibroTokens.AsNoTracking()
            .Where(t => t.LibroId == libroId && t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > ahora))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return new AnalisisCompartirEstado(
            libro.Visibilidad,
            AnalisisService.AllowedUsers(libro.AllowedUserIdsJson).ToList(),
            token is not null && libro.Visibilidad == AnalisisVisibilidad.EnlacePublico,
            token?.CreatedAt,
            token?.ExpiresAt);
    }

    public async Task<IReadOnlyList<AnalisisUsuarioRef>> UsuariosAsync(CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var usuarios = await db.TenantUsers.AsNoTracking()
            .Where(u => u.Status == PlatformUserStatus.Active)
            .Select(u => new { u.PlatformUserId, u.Email, Nombre = u.PlatformUser != null ? u.PlatformUser.DisplayName : null })
            .ToListAsync(ct);

        return usuarios
            .Select(u => new AnalisisUsuarioRef(u.PlatformUserId, string.IsNullOrWhiteSpace(u.Nombre) ? u.Email : u.Nombre.Trim(), u.Email))
            .OrderBy(u => u.Nombre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AnalisisOpResult> CambiarVisibilidadAsync(
        Guid libroId, AnalisisVisibilidad visibilidad, IReadOnlyList<Guid> usuarios, AnalisisActor actor, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(visibilidad))
        {
            return AnalisisOpResult.Falla("Visibilidad no valida.");
        }

        if (visibilidad == AnalisisVisibilidad.EnlacePublico)
        {
            return AnalisisOpResult.Falla("El enlace publico se activa con \"Generar enlace\".");
        }

        await using var db = _factory.Create();
        await using var tx = await db.BeginTransactionAsync(ct);
        var libro = await db.AnalisisLibros.FirstOrDefaultAsync(l => l.Id == libroId, ct);
        if (libro is null || !AnalisisService.PuedeEditar(libro, actor))
        {
            return AnalisisOpResult.Falla("Solo el dueno del libro o un administrador puede compartirlo.");
        }

        // Pasar a Privado o Compartido apaga el enlace publico: se revocan sus tokens en la misma transaccion.
        if (libro.Visibilidad == AnalisisVisibilidad.EnlacePublico)
        {
            await RevocarTokensAsync(db, libroId, ct);
        }

        libro.Visibilidad = visibilidad;
        var lista = usuarios.Where(u => u != Guid.Empty && u != libro.OwnerUserId).Distinct().ToList();
        libro.AllowedUserIdsJson = lista.Count == 0 ? null : JsonSerializer.Serialize(lista);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return AnalisisOpResult.Hecho(libro.Id);
    }

    public async Task<AnalisisEnlaceNuevo> GenerarEnlaceAsync(Guid libroId, DateTimeOffset? expira, AnalisisActor actor, CancellationToken ct = default)
    {
        if (!actor.EsAdmin)
        {
            return new AnalisisEnlaceNuevo(false, null, "Solo un administrador puede publicar un libro con enlace publico.");
        }

        var ahora = _time.GetUtcNow();
        if (expira is DateTimeOffset e && e <= ahora)
        {
            return new AnalisisEnlaceNuevo(false, null, "La fecha de expiracion debe ser futura.");
        }

        await using var db = _factory.Create();
        await using var tx = await db.BeginTransactionAsync(ct);
        var libro = await db.AnalisisLibros.FirstOrDefaultAsync(l => l.Id == libroId, ct);
        if (libro is null)
        {
            return new AnalisisEnlaceNuevo(false, null, "El libro no existe.");
        }

        await RevocarTokensAsync(db, libroId, ct); // rotar: el enlace anterior deja de funcionar
        var token = GenerarToken();
        db.AnalisisLibroTokens.Add(new AnalisisLibroToken { LibroId = libroId, TokenHash = Hash(token), ExpiresAt = expira });
        libro.Visibilidad = AnalisisVisibilidad.EnlacePublico;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new AnalisisEnlaceNuevo(true, token, null);
    }

    public async Task<AnalisisOpResult> RevocarEnlaceAsync(Guid libroId, AnalisisActor actor, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        await using var tx = await db.BeginTransactionAsync(ct);
        var libro = await db.AnalisisLibros.FirstOrDefaultAsync(l => l.Id == libroId, ct);
        if (libro is null || !AnalisisService.PuedeEditar(libro, actor))
        {
            return AnalisisOpResult.Falla("Solo el dueno del libro o un administrador puede revocar el enlace.");
        }

        await RevocarTokensAsync(db, libroId, ct);
        if (libro.Visibilidad == AnalisisVisibilidad.EnlacePublico)
        {
            libro.Visibilidad = AnalisisVisibilidad.CompartidoEnTenant;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return AnalisisOpResult.Hecho(libro.Id);
    }

    public async Task<AnalisisEnlaceResuelto?> ResolverAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxToken)
        {
            return null;
        }

        var hash = Hash(token.Trim());
        var ahora = _time.GetUtcNow();
        await using var db = _factory.Create();

        // UNICO punto cross-tenant: el visor anonimo no tiene tenant; se ignora el filtro global SOLO por
        // igualdad exacta del hash. El libro debe seguir vivo y en visibilidad EnlacePublico.
        var r = await (
            from t in db.AnalisisLibroTokens.IgnoreQueryFilters().AsNoTracking()
            join l in db.AnalisisLibros.IgnoreQueryFilters().AsNoTracking() on t.LibroId equals l.Id
            where t.TokenHash == hash && t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > ahora)
                && t.TenantId == l.TenantId && !l.IsDeleted && l.Visibilidad == AnalisisVisibilidad.EnlacePublico
            select new { l.TenantId, LibroId = l.Id })
            .FirstOrDefaultAsync(ct);

        return r is null ? null : new AnalisisEnlaceResuelto(r.TenantId, r.LibroId);
    }

    public async Task<AnalisisLibroDetalle?> LibroPublicoAsync(Guid libroId, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var libro = await db.AnalisisLibros.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == libroId && l.Visibilidad == AnalisisVisibilidad.EnlacePublico, ct);
        return libro is null ? null : await AnalisisService.CargarDetalleAsync(db, libro, puedeEditar: false, ct);
    }

    public async Task<AnalisisLibroDetalle?> LibroParaPdfAsync(Guid libroId, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        // El filtro global del contexto acota al tenant ambient: solo devuelve el libro si es de ese tenant.
        var libro = await db.AnalisisLibros.AsNoTracking().FirstOrDefaultAsync(l => l.Id == libroId, ct);
        return libro is null ? null : await AnalisisService.CargarDetalleAsync(db, libro, puedeEditar: false, ct);
    }

    private async Task RevocarTokensAsync(IApplicationDbContext db, Guid libroId, CancellationToken ct)
    {
        var ahora = _time.GetUtcNow();
        foreach (var t in await db.AnalisisLibroTokens.Where(t => t.LibroId == libroId && t.RevokedAt == null).ToListAsync(ct))
        {
            t.RevokedAt = ahora;
        }
    }

    private static string GenerarToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>SHA-256 hex minusculas del token en claro (patron FormToken).</summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
