namespace Ecorex.Application.Confidentiality;

/// <summary>Vista admin de la politica de confidencialidad del tenant (ADR-0160).</summary>
public sealed record ConfidentialityPolicyDto(
    Guid Id,
    string Titulo,
    string Contenido,
    bool Activa,
    bool CapturaDatos,
    string PublicToken,
    long Version,
    DateTimeOffset? UpdatedAt,
    int TotalAceptaciones);

/// <summary>Lo que ve el visor publico /pc/{token} tras resolver el token (cross-tenant).</summary>
public sealed record ConfidentialityPolicyPublicDto(
    Guid PolicyId,
    Guid TenantId,
    string Titulo,
    string Contenido,
    bool CapturaDatos,
    long Version,
    string ContenidoHash);

/// <summary>Una constancia de aceptacion (para el listado del admin).</summary>
public sealed record ConfidentialityAcceptanceDto(
    Guid Id,
    string Nombre,
    string Identificacion,
    string? Correo,
    DateTimeOffset AceptadoAt,
    long PolicyVersionSnapshot);

/// <summary>Datos que el admin guarda de la politica.</summary>
public sealed record ConfidentialityPolicySaveRequest(
    string Titulo,
    string Contenido,
    bool Activa,
    bool CapturaDatos);

/// <summary>Datos de una aceptacion desde el visor publico.</summary>
public sealed record ConfidentialityAcceptRequest(
    string Nombre,
    string Identificacion,
    string? Correo,
    string? Ip,
    string? UserAgent);

/// <summary>Resultado de aceptar en el visor publico (mensaje neutro ante token invalido).</summary>
public enum ConfidentialityAcceptStatus
{
    Ok = 0,
    /// <summary>Token invalido / politica inexistente o deshabilitada (mensaje neutro).</summary>
    Unavailable,
    /// <summary>Faltan nombre o identificacion.</summary>
    MissingData
}
