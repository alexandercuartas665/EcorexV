namespace Ecorex.Application.Documentos;

// ===========================================================================
// DTOs del Gestor Documental - mitad "Expedientes" (Tabla de Retencion Documental).
// Igual que en el Archivo central, el binario viaja como byte[] y no en base64: aqui la pagina
// llama al servicio en proceso, no por HTTP.
// ===========================================================================

// ---- Configuracion (la TRD) ----

public sealed record TrdSerieDto(Guid Id, string Nombre, int Orden, IReadOnlyList<TrdSubserieDto> Subseries);

public sealed record TrdSubserieDto(
    Guid Id,
    Guid SerieId,
    string Nombre,
    int Orden,
    IReadOnlyList<TrdTipologiaDto> Tipologias,
    IReadOnlyList<TrdCampoDto> Campos);

public sealed record TrdTipologiaDto(Guid Id, string Nombre, bool Obligatoria, int Orden);

public sealed record TrdCampoDto(Guid Id, string Clave, string Label, int Orden);

public sealed record CrearSerieRequest(string Nombre);
public sealed record CrearSubserieRequest(Guid SerieId, string Nombre);
public sealed record CrearTipologiaCfgRequest(Guid SubserieId, string Nombre, bool Obligatoria);
/// <summary>La CLAVE se deriva del label (slug); no se le pide al usuario.</summary>
public sealed record CrearCampoCfgRequest(Guid SubserieId, string Label);

// ---- Expedientes (instancias) ----

public sealed record ExpedienteListaDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string Serie,
    string Subserie,
    int Total,
    int Cargadas,
    int PendientesObligatorias,
    DateTimeOffset CreadoAt);

public sealed record ExpedienteCampoDto(Guid Id, string Clave, string Label, int Orden);

public sealed record ExpedienteMetaPairDto(string Clave, string Label, string? Valor);

public sealed record ExpedienteTipologiaDto(
    Guid Id,
    string Nombre,
    bool Obligatoria,
    bool Cargado,
    string? ArchivoNombre,
    string? ArchivoMime,
    long ArchivoTamano,
    string? ArchivoUrl,
    int Orden,
    IReadOnlyList<ExpedienteMetaPairDto> Meta);

public sealed record ExpedienteDetalleDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string Serie,
    string Subserie,
    int Total,
    int Cargadas,
    IReadOnlyList<ExpedienteCampoDto> Campos,
    IReadOnlyList<ExpedienteTipologiaDto> Tipologias);

public sealed record CrearExpedienteRequest(Guid SubserieId, string Nombre);
public sealed record AgregarTipologiaExpRequest(string Nombre, bool Obligatoria);
public sealed record AgregarCampoExpRequest(string Label);
public sealed record ExpedienteMetaValorDto(string Clave, string? Valor);

public sealed record CargarTipologiaRequest(
    string NombreArchivo,
    string TipoMime,
    byte[] Contenido,
    IReadOnlyList<ExpedienteMetaValorDto>? Meta);
