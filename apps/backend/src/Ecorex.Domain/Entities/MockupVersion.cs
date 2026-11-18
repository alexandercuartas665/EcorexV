using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Version INMUTABLE de una maqueta (ADR-0179): el ZIP subido vive en el gestor unificado de archivos
/// (<see cref="ZipFileId"/>) y se descomprime bajo demanda a una cache en disco para servirlo.
/// </summary>
public class MockupVersion : TenantEntity
{
    public Guid MockupProjectId { get; set; }

    public MockupProject? MockupProject { get; set; }

    /// <summary>Numero correlativo dentro del proyecto (1, 2, 3...).</summary>
    public int Number { get; set; }

    /// <summary>Id del ZIP en el gestor unificado de archivos (modulo "maqueta-sitio").</summary>
    public Guid ZipFileId { get; set; }

    public long SizeBytes { get; set; }

    public int FileCount { get; set; }

    /// <summary>Prefijo de carpeta dentro del ZIP que se quita al servir (si el ZIP trae una carpeta raiz unica).</summary>
    public string? RootPrefix { get; set; }

    /// <summary>Pagina que abre el launcher: "anotador/anotador.html" si viene el kit, si no "index.html".</summary>
    public string EntryPath { get; set; } = "index.html";

    public bool HasAnotador { get; set; }

    public string? Notes { get; set; }

    public string? UploadedByName { get; set; }
}
