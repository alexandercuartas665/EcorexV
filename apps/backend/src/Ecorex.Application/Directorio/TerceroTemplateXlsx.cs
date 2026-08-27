using ClosedXML.Excel;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Genera la PLANTILLA de importacion de Terceros (Directorio) en Excel (.xlsx): hoja principal
/// "Terceros" con encabezados + filas de EJEMPLO, y HOJAS ALTERNAS de referencia con los valores
/// validos de cada campo relacional (Tipo, Perfiles, Estado, TipoId, Ciudad, Sector, Vendedor).
///
/// A diferencia de la version anterior (solo comentarios de celda), esta plantilla trae LISTAS
/// DESPLEGABLES reales: cada columna relacional apunta por validacion de datos a un RANGO CON
/// NOMBRE (lista_Tipos, lista_Perfiles, ...) alimentado desde su hoja de referencia. Las listas
/// son de AYUDA (no bloquean): ShowErrorMessage=false permite valores fuera de lista (p.ej. una
/// ciudad nueva o una combinacion de perfiles), que el importador valida despues.
///
/// Vendedores sale del catalogo VIVO de asesores del tenant (se pasa por parametro). Ciudades y
/// Sectores traen un catalogo por defecto util, ampliado con los valores que ya existan en el
/// tenant. La MISMA plantilla que se descarga es la que el importador sabe leer.
/// </summary>
public static class TerceroTemplateXlsx
{
    /// <summary>MIME de un .xlsx, para la descarga en el navegador.</summary>
    public static string Mime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Ultima fila (inclusive) a la que se extiende la validacion en la hoja "Terceros".</summary>
    private const int LastDataRow = 1000;

    /// <summary>Encabezados de la hoja "Terceros", en orden. El importador mapea por esta posicion.</summary>
    public static readonly string[] Headers =
        { "Nombre*", "Tipo", "Perfiles", "Estado", "TipoId", "NumeroId", "Ciudad", "Sector", "Cargo", "Email", "Telefono", "Vendedor" };

    /// <summary>Catalogo por defecto de ciudades (Colombia) para la lista desplegable.</summary>
    public static readonly string[] CiudadesDefault =
    {
        "Bogota", "Medellin", "Cali", "Barranquilla", "Cartagena", "Cucuta", "Bucaramanga", "Pereira",
        "Santa Marta", "Ibague", "Pasto", "Manizales", "Neiva", "Villavicencio", "Armenia", "Valledupar",
        "Monteria", "Sincelejo", "Popayan", "Palmira", "Buenaventura", "Floridablanca", "Tulua",
        "Dosquebradas", "Envigado", "Bello", "Soledad", "Soacha", "Tunja", "Riohacha"
    };

    /// <summary>Catalogo por defecto de sectores economicos para la lista desplegable.</summary>
    public static readonly string[] SectoresDefault =
    {
        "Comercio", "Industria / Manufactura", "Construccion", "Servicios", "Salud", "Educacion",
        "Tecnologia", "Agricultura", "Transporte", "Financiero", "Turismo", "Alimentos", "Textil",
        "Mineria", "Energia", "Telecomunicaciones", "Inmobiliario", "Automotriz", "Quimico", "Otros"
    };

    /// <summary>
    /// Construye la plantilla. <paramref name="vendedores"/> son los nombres de los asesores VIVOS
    /// del tenant (catalogo 000074). <paramref name="ciudades"/> / <paramref name="sectores"/> son
    /// valores que ya existen en el tenant y se FUNDEN con el catalogo por defecto (sin duplicar).
    /// </summary>
    public static byte[] Build(
        IEnumerable<string>? vendedores = null,
        IEnumerable<string>? ciudades = null,
        IEnumerable<string>? sectores = null)
    {
        using var wb = new XLWorkbook();

        var ws = wb.Worksheets.Add("Terceros");
        for (var c = 0; c < Headers.Length; c++) { ws.Cell(1, c + 1).Value = Headers[c]; }
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);

        ws.Cell(1, 1).GetComment().AddText("Obligatorio. Nombre de la empresa o de la persona.");
        ws.Cell(1, 2).GetComment().AddText("Lista desplegable (hoja 'Tipos'): Empresa o Persona.");
        ws.Cell(1, 3).GetComment().AddText("Lista desplegable (hoja 'Perfiles'). Combinables con coma (ej. Cliente, Proveedor).");
        ws.Cell(1, 4).GetComment().AddText("Lista desplegable (hoja 'Estados').");
        ws.Cell(1, 5).GetComment().AddText("Lista desplegable (hoja 'TiposId').");
        ws.Cell(1, 7).GetComment().AddText("Lista desplegable (hoja 'Ciudades'). Se admite escribir otra.");
        ws.Cell(1, 8).GetComment().AddText("Lista desplegable (hoja 'Sectores'). Aplica a empresas.");
        ws.Cell(1, 12).GetComment().AddText("Lista desplegable (hoja 'Vendedores'): asesor asignado.");

