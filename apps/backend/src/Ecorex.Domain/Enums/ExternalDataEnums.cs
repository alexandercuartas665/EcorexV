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
/// - <see cref="RowLimit"/>: parametro que acota el NUMERO DE FILAS (p.ej. TOP/LIMIT). En un REPORTE se
///   enlaza al tope duro del sistema (ExternalQuery.MaxRows), NO al DefaultValue de autoria: asi el default
///   pensado para probar en el editor no capa la salida de un panel. En la consola "Ejecutar" del editor
///   sigue tomando el valor que teclee el usuario (o su DefaultValue).
/// </summary>
public enum ExternalDataParameterBinding
{
    Input,
    Context,
    RowLimit
}
