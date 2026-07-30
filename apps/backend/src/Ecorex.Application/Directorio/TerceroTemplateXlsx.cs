using ClosedXML.Excel;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Genera la PLANTILLA de importacion de Terceros (Directorio) en Excel (.xlsx): hoja principal
/// "Terceros" con encabezados + filas de EJEMPLO, y HOJAS ALTERNAS de referencia con los valores
/// validos de los campos relacionales (Tipo, Perfiles, Estado, Tipo de identificacion), que son
/// ENUMS fijos del dominio. La Ciudad es texto libre (no lleva hoja de codigos).
/// </summary>
public static class TerceroTemplateXlsx
{
    /// <summary>MIME de un .xlsx, para la descarga en el navegador.</summary>
    public static string Mime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Build()
    {
        using var wb = new XLWorkbook();

        var ws = wb.Worksheets.Add("Terceros");
        string[] headers = { "Nombre*", "Tipo", "Perfiles", "Estado", "TipoId", "NumeroId", "Ciudad", "Sector", "Cargo", "Email", "Telefono", "Vendedor" };
        for (var c = 0; c < headers.Length; c++) { ws.Cell(1, c + 1).Value = headers[c]; }
        ws.Row(1).Style.Font.Bold = true;

        ws.Cell(1, 2).GetComment().AddText("Ver hoja 'Tipos' (Empresa o Persona).");
        ws.Cell(1, 3).GetComment().AddText("Ver hoja 'Perfiles'. Combinables con coma (ej. Cliente, Proveedor).");
        ws.Cell(1, 4).GetComment().AddText("Ver hoja 'Estados'.");
        ws.Cell(1, 5).GetComment().AddText("Ver hoja 'TiposId'.");

        void Row(int r, params string[] vals)
        {
            for (var c = 0; c < vals.Length; c++) { ws.Cell(r, c + 1).Value = vals[c]; }
        }
        Row(2, "Comercializadora Ejemplo S.A.S", nameof(TerceroTipo.Empresa), "Cliente, Proveedor",
            nameof(TerceroEstado.Activo), nameof(TerceroIdTipo.Nit), "900123456-7", "Bogota", "Comercio",
            "", "contacto@ejemplo.com", "6011234567", "Juan Perez");
        Row(3, "Maria Gomez", nameof(TerceroTipo.Persona), "Cliente",
            nameof(TerceroEstado.Prospecto), nameof(TerceroIdTipo.Identificacion), "52123456", "Medellin", "",
            "Gerente", "maria@ejemplo.com", "3001234567", "");

        ws.Columns().AdjustToContents();

        AddEnumSheet(wb, "Tipos", "Tipo de tercero", Enum.GetNames<TerceroTipo>());
        AddEnumSheet(wb, "Perfiles", "Perfil (combinable con coma)",
            Enum.GetNames<TerceroPerfil>().Where(n => n != nameof(TerceroPerfil.Ninguno)).ToArray());
        AddEnumSheet(wb, "Estados", "Estado", Enum.GetNames<TerceroEstado>());
        AddEnumSheet(wb, "TiposId", "Tipo de identificacion", Enum.GetNames<TerceroIdTipo>());

        return ToBytes(wb);
    }

    private static void AddEnumSheet(XLWorkbook wb, string sheet, string header, string[] values)
    {
        var ws = wb.Worksheets.Add(sheet);
        ws.Cell(1, 1).Value = header;
        ws.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var v in values) { ws.Cell(r++, 1).Value = v; }
        ws.Columns().AdjustToContents();
    }

    private static byte[] ToBytes(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
