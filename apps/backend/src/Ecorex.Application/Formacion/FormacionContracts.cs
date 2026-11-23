using Ecorex.Domain.Entities;

namespace Ecorex.Application.Formacion;

/// <summary>Resultado simple de una operacion del modulo de Formacion (ADR-0202).</summary>
public sealed record FormacionResult(bool Ok, string? Error = null, Guid? Id = null)
{
    public static FormacionResult Bien(Guid? id = null) => new(true, null, id);
    public static FormacionResult Mal(string error) => new(false, error);
}

/// <summary>Tarjeta de curso (catalogo del admin).</summary>
public sealed record CursoResumenDto(
    Guid Id, string Titulo, string? Descripcion, string? Categoria, Guid? PortadaFileId, bool Publicado,
    int? DuracionMinutos, int Modulos, int Lecciones, int Asignaciones, DateTimeOffset CreatedAt)
{
    public string? PortadaUrl => PortadaFileId is Guid g ? $"/archivo/{g}" : null;
}

/// <summary>Leccion con lo necesario para editarla o verla.</summary>
public sealed record LeccionDto(
    Guid Id, Guid ModuloId, string Titulo, string? Descripcion, int Orden, CursoLeccionTipo Tipo,
    Guid? ArchivoId, string? ArchivoNombre, string? ArchivoMime, long? ArchivoBytes, string? Url, string? Contenido,
    string? WizardEntryPath, int? DuracionMinutos)
{
    /// <summary>Binario de la leccion (video/PDF/imagen/archivo), servido con rangos por el modulo.</summary>
    public string? RecursoUrl => ArchivoId is null ? null : $"/formacion/recurso/{Id}";

    /// <summary>Reproductor embebido de YouTube/Vimeo (null si el enlace no es valido).</summary>
    public string? EmbedUrl => Tipo == CursoLeccionTipo.VideoEnlace ? FormacionVideoLinks.ToEmbedUrl(Url) : null;

    /// <summary>true si la leccion ya tiene su contenido (archivo, enlace o texto) y se puede ver.</summary>
    public bool TieneContenido => Tipo switch
    {
        CursoLeccionTipo.VideoEnlace => EmbedUrl is not null,
        CursoLeccionTipo.Texto => !string.IsNullOrWhiteSpace(Contenido),
        _ => ArchivoId is not null
    };
}

public sealed record ModuloDto(Guid Id, string Titulo, string? Descripcion, int Orden, IReadOnlyList<LeccionDto> Lecciones);

public sealed record CursoDetalleDto(
    Guid Id, string Titulo, string? Descripcion, string? Categoria, Guid? PortadaFileId, bool Publicado,
    int? DuracionMinutos, IReadOnlyList<ModuloDto> Modulos)
{
    public string? PortadaUrl => PortadaFileId is Guid g ? $"/archivo/{g}" : null;
    public int TotalLecciones => Modulos.Sum(m => m.Lecciones.Count);
}

public sealed record GuardarCursoRequest(Guid? Id, string Titulo, string? Descripcion, string? Categoria, bool Publicado, int? DuracionMinutos);

public sealed record GuardarModuloRequest(Guid CursoId, Guid? Id, string Titulo, string? Descripcion);

public sealed record GuardarLeccionRequest(
    Guid ModuloId, Guid? Id, string Titulo, string? Descripcion, CursoLeccionTipo Tipo, string? Url, string? Contenido, int? DuracionMinutos);

/// <summary>Asignacion de un curso como obligatorio.</summary>
public sealed record AsignacionDto(
    Guid Id, Guid CursoId, CursoAsignacionDestino Destino, Guid? TenantUserId, Guid? OrgUnitId, string DestinoNombre,
    DateOnly? FechaLimite, int Personas);

public sealed record AgregarAsignacionRequest(
    Guid CursoId, CursoAsignacionDestino Destino, Guid? TenantUserId, Guid? OrgUnitId, DateOnly? FechaLimite);

/// <summary>Opcion de los selectores de asignacion (usuario o unidad del organigrama).</summary>
public sealed record FormacionOpcionDto(Guid Id, string Nombre, string? Detalle);

/// <summary>Curso visto desde el catalogo del usuario (con su asignacion y avance). Completado = terminado
/// (lecciones vistas y, si el curso la exige, evaluacion aprobada).</summary>
public sealed record CatalogoCursoDto(
    Guid Id, string Titulo, string? Descripcion, string? Categoria, Guid? PortadaFileId, int? DuracionMinutos,
    int Lecciones, bool Obligatorio, DateOnly? FechaLimite, bool Vencido, bool Iniciado, int Porcentaje, bool Completado)
{
    public string? PortadaUrl => PortadaFileId is Guid g ? $"/archivo/{g}" : null;
}

/// <summary>Curso abierto en el reproductor: estructura + lecciones vistas + avance. Completado = vio todas las
/// lecciones; Terminado = ademas aprobo la evaluacion final si el curso la exige (ola 2).</summary>
public sealed record CursoVistaDto(
    CursoDetalleDto Curso, IReadOnlySet<Guid> Vistas, int Porcentaje, bool Completado, Guid? UltimaLeccionId,
    bool Obligatorio, DateOnly? FechaLimite, bool TieneEvaluacion = false, bool Aprobado = false, bool Terminado = false);

/// <summary>Avance tras marcar una leccion.</summary>
public sealed record AvanceDto(int LeccionesVistas, int TotalLecciones, int Porcentaje, bool Completado);

/// <summary>Leccion wizard lista para servir: tenant, archivo ZIP y como desempacarlo (ADR-0202).</summary>
public sealed record WizardTarget(Guid TenantId, Guid LeccionId, Guid ZipFileId, string? RootPrefix, string EntryPath);

/// <summary>Enlaces de YouTube/Vimeo a URL embebible (solo esos dos proveedores).</summary>
public static class FormacionVideoLinks
{
    /// <summary>URL del reproductor embebido o null si el enlace no es de YouTube/Vimeo valido.</summary>
    public static string? ToEmbedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) { return null; }
        if (u.Scheme is not ("https" or "http")) { return null; }
        var host = u.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) { host = host[4..]; }
        if (host.StartsWith("m.", StringComparison.Ordinal)) { host = host[2..]; }
        var segs = u.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        string? yt = null;
        if (host == "youtu.be" && segs.Length >= 1) { yt = segs[0]; }
        else if (host is "youtube.com" or "youtube-nocookie.com")
        {
            if (segs.Length >= 2 && segs[0] is "embed" or "shorts" or "live" or "v") { yt = segs[1]; }
            else if (segs.Length >= 1 && segs[0] == "watch")
            {
                yt = System.Web.HttpUtility.ParseQueryString(u.Query)["v"];
            }
        }
        if (yt is not null)
        {
            return IsSafeId(yt, 6, 20) ? $"https://www.youtube-nocookie.com/embed/{yt}" : null;
        }

        if (host is "vimeo.com" or "player.vimeo.com")
        {
            var id = segs.FirstOrDefault(s => s.All(char.IsDigit));
            return id is { Length: >= 4 and <= 15 } ? $"https://player.vimeo.com/video/{id}" : null;
        }
        return null;
    }

    private static bool IsSafeId(string s, int min, int max)
        => s.Length >= min && s.Length <= max && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