        void Row(int r, params string[] vals)
        {
            for (var c = 0; c < vals.Length; c++) { ws.Cell(r, c + 1).Value = vals[c]; }
        }
        Row(2, "Comercializadora Ejemplo S.A.S", nameof(TerceroTipo.Empresa), "Cliente, Proveedor",
            nameof(TerceroEstado.Activo), nameof(TerceroIdTipo.Nit), "900123456-7", "Bogota", "Comercio",
            "", "contacto@ejemplo.com", "6011234567", "");
        Row(3, "Maria Gomez", nameof(TerceroTipo.Persona), "Cliente",
            nameof(TerceroEstado.Prospecto), nameof(TerceroIdTipo.Identificacion), "52123456", "Medellin", "",
            "Gerente", "maria@ejemplo.com", "3001234567", "");

        // ---- Hojas de referencia + rangos con nombre para las listas desplegables ----
        var tipos = Enum.GetNames<TerceroTipo>();
        var perfiles = Enum.GetNames<TerceroPerfil>().Where(n => n != nameof(TerceroPerfil.Ninguno))
            .Append("Cliente, Proveedor").ToArray();
        var estados = Enum.GetNames<TerceroEstado>();
        var tiposId = Enum.GetNames<TerceroIdTipo>();
        var ciudadesFull = MergeCatalog(CiudadesDefault, ciudades);
        var sectoresFull = MergeCatalog(SectoresDefault, sectores);
        var vendedoresFull = MergeCatalog(new[] { "(Sin asignar)" }, vendedores);

        AddRefSheet(wb, "Tipos", "Tipo de tercero", tipos, "lista_Tipos");
        AddRefSheet(wb, "Perfiles", "Perfil (combinable con coma)", perfiles, "lista_Perfiles");
        AddRefSheet(wb, "Estados", "Estado", estados, "lista_Estados");
        AddRefSheet(wb, "TiposId", "Tipo de identificacion", tiposId, "lista_TiposId");
        AddRefSheet(wb, "Ciudades", "Ciudad", ciudadesFull, "lista_Ciudades");
        AddRefSheet(wb, "Sectores", "Sector economico", sectoresFull, "lista_Sectores");
        AddRefSheet(wb, "Vendedores", "Vendedor / Comercial", vendedoresFull, "lista_Vendedores");

        Dropdown(ws, 2, "lista_Tipos");      // Tipo
        Dropdown(ws, 3, "lista_Perfiles");   // Perfiles
        Dropdown(ws, 4, "lista_Estados");    // Estado
        Dropdown(ws, 5, "lista_TiposId");    // TipoId
        Dropdown(ws, 7, "lista_Ciudades");   // Ciudad
        Dropdown(ws, 8, "lista_Sectores");   // Sector
        Dropdown(ws, 12, "lista_Vendedores"); // Vendedor

        ws.Columns().AdjustToContents();
        return ToBytes(wb);
    }

    /// <summary>Aplica una lista desplegable de AYUDA (no bloqueante) a la columna <paramref name="col"/>
    /// desde la fila 2 hasta <see cref="LastDataRow"/>, apuntando al rango con nombre indicado.</summary>
    private static void Dropdown(IXLWorksheet ws, int col, string namedRange)
    {
        var range = ws.Range(2, col, LastDataRow, col);
        var dv = range.CreateDataValidation();
        dv.List("=" + namedRange, true);
        dv.IgnoreBlanks = true;
        dv.ShowErrorMessage = false; // ayuda, no bloqueo: el importador valida despues
    }

    /// <summary>Crea una hoja de referencia (encabezado + valores) y la registra como rango con nombre.</summary>
    private static void AddRefSheet(XLWorkbook wb, string sheet, string header, string[] values, string namedRange)
    {
        var ws = wb.Worksheets.Add(sheet);
        ws.Cell(1, 1).Value = header;
        ws.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var v in values) { ws.Cell(r++, 1).Value = v; }
        var last = Math.Max(2, values.Length + 1);
        ws.Range(2, 1, last, 1).AddToNamed(namedRange, XLScope.Workbook);
        ws.Columns().AdjustToContents();
    }

    /// <summary>Une el catalogo por defecto con los valores vivos del tenant, sin duplicar (case-insensitive)
    /// y sin vacios; conserva el orden (defaults primero, luego los nuevos ordenados).</summary>
    private static string[] MergeCatalog(string[] defaults, IEnumerable<string>? extra)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var v in defaults)
        {
            if (!string.IsNullOrWhiteSpace(v) && seen.Add(v.Trim())) { result.Add(v.Trim()); }
        }
        if (extra is not null)
        {
            foreach (var v in extra.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                         .OrderBy(x => x, StringComparer.CurrentCulture))
            {
                if (seen.Add(v)) { result.Add(v); }
            }
        }
        return result.ToArray();
    }

    private static byte[] ToBytes(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
