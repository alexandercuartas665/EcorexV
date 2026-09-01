using Ecorex.Domain.Enums;

namespace Ecorex.Application.Reporting.External;

/// <summary>
/// Un parametro DECLARADO de un <see cref="Ecorex.Domain.Entities.ExternalDataSet"/>. Curado por el
/// operador de plataforma. El conector SOLO enlaza parametros que existen en esta lista: es el limite
/// entre "consulta parametrizada gobernada" y "SQL libre".
/// </summary>
/// <param name="Name">Nombre del parametro tal como aparece en el CommandText (sin el prefijo @).</param>
/// <param name="Type">Tipo logico para la conversion segura antes de enlazar.</param>
/// <param name="Binding">De donde sale el valor: contexto de confianza o entrada tipada.</param>
/// <param name="ContextKey">Clave de contexto a resolver cuando <paramref name="Binding"/>=Context
/// (p.ej. "tenantid", "userid", "sucursal"). Ignorada para Input.</param>
/// <param name="DefaultValue">Valor por defecto (texto) para parametros Input; se usa si el runtime no
/// provee uno. Se convierte al tipo declarado igual que cualquier valor (nunca se concatena).</param>
public sealed record ExternalDataSetParameter(
    string Name,
    ExternalDataParameterType Type,
    ExternalDataParameterBinding Binding = ExternalDataParameterBinding.Input,
    string? ContextKey = null,
    string? DefaultValue = null);

/// <summary>Metadato de un campo de salida del dataset (nombre + tipo logico).</summary>
public sealed record ExternalDataSetField(string Name, ExternalDataParameterType Type);

/// <summary>Un parametro YA ENLAZADO: nombre + tipo + valor convertido. Lo consume el executor para
/// crear un parametro tipado de ADO.NET (jamas concatenacion de texto).</summary>
public sealed record ExternalBoundParameter(string Name, ExternalDataParameterType Type, object? Value);

/// <summary>
/// Una consulta externa lista para ejecutar por el <see cref="IExternalQueryExecutor"/>: proveedor +
/// cadena descifrada (en memoria) + texto curado + parametros enlazados. La cadena viaja aqui solo
/// dentro del proceso, nunca se persiste ni se loggea.
/// </summary>
public sealed record ExternalQuery(
    ExternalDataProvider Provider,
    string ConnectionString,
    string CommandText,
    IReadOnlyList<ExternalBoundParameter> Parameters,
    int MaxRows = 50_000,
    int TimeoutSeconds = 30,
    // Cuando es true (conexion propia del tenant con escritura habilitada) el ejecutor OMITE el guard de
    // solo lectura y confirma la transaccion. Por defecto false: se fuerza lectura (reportes, ADR-0064).
    bool AllowWrite = false);

/// <summary>
/// Contexto de CONFIANZA para resolver parametros de alcance (userid, sucursal, tenant...). Los valores
/// salen de aqui, no de entrada libre del reporte. <see cref="Extra"/> permite claves adicionales que la
/// capa de presentacion resuelva del principal autenticado.
/// </summary>
public sealed record ExternalRunContext(
    Guid TenantId,
    Guid? UserId = null,
    IReadOnlyDictionary<string, string?>? Extra = null);
