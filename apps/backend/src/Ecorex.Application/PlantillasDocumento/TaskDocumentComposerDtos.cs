namespace Ecorex.Application.PlantillasDocumento;

/// <summary>Plantilla de documento disponible para una tarea (habilitada por el concepto), para el picker.</summary>
public sealed record TaskDocTemplateDto(Guid Id, Guid GroupId, string GroupName, string Name);

/// <summary>Documento (Gestor Documental) nacido de una tarea, para la lista de la pestana Documentos.</summary>
public sealed record TaskDocumentoDto(
    Guid Id,
    string Titulo,
    int NumeroVersiones,
    Guid? VersionActualId,
    string? UrlActual,
    DateTimeOffset ActualizadoAt);

/// <summary>Una version del documento de la tarea, para el historial (activar version).</summary>
public sealed record TaskDocumentoVersionDto(
    Guid Id,
    int Numero,
    string? NotasCambio,
    bool EsActual,
    string Url,
    DateTimeOffset CreadoAt);
