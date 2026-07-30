using System.Data;
using System.Text;
using BoldReports.Web.ReportViewer;
using Ecorex.Application.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Ecorex.SuperAdmin.Controllers;

/// <summary>
/// Web Reporting API de Bold Reports (Motor de Reportes, Ola 2, ADR-0051). Procesa el RDL para el
/// visor. La clave: los datos NO salen de una cadena de conexion; en <see cref="OnInitReportOptions"/>
/// se cargan las filas YA FILTRADAS POR TENANT desde <see cref="IReportDefinitionService"/> (que pasa
/// por el datasource tenant-safe y el filtro global del DbContext) y se inyectan como
/// <see cref="ReportDataSource"/>. El tenant lo resuelve la cookie del request (AmbientTenantContext),
/// asi que un tenant nunca ve el RDL ni los datos de otro.
/// </summary>
[Authorize(Policy = "TenantMember")]
[Route("api/{controller}/{action}/{id?}")]
public sealed class BoldReportsApiController : Controller, IReportController
{
    // Nombre del DATA SOURCE embebido en el RDL (ReportSpecToRdl.DataSourceName). El ReportDataSource
    // inyectado debe llamarse EXACTAMENTE asi para que Bold use los datos en memoria (no el dataset).
    private const string DataSourceName = "EcorexTenantSafe";

    private readonly IMemoryCache _cache;
    private readonly IReportDefinitionService _definitions;

    public BoldReportsApiController(IMemoryCache cache, IReportDefinitionService definitions)
    {
        _cache = cache;
        _definitions = definitions;
    }

    [ActionName("GetResource")]
    [AcceptVerbs("GET")]
    public object GetResource(ReportResource resource) => ReportHelper.GetResource(resource, this, _cache);

    [HttpPost]
    public object PostReportAction([FromBody] Dictionary<string, object> jsonArray) =>
        ReportHelper.ProcessReport(jsonArray, this, _cache);

    [HttpPost]
    public object PostFormReportAction() => ReportHelper.ProcessReport(null, this, _cache);

    // ---- Hooks de Bold ----

    [NonAction]
    public void OnInitReportOptions(ReportViewerOptions reportOption)
    {
        // El reportPath que envia el visor es el Id de la ReportDefinition (imprimible) del tenant.
        if (!Guid.TryParse(reportOption.ReportModel.ReportPath, out var definitionId))
        {
            return;
        }

        var printable = _definitions.GetPrintableAsync(definitionId).GetAwaiter().GetResult();
        if (printable is null)
        {
            return;
        }

        // Modo LOCAL: es la UNICA forma en que Bold usa los datos inyectados en vez de conectar a BD.
        reportOption.ReportModel.ProcessingMode = ProcessingMode.Local;

        // RDL como stream.
        reportOption.ReportModel.Stream = new MemoryStream(Encoding.UTF8.GetBytes(printable.Rdl));

        // Datos ya filtrados por tenant, inyectados como data source en memoria (nunca connection string).
        reportOption.ReportModel.DataSources.Clear();
        reportOption.ReportModel.DataSources.Add(new BoldReports.Web.ReportDataSource
        {
            Name = DataSourceName,
            Value = ToDataTable(printable.DataSet)
        });
    }

    [NonAction]
    public void OnReportLoaded(ReportViewerOptions reportOption)
    {
    }

    // ---- Helpers ----

    private static DataTable ToDataTable(ReportDataSet ds)
    {
        var table = new DataTable(DataSourceName);
        foreach (var col in ds.Columns)
        {
            table.Columns.Add(new DataColumn(col.Key, ClrType(col.Type)) { AllowDBNull = true });
        }

        foreach (var row in ds.Rows)
        {
            var values = new object[ds.Columns.Count];
            for (var i = 0; i < ds.Columns.Count; i++)
            {
                values[i] = Coerce(i < row.Count ? row[i] : null, ds.Columns[i].Type);
            }

            table.Rows.Add(values);
        }

        return table;
    }

    private static Type ClrType(ReportFieldType type) => type switch
    {
        ReportFieldType.Number => typeof(long),
        ReportFieldType.Decimal => typeof(decimal),
        ReportFieldType.Date => typeof(DateTime),
        ReportFieldType.Boolean => typeof(bool),
        _ => typeof(string)
    };

    private static object Coerce(object? value, ReportFieldType type)
    {
        if (value is null)
        {
            return DBNull.Value;
        }

        return type switch
        {
            ReportFieldType.Number => value is long l ? l : Convert.ToInt64(value),
            ReportFieldType.Decimal => value is decimal d ? d : Convert.ToDecimal(value),
            ReportFieldType.Date => value switch
            {
                DateTimeOffset dto => dto.UtcDateTime,
                DateTime dt => dt,
                _ => DBNull.Value
            },
            ReportFieldType.Boolean => value is bool b ? b : Convert.ToBoolean(value),
            _ => value.ToString() ?? string.Empty
        };
    }
}
