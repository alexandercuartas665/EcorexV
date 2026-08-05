namespace Ecorex.Application.Reporting.External;

/// <summary>
/// Ejecutor de bajo nivel del conector externo (ADR-0064). Abre una conexion de SOLO LECTURA contra la
/// base externa (segun el proveedor), ejecuta el comando CURADO con parametros TIPADOS (cero
/// concatenacion) y devuelve un <see cref="ReportDataSet"/> neutro. La implementacion vive en
/// Infrastructure (tiene los drivers ADO.NET) y FUERZA la lectura: transaccion/aplicacion de solo
/// lectura + solo se permite ejecutar SELECT. Nunca persiste ni loggea la cadena de conexion.
/// </summary>
public interface IExternalQueryExecutor
{
    /// <summary>Ejecuta la consulta parametrizada de solo lectura y materializa las filas.</summary>
    Task<ReportDataSet> ExecuteAsync(ExternalQuery query, CancellationToken ct = default);

    /// <summary>
    /// Prueba de conexion de SOLO LECTURA: abre la conexion y corre un "SELECT 1" parametrizado. Devuelve
    /// null si OK, o un mensaje de error legible (sin filtrar la cadena de conexion) si fallo.
    /// </summary>
    Task<string?> TestConnectionAsync(Ecorex.Domain.Enums.ExternalDataProvider provider, string connectionString, CancellationToken ct = default);
}
