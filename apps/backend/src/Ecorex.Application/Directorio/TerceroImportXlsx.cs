using ClosedXML.Excel;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Lee la plantilla de importacion de Terceros (hoja "Terceros" que genera
/// <see cref="TerceroTemplateXlsx"/>) y la convierte en filas tipadas listas para dar de alta con
/// <c>ITerceroService.CreateAsync</c>. Valida por fila (no aborta todo el archivo por un error): cada
/// fila trae su <see cref="TerceroImportRow.Error"/> si no se puede importar, y el resto sigue.
///
/// Multi-tenant: NO toca la BD; solo parsea. La resolucion de Vendedor -> asesor se hace con el
/// diccionario que pasa la UI (nombres del catalogo VIVO del tenant), asi el parser queda puro.
/// </summary>
public static class TerceroImportXlsx
{
    /// <summary>Fila parseada de la plantilla. <see cref="Error"/> != null => no se importa.</summary>
    public sealed record TerceroImportRow(
        int RowNumber,
        string Nombre,
        TerceroTipo Tipo,
        TerceroPerfil Perfiles,
        TerceroEstado Estado,
        TerceroIdTipo IdTipo,
        string? NumeroId,
        string? Ciudad,
        string? Sector,
        string? Cargo,
        string? Email,
        string? Telefono,
        string? Vendedor,
        Guid? VendedorAsesorId,
        string? Error)
    {
        public bool IsValid => Error is null;

        /// <summary>Arma el request de alta a partir de la fila (solo se llama si es valida).</summary>
        public SaveTerceroRequest ToRequest() => new(
            Nombre: Nombre,
            Tipo: Tipo,
            Perfiles: Perfiles,
            Estado: Estado,
            Vendedor: VendedorAsesorId is null ? Vendedor : null,
            Ciudad: Ciudad,
            IdTipo: IdTipo,
            IdValor: NumeroId,
            Sector: Tipo == TerceroTipo.Empresa ? Sector : null,
            Cargo: Tipo == TerceroTipo.Persona ? Cargo : null,
            Email: Email,
            Telefono: Telefono,
            VendedorAsesorId: VendedorAsesorId);
    }

    /// <summary>Resultado global del parseo.</summary>
    public sealed record TerceroImportParse(
        IReadOnlyList<TerceroImportRow> Rows,
        string? FatalError)
    {
        public int Total => Rows.Count;
        public int Valid => Rows.Count(r => r.IsValid);
        public int Invalid => Rows.Count(r => !r.IsValid);
    }

    /// <summary>
    /// Parsea el .xlsx. <paramref name="asesoresByName"/> mapea nombre de asesor (normalizado por el
    /// parser) -> Id, para resolver la columna Vendedor. Filas totalmente vacias se ignoran.
    /// </summary>
    public static TerceroImportParse Parse(Stream xlsx, IReadOnlyDictionary<string, Guid>? asesoresByName = null)
    {
        var asesores = BuildAsesorIndex(asesoresByName);

        XLWorkbook wb;
        try { wb = new XLWorkbook(xlsx); }
        catch (Exception ex) { return new TerceroImportParse(Array.Empty<TerceroImportRow>(), "No se pudo leer el archivo Excel: " + ex.Message); }

        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => string.Equals(s.Name, "Terceros", StringComparison.OrdinalIgnoreCase))
                     ?? wb.Worksheets.FirstOrDefault();
            if (ws is null) { return new TerceroImportParse(Array.Empty<TerceroImportRow>(), "El archivo no tiene hojas."); }

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var rows = new List<TerceroImportRow>();
            for (var r = 2; r <= lastRow; r++)
            {
                string Cell(int c) => ws.Cell(r, c).GetString().Trim();

                var nombre = Cell(1);
                var tipoRaw = Cell(2);
                var perfilesRaw = Cell(3);
                var estadoRaw = Cell(4);
                var tipoIdRaw = Cell(5);
                var numeroId = Cell(6);
                var ciudad = Cell(7);
                var sector = Cell(8);
                var cargo = Cell(9);
                var email = Cell(10);
                var telefono = Cell(11);
                var vendedorRaw = Cell(12);

                // Fila vacia (ni nombre ni ningun otro dato): se ignora en silencio.
                if (string.IsNullOrWhiteSpace(nombre) && string.IsNullOrWhiteSpace(tipoRaw) &&
                    string.IsNullOrWhiteSpace(perfilesRaw) && string.IsNullOrWhiteSpace(numeroId) &&
                    string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(telefono) &&
                    string.IsNullOrWhiteSpace(vendedorRaw))
                {
                    continue;
                }

                string? error = null;

                if (string.IsNullOrWhiteSpace(nombre)) { error = "Falta el nombre (obligatorio)."; }

                var tipo = ParseEnum(tipoRaw, TerceroTipo.Empresa, ref error, "Tipo");
                var estado = ParseEnum(estadoRaw, TerceroEstado.Activo, ref error, "Estado");
                var perfiles = ParsePerfiles(perfilesRaw, ref error);

                var idTipo = ParseIdTipo(tipoIdRaw, numeroId, ref error);

                Guid? vendedorAsesorId = null;
                string? vendedor = null;
                if (!string.IsNullOrWhiteSpace(vendedorRaw) &&
                    !string.Equals(vendedorRaw, "(Sin asignar)", StringComparison.OrdinalIgnoreCase))
                {
                    if (asesores.TryGetValue(Norm(vendedorRaw), out var aid)) { vendedorAsesorId = aid; }
                    else { vendedor = vendedorRaw; } // no existe como asesor -> se guarda como texto legado
                }

                rows.Add(new TerceroImportRow(
                    r, nombre, tipo, perfiles, estado, idTipo,
                    Nz(numeroId), Nz(ciudad), Nz(sector), Nz(cargo), Nz(email), Nz(telefono),
                    vendedor, vendedorAsesorId, error));
            }

            return new TerceroImportParse(rows, null);
        }
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static TEnum ParseEnum<TEnum>(string raw, TEnum fallback, ref string? error, string field)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw)) { return fallback; }
        if (Enum.TryParse<TEnum>(raw, ignoreCase: true, out var v) && Enum.IsDefined(v)) { return v; }
        error ??= $"{field} no valido: '{raw}'.";
        return fallback;
    }

    private static TerceroPerfil ParsePerfiles(string raw, ref string? error)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return TerceroPerfil.Ninguno; }
        var acc = TerceroPerfil.Ninguno;
        foreach (var token in raw.Split(new[] { ',', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<TerceroPerfil>(token, ignoreCase: true, out var p) && p != TerceroPerfil.Ninguno)
            {
                acc |= p;
            }
            else
            {
                error ??= $"Perfil no valido: '{token}'.";
            }
        }
        return acc;
    }

    /// <summary>Tipo de identificacion: si viene en blanco, se infiere (Nit si hay numero, Ninguno si no).</summary>
    private static TerceroIdTipo ParseIdTipo(string raw, string numeroId, ref string? error)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.IsNullOrWhiteSpace(numeroId) ? TerceroIdTipo.Ninguno : TerceroIdTipo.Nit;
        }
        if (Enum.TryParse<TerceroIdTipo>(raw, ignoreCase: true, out var v) && Enum.IsDefined(v)) { return v; }
        error ??= $"TipoId no valido: '{raw}'.";
        return string.IsNullOrWhiteSpace(numeroId) ? TerceroIdTipo.Ninguno : TerceroIdTipo.Nit;
    }

    private static Dictionary<string, Guid> BuildAsesorIndex(IReadOnlyDictionary<string, Guid>? src)
    {
        var d = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (src is null) { return d; }
        foreach (var kv in src)
        {
            var key = Norm(kv.Key);
            if (!string.IsNullOrEmpty(key)) { d[key] = kv.Value; }
        }
        return d;
    }

    private static string Norm(string s) => s.Trim().ToLowerInvariant();
}
