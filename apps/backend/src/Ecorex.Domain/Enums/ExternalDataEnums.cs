namespace Ecorex.Domain.Enums;

/// <summary>
/// Motor de base de datos de una fuente externa gobernada (ADR-0064). Acota los proveedores para los
/// que el conector sabe abrir una conexion de SOLO LECTURA parametrizada.
/// </summary>
public enum ExternalDataProvider
{
    SqlServer,
    Postgres
}

/// <summary>
/// Tipo logico de un parametro/campo de un ExternalDataSet. Gobierna la conversion segura del valor
/// antes de enlazarlo como parametro tipado (nunca por concatenacion de texto).
/// </summary>
public enum ExternalDataParameterType
{
    String,
    Int,
    Decimal,
    Date,
    Boolean,
    Guid
}

/// <summary>
/// Origen del valor de un parametro de un ExternalDataSet. Es el limite de seguridad de alcance:
/// - <see cref="Context"/>: el valor se enlaza del CONTEXTO de confianza (tenant, usuario, sucursal),
///   nunca de entrada libre. La clave de contexto la resuelve el conector.
/// - <see cref="Input"/>: parametro de reporte tipado que el usuario provee al ejecutar (fechas,
///   filtros). Se enlaza igualmente como parametro tipado (cero concatenacion).
/// </summary>
public enum ExternalDataParameterBinding
{
    Input,
    Context
}
