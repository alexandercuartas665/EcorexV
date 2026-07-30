using System.Data;
using System.Text;
using BoldReports.Web.ReportDesigner;
using BoldReports.Web.ReportViewer;
using Ecorex.Application.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Ecorex.SuperAdmin.Controllers;

/// <summary>
/// Web API del DISENIADOR Bold Reports (Motor de Reportes, Ola 2, ADR-0051). Permite editar el RDL de
/// una ReportDefinition en el diseniador drag-drop y guardarlo de vuelta. El "itemId" que maneja el
/// diseniador es el Id de la ReportDefinition (tenant-scoped): abrir carga su RDL (GetData), guardar lo
/// persiste (SetData -> UpdateRdlAsync). <see cref="IReportDesignerController"/> hereda de
/// <see cref="IReportController"/>, asi que tambien implementa el visor para la vista previa del
/// diseniador: la previa inyecta los datos YA FILTRADOS POR TENANT en ProcessingMode.Local (nunca una
/// cadena de conexion). Tenant resuelto por la cookie (AmbientTenantContext).
/// </summary>
[Authorize(Policy = "Perm:reportes/imprimibles:Edit")]
[Route("api/{controller}/{action}")]
public sealed class BoldReportsDesignerController : Controller, IReportDesignerController
{
    private const string DataSourceName = "EcorexTenantSafe";

    private readonly IMemoryCache _cache;
    private readonly IReportDefinitionService _definitions;

    public BoldReportsDesignerController(IMemoryCache cache, IReportDefinitionService definitions)
    {
        _cache = cache;
        _definitions = definitions;
    }

    // ---- Diseniador ----

    [HttpPost]
    public object PostDesignerAction([FromBody] Dictionary<string, object> jsonResult) =>
        ReportDesignerHelper.ProcessDesigner(jsonResult, this, null, _cache);

    [HttpPost]
    public object PostFormDesignerAction() =>
        ReportDesignerHelper.ProcessDesigner(null, this, HttpContext.Request.Form.Files.Count > 0 ? HttpContext.Request.Form.Files[0] : null, _cache);

    [HttpPost]
    public void UploadReportAction() =>
        ReportDesignerHelper.ProcessDesigner(null, this, HttpContext.Request.Form.Files.Count > 0 ? HttpContext.Request.Form.Files[0] : null, _cache);

    [ActionName("GetImage")]
    [AcceptVerbs("GET")]
    public object GetImage(string key, string image) => ReportDesignerHelper.GetImage(key, image, this);

    // GetData/SetData son el almacen de ARTEFACTOS DE SESION del diseniador (setting.txt, imagenes,
    // estado intermedio), NO el reporte final. Bold los pide con claves (key,itemId) arbitrarias; si
    // se devuelve algo invalido, ProcessDesigner lanza NRE. Se guardan como archivos en un temp por
    // sesion. El reporte final se carga/guarda por separado (openReportDefinition / endpoint RDL).

    private static string SessionDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ecorex-bold-designer");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string PathFor(string key, string itemId)
    {
        var raw = (key ?? string.Empty) + "__" + (itemId ?? string.Empty);
        var safe = string.Concat(raw.Split(Path.GetInvalidFileNameChars()));
        if (safe.Length > 150)
        {
            safe = safe.Substring(0, 150) + "_" + (uint)raw.GetHashCode();
        }

        return Path.Combine(SessionDir(), safe);
    }

    [NonAction]
    public ResourceInfo GetData(string key, string itemId)
    {
        var info = new ResourceInfo();
        try
        {
            var p = PathFor(key, itemId);
            if (System.IO.File.Exists(p))
            {
                info.Data = System.IO.File.ReadAllBytes(p);
            }
        }
        catch
        {
            // Un fallo de lectura del temp no debe tumbar al diseniador.
        }

        return info;
    }

    [NonAction]
    public bool SetData(string key, string itemId, ItemInfo itemData, out string errorMessage)
    {
        errorMessage = string.Empty;
        try
        {
            if (itemData?.Data is not null)
            {
                System.IO.File.WriteAllBytes(PathFor(key, itemId), itemData.Data);
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    // ---- Visor (para la vista previa del diseniador): IReportDesignerController : IReportController ----

    [ActionName("GetResource")]
    [AcceptVerbs("GET")]
    public object GetResource(ReportResource resource) => ReportHelper.GetResource(resource, this, _cache);

    [HttpPost]
    public object PostReportAction([FromBody] Dictionary<string, object> jsonArray) =>
        ReportHelper.ProcessReport(jsonArray, this, _cache);

    [HttpPost]
    public object PostFormReportAction() => ReportHelper.ProcessReport(null, this, _cache);

    [NonAction]
    public void OnInitReportOptions(ReportViewerOptions reportOption)
    {
        if (!Guid.TryParse(reportOption.ReportModel.ReportPath, out var id))
        {
            return;
        }

        var printable = _definitions.GetPrintableAsync(id).GetAwaiter().GetResult();
        if (printable is null)
        {
            return;
        }

        reportOption.ReportModel.ProcessingMode = ProcessingMode.Local;
        reportOption.ReportModel.Stream = new MemoryStream(Encoding.UTF8.GetBytes(printable.Rdl));
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
