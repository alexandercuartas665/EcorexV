using Ecorex.Domain.Enums;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Fila del listado del Directorio General (modulo 000232). Solo aparecen empresas y
/// personas individuales (una persona asignada a una empresa se oculta y cuenta como
/// contacto de esa empresa).
/// </summary>
/// <param name="Filtrables">
/// Valores de los campos marcados "ofrecer como filtro" (ADR-0029), por FieldKey. Solo esos: el
/// listado no necesita cargar toda la ficha para filtrar. Vacio si el tenant no marco ninguno.
/// </param>
public sealed record TerceroListItemDto(
    Guid Id,
    string Nombre,
    TerceroTipo Tipo,
    TerceroPerfil Perfiles,
    string Identificacion,
    string? Vendedor,
    TerceroEstado Estado,
    string? Ciudad,
    string? Sub,
    int ContadorContactos,
    bool EsEmpresa,
    bool EsPersona,
    IReadOnlyDictionary<string, string>? Filtrables = null,
    // Vendedor asignado desde el catalogo de asesores (000074). Null = sin asesor; en ese caso la UI
    // cae al texto libre legado de <see cref="Vendedor"/>.
    Guid? VendedorAsesorId = null,
    string? VendedorAsesorNombre = null);

/// <summary>Detalle completo de un tercero: campos + fichas dinamicas + contactos.</summary>
public sealed record TerceroDetailDto(
    Guid Id,
    string Nombre,
    TerceroTipo Tipo,
    TerceroPerfil Perfiles,
    TerceroEstado Estado,
    string? Vendedor,
    string? Ciudad,
    TerceroIdTipo IdTipo,
    string? IdValor,
    string Identificacion,
    string? Sector,
    string? Cargo,
    string? Email,
    string? Telefono,
    Guid? EmpresaId,
    string? EmpresaNombre,
    bool EsEmpresa,
    bool EsPersona,
    string? FichasJson,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> Fichas,
    IReadOnlyList<TerceroContactoDto> Contactos,
    Guid? VendedorAsesorId = null,
    string? VendedorAsesorNombre = null,
    // Auditoria de alta, de solo lectura, para los campos "fecha de creacion" y "usuario" del
    // formulario Publico. CreadoPor viene resuelto a nombre; null si el registro es anterior a la
    // auditoria o el usuario ya no existe.
    DateTimeOffset? CreatedAt = null,
    string? CreadoPor = null,
    // Telefono de la empresa vinculada: el formulario Publico pide un telefono por lado (empresa y
    // contacto) y exige al menos uno de los dos.
    string? EmpresaTelefono = null);

/// <summary>Contacto embebido de una empresa.</summary>
public sealed record TerceroContactoDto(
    Guid Id,
    Guid TerceroId,
    string Nombre,
    string? Cargo,
    string? Email,
    string? Telefono,
    // true = es un Tercero tipo Persona vinculado a la empresa (EmpresaId): se edita con el modal
    // completo y su Id es el del tercero. false = contacto ligero heredado (entidad TerceroContacto).
    bool EsTercero = false);

/// <summary>Alta/edicion de un tercero.</summary>
public sealed record SaveTerceroRequest(
    string Nombre,
    TerceroTipo Tipo = TerceroTipo.Empresa,
    TerceroPerfil Perfiles = TerceroPerfil.Ninguno,
    TerceroEstado Estado = TerceroEstado.Activo,
    string? Vendedor = null,
    string? Ciudad = null,
    TerceroIdTipo IdTipo = TerceroIdTipo.Nit,
    string? IdValor = null,
    string? Sector = null,
    string? Cargo = null,
    string? Email = null,
    string? Telefono = null,
    Guid? EmpresaId = null,
    string? FichasJson = null,
    Guid? VendedorAsesorId = null);

/// <summary>Alta/edicion de un contacto embebido de una empresa.</summary>
public sealed record SaveContactoRequest(
    string Nombre,
    string? Cargo = null,
    string? Email = null,
    string? Telefono = null);

/// <summary>Nota / gestion "Contacto cliente" de un tercero (timeline).</summary>
public sealed record TerceroNotaDto(
    Guid Id,
    Guid TerceroId,
    string Texto,
    string Accion,
    string? Categoria,
    string? Subcategoria,
    string? Autor,
    DateTimeOffset CreatedAt,
    Guid? ConceptoActividadId = null,
    string? ConceptoNombre = null,
    Guid? FormResponseId = null,
    decimal? Valor = null);

/// <summary>Alta de una nota / gestion del tercero.</summary>
public sealed record SaveNotaRequest(
    string Texto,
    string Accion = "Nota",
    string? Categoria = null,
    string? Subcategoria = null,
    string? Autor = null,
    Guid? ConceptoActividadId = null,
    Guid? FormResponseId = null,
    decimal? Valor = null);

/// <summary>KPIs de cabecera del modulo (como el prototipo).</summary>
public sealed record TerceroKpisDto(
    int Clientes,
    int Empresas,
    int Personas,
    int ContactosAsociados);

/// <summary>
/// Directorio activo del listado (fila TIPO). Cada directorio filtra por el perfil que le
/// corresponde y, en el editor, abre la ficha de datos de ese directorio.
/// <see cref="Publico"/> es la vista completa: no filtra y solo edita los datos basicos.
/// </summary>
public enum TerceroTabTipo
{
    Publico = 0,
    Fiscal,
    Comercial,
    Cartera,
    Proveedores,
    Laboral
}

/// <summary>Tab por naturaleza del listado (empresa vs persona individual).</summary>
public enum TerceroTabNaturaleza
{
    Todos = 0,
    Empresas,
    Contactos
}

/// <summary>Filtros del listado del Directorio General.</summary>
public sealed record TerceroListFilter(
    TerceroTabTipo Tipo = TerceroTabTipo.Publico,
    TerceroTabNaturaleza Naturaleza = TerceroTabNaturaleza.Todos,
    string? Busqueda = null,
    bool IncludeInactive = false);
