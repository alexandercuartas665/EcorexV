using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

/// <summary>
/// Implementacion del modulo Conciliacion DIAN Compras. Fase 1: fuentes DUMMY internas + logica real de
/// Importar/Cruzar. Multi-tenant por el filtro global (el alta estampa TenantId del contexto).
/// </summary>
public sealed class ConciliacionDianService : IConciliacionDianService
{
    private const string MarcaInconsistencia = "Inconsistencia!";

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly INewtonEventSender? _sender;

    public ConciliacionDianService(IApplicationDbContext db, ITenantContext tenant, INewtonEventSender? sender = null)
    {
        _db = db;
        _tenant = tenant;
        _sender = sender;
    }

    // Normaliza un numero de factura para el cruce: deja solo letras/digitos, en mayuscula (el molde quitaba
    // guiones; esto es un poco mas robusto ante espacios/puntos).
    private static string Norm(string? s) =>
        new string((s ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    // ---- Documentos ----

    public async Task<IReadOnlyList<ConciliacionDianDocumentoDto>> ListDocumentosAsync(CancellationToken ct = default) =>
        (await _db.ConciliacionDianDocumentos.AsNoTracking()
            .OrderByDescending(d => d.Anio).ThenByDescending(d => d.Mes)
            .ToListAsync(ct))
            .Select(MapDoc).ToList();

    public async Task<ConciliacionDianDocumentoDto?> GetDocumentoAsync(Guid documentoId, CancellationToken ct = default)
    {
        var d = await _db.ConciliacionDianDocumentos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == documentoId, ct);
        return d is null ? null : MapDoc(d);
    }

    public async Task<(ConciliacionDianDocumentoDto? Doc, string? Error)> CrearDocumentoAsync(int anio, int mes, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return (null, "No hay tenant activo."); }
        if (mes is < 1 or > 12) { return (null, "Mes invalido."); }
        if (anio is < 2000 or > 2100) { return (null, "Anio invalido."); }
        if (await _db.ConciliacionDianDocumentos.AnyAsync(d => d.Anio == anio && d.Mes == mes, ct))
        {
            return (null, $"Ya existe un documento de conciliacion para {mes:00}/{anio}.");
        }
        var n = await _db.ConciliacionDianDocumentos.CountAsync(ct) + 1;
        var doc = new ConciliacionDianDocumento
        {
            TenantId = tenantId,
            Consecutivo = $"CCD-{n:0000}",
            Anio = anio,
            Mes = mes,
            Estado = "ABIERTO",
        };
        _db.ConciliacionDianDocumentos.Add(doc);
        await _db.SaveChangesAsync(ct);
        return (MapDoc(doc), null);
    }

    // ---- Renglones ----

    public async Task<IReadOnlyList<ConciliacionDianRenglonDto>> ListRenglonesAsync(
        Guid documentoId, EstadoConciliacionDian estado, ConciliacionDianFiltro filtro, CancellationToken ct = default)
    {
        var rows = await _db.ConciliacionDianRenglones.AsNoTracking()
            .Where(r => r.DocumentoId == documentoId && r.Estado == estado)
            .OrderBy(r => r.FechaEmision).ThenBy(r => r.NombreProveedor)
            .ToListAsync(ct);
        return rows.Where(r => PasaFiltro(r, filtro)).Select(MapRenglon).ToList();
    }

    public async Task<IReadOnlyDictionary<EstadoConciliacionDian, int>> ContarPorEstadoAsync(
        Guid documentoId, ConciliacionDianFiltro filtro, CancellationToken ct = default)
    {
        var rows = await _db.ConciliacionDianRenglones.AsNoTracking()
            .Where(r => r.DocumentoId == documentoId)
            .ToListAsync(ct);
        return rows.Where(r => PasaFiltro(r, filtro))
            .GroupBy(r => r.Estado)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    // Filtro en memoria (volumenes dummy pequenos; el rango de documento por string no es traducible a SQL
    // de forma portable entre PG y SQL Server). En Fase 2, con datos reales, se movera a SQL parametrizado.
    private static bool PasaFiltro(ConciliacionDianRenglon r, ConciliacionDianFiltro f)
    {
        if (!string.IsNullOrWhiteSpace(f.Proveedor))
        {
            var q = f.Proveedor.Trim();
            if (!(r.NombreProveedor.Contains(q, StringComparison.OrdinalIgnoreCase)
                  || r.NitProveedor.Contains(q, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }
        if (f.Desde is { } d && r.FechaEmision < d) { return false; }
        if (f.Hasta is { } h && r.FechaEmision > h) { return false; }
        if (!string.IsNullOrWhiteSpace(f.DocDesde)
            && string.Compare(r.NumFacturaProveedor, f.DocDesde.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(f.DocHasta)
            && string.Compare(r.NumFacturaProveedor, f.DocHasta.Trim(), StringComparison.OrdinalIgnoreCase) > 0)
        {
            return false;
        }
        return true;
    }

    // ---- Acciones ----

    public async Task<int> ImportarDesdeBotAsync(Guid documentoId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return 0; }
        var doc = await _db.ConciliacionDianDocumentos.FirstOrDefaultAsync(d => d.Id == documentoId, ct);
        if (doc is null) { return 0; }

        // Facturas del bot DIAN (dummy) del periodo del CCD.
        var bot = await _db.ConciliacionDianBotDummies.AsNoTracking()
            .Where(b => b.FechaDoc.Year == doc.Anio && b.FechaDoc.Month == doc.Mes && b.Cufe != "")
            .ToListAsync(ct);
        if (bot.Count == 0) { return 0; }

        // Idempotente: no re-insertar CUFE ya presente en este documento (no pisa aprobaciones/estados hechos).
        var existentes = await _db.ConciliacionDianRenglones
            .Where(r => r.DocumentoId == documentoId)
            .Select(r => r.Cufe)
            .ToListAsync(ct);
        var yaHay = existentes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nuevos = 0;
        foreach (var b in bot)
        {
            if (yaHay.Contains(b.Cufe)) { continue; }
            _db.ConciliacionDianRenglones.Add(new ConciliacionDianRenglon
            {
                TenantId = tenantId,
                DocumentoId = documentoId,
                Estado = EstadoConciliacionDian.Conciliar,
                Cufe = b.Cufe,
                TipoDocDian = b.TipoDoc,
                FechaEmision = b.FechaDoc,
                NombreProveedor = b.NombreEmisor,
                NitProveedor = b.NitEmisor,
                NumFacturaProveedor = b.PrefijoFolio,
                NumFacturaSoldarco = string.Empty,
                OrdenCompraSoldarco = b.IdCompra,
                SubtotalBruto = b.SubtotalBruto,
                DescuentoComercial = b.DescuentoComercial,
                SubtotalNeto = b.Subtotal,
                IvaDescontable = b.Iva,
                TotalAntesRetenciones = b.TotalAntesRet,
                RetRetefuente = b.RetencionFuente,
                RetIca = b.RetencionIca,
                TotalFactura = b.Total,
                TipoPago = b.TipoPago,
            });
            yaHay.Add(b.Cufe);
            nuevos++;
        }
        if (nuevos > 0) { await _db.SaveChangesAsync(ct); }
        return nuevos;
    }

    public async Task<ConciliacionCruceResult> CruzarAsync(Guid documentoId, CancellationToken ct = default)
    {
        var renglones = await _db.ConciliacionDianRenglones
            .Where(r => r.DocumentoId == documentoId
                        && (r.Estado == EstadoConciliacionDian.Conciliar || r.Estado == EstadoConciliacionDian.Conciliado))
            .ToListAsync(ct);
        if (renglones.Count == 0) { return new(0, 0, 0); }

        // ERP (dummy): referencia normalizada -> documentos internos distintos.
        var erp = await _db.ConciliacionDianErpRefDummies.AsNoTracking().ToListAsync(ct);
        var erpLookup = erp
            .GroupBy(e => Norm(e.Referencia))
            .ToDictionary(g => g.Key, g => g.Select(x => x.DocumentoInterno).Distinct().ToList());

        // NEWTON (dummy): CUFE con representacion grafica (GuidPdf) -> habilita plataforma.
        var conPlataforma = (await _db.ConciliacionDianNewtonDummies.AsNoTracking()
                .Where(n => n.GuidPdf != "")
                .Select(n => n.Cufe)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int cruzadas = 0, inconsistencias = 0, autoAprobadas = 0;
        foreach (var r in renglones)
        {
            var key = Norm(r.NumFacturaProveedor);
            if (erpLookup.TryGetValue(key, out var docs) && docs.Count > 0)
            {
                if (docs.Count == 1)
                {
                    r.NumFacturaSoldarco = docs[0];
                    cruzadas++;
                    // Auto-aprueba solo si el usuario NO toco el toggle (replica el "estaba vacio" del molde).
                    if (!r.AprobadaManual && !r.FacturaAprobada)
                    {
                        r.FacturaAprobada = true;
                        autoAprobadas++;
                    }
                }
                else
                {
                    r.NumFacturaSoldarco = MarcaInconsistencia;
                    inconsistencias++;
                }
            }
            // 0 coincidencias: se deja como estaba (no borra un cruce/decision previa).

            r.PlataformaProveedor = conPlataforma.Contains(r.Cufe);
            RecomputeEstado(r);
        }
        await _db.SaveChangesAsync(ct);
        return new(cruzadas, inconsistencias, autoAprobadas);
    }

    public async Task UpdateOrdenCompraAsync(Guid renglonId, string ordenCompra, CancellationToken ct = default)
    {
        var r = await _db.ConciliacionDianRenglones.FirstOrDefaultAsync(x => x.Id == renglonId, ct);
        if (r is null) { return; }
        r.OrdenCompraSoldarco = (ordenCompra ?? string.Empty).Trim();
        await _db.SaveChangesAsync(ct);
    }

    // Colores permitidos para el marcador de fila (whitelist; cualquier otro valor se trata como "sin color").
    private static readonly HashSet<string> _marcadores = new(StringComparer.OrdinalIgnoreCase)
    { "green", "yellow", "red", "blue", "gray", "purple", "orange" };

    public async Task SetMarkerColorAsync(Guid renglonId, string? color, CancellationToken ct = default)
    {
        var r = await _db.ConciliacionDianRenglones.FirstOrDefaultAsync(x => x.Id == renglonId, ct);
        if (r is null) { return; }
        var c = color?.Trim().ToLowerInvariant();
        r.MarkerColor = !string.IsNullOrEmpty(c) && _marcadores.Contains(c) ? c : null; // "" o invalido => limpia
        await _db.SaveChangesAsync(ct);
    }

    public async Task ToggleAprobadaAsync(Guid renglonId, bool aprobada, CancellationToken ct = default)
    {
        var r = await _db.ConciliacionDianRenglones.FirstOrDefaultAsync(x => x.Id == renglonId, ct);
        if (r is null) { return; }
        r.FacturaAprobada = aprobada;
        r.AprobadaManual = true; // decision del usuario: el cruce ya no la pisa.
        RecomputeEstado(r);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetSeleccionAsync(IReadOnlyList<Guid> renglonIds, bool seleccionado, CancellationToken ct = default)
    {
        if (renglonIds.Count == 0) { return; }
        var rows = await _db.ConciliacionDianRenglones.Where(r => renglonIds.Contains(r.Id)).ToListAsync(ct);
        foreach (var r in rows) { r.Seleccionado = seleccionado; }
        await _db.SaveChangesAsync(ct);
    }

    // Secuencia RADIAN que se radica: 030 (acuse) -> 032 (recibo del bien/servicio) -> 033 (aceptacion expresa).
    private static readonly (string Tipo, Func<ConciliacionDianRenglon, bool> YaHecho, Action<ConciliacionDianRenglon> Marcar)[] _secuenciaRadian =
    {
        ("ACUSE_DE_RECIBO",       r => r.Evento30, r => r.Evento30 = true),
        ("RECIBO_DE_PRESTACION",  r => r.Evento32, r => r.Evento32 = true),
        ("ACEPTACION_EXPRESA",    r => r.Evento33, r => r.Evento33 = true),
    };

    public async Task<ConciliacionRadicacionResult> RadicarEventosAsync(
        Guid documentoId, IReadOnlyList<Guid> renglonIds, bool simular, CancellationToken ct = default)
    {
        var q = _db.ConciliacionDianRenglones.Where(r => r.DocumentoId == documentoId);
        q = renglonIds.Count > 0 ? q.Where(r => renglonIds.Contains(r.Id)) : q.Where(r => r.Seleccionado);
        var rows = await q.ToListAsync(ct);

        // EventId de NEWTON por CUFE (poblado por la ingesta). Sin EventId no se puede radicar.
        var cufes = rows.Select(r => r.Cufe).Distinct().ToList();
        var eventIdPorCufe = await _db.ConciliacionDianNewtonDummies
            .Where(n => cufes.Contains(n.Cufe) && n.EventId != "")
            .Select(n => new { n.Cufe, n.EventId })
            .ToDictionaryAsync(x => x.Cufe, x => x.EventId, ct);

        int elegibles = 0, enviados = 0, fallidos = 0, sinPlataforma = 0, sinEventId = 0;
        var changed = false;

        if (!simular && _sender is null)
        {
            return new(0, 0, 0, 0, 0, false, "Envio real no disponible (INewtonEventSender no configurado).");
        }

        foreach (var r in rows)
        {
            if (!r.FacturaAprobada) { continue; }
            if (!r.PlataformaProveedor) { sinPlataforma++; continue; }
            var pendientes = _secuenciaRadian.Where(e => !e.YaHecho(r)).ToList();
            if (pendientes.Count == 0) { continue; }
            elegibles++;

            if (!eventIdPorCufe.TryGetValue(r.Cufe, out var eventId) || string.IsNullOrWhiteSpace(eventId))
            {
                sinEventId++;
                continue;
            }

            foreach (var e in pendientes)
            {
                if (simular) { enviados++; continue; } // DRY-RUN: cuenta lo que se enviaria; NO llama a NEWTON ni marca.

                var (ok, _) = await _sender!.SendEventAsync(eventId, e.Tipo, ct);
                if (ok) { e.Marcar(r); enviados++; changed = true; } else { fallidos++; }
            }
        }

        if (changed) { await _db.SaveChangesAsync(ct); }
        return new(elegibles, enviados, fallidos, sinPlataforma, sinEventId, simular, null);
    }

    // Estado derivado: Buzon/Procesado (ramas del molde) se respetan; el nucleo mueve Conciliar<->Conciliado
    // segun si el renglon esta cruzado limpio Y aprobado.
    private static void RecomputeEstado(ConciliacionDianRenglon r)
    {
        if (r.Estado is EstadoConciliacionDian.Buzon or EstadoConciliacionDian.Procesado) { return; }
        var cruzadoLimpio = !string.IsNullOrEmpty(r.NumFacturaSoldarco) && r.NumFacturaSoldarco != MarcaInconsistencia;
        r.Estado = (cruzadoLimpio && r.FacturaAprobada)
            ? EstadoConciliacionDian.Conciliado
            : EstadoConciliacionDian.Conciliar;
    }

    // ---- Seed dummy (Fase 1) ----

    public async Task<string> SeedDummyAsync(CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return "No hay tenant activo."; }
        if (await _db.ConciliacionDianDocumentos.AnyAsync(ct))
        {
            return "El modulo ya tiene datos para este tenant (seed idempotente: no se duplico nada).";
        }

        var hoy = DateTimeOffset.UtcNow;
        var anio = hoy.Year;
        var mes = hoy.Month;
        var baseFecha = new DateTimeOffset(anio, mes, 5, 0, 0, 0, TimeSpan.Zero);

        // 6 facturas del bot DIAN (dummy): 4 cruzaran limpio, 1 con inconsistencia (2 docs ERP), 1 sin cruce.
        var proveedores = new[]
        {
            ("Suministros Andinos SAS", "900123456", "FE-1001", "12345.00"),
            ("Ferreteria El Tornillo Ltda", "830987654", "FE-2002", "8900.00"),
            ("Distribuciones La 80", "901555222", "FAV-3050", "23400.00"),
            ("Aceros y Perfiles SA", "860333111", "SETP-4110", "56000.00"),
            ("Papeleria Central", "800111999", "FE-5500", "3200.00"),   // inconsistencia
            ("Transportes del Sur", "901777888", "FE-6600", "14100.00"), // sin cruce en ERP
        };
        var i = 0;
        foreach (var (nombre, nit, folio, subtotal) in proveedores)
        {
            var sub = decimal.Parse(subtotal, System.Globalization.CultureInfo.InvariantCulture);
            var iva = Math.Round(sub * 0.19m, 2);
            _db.ConciliacionDianBotDummies.Add(new ConciliacionDianBotDummy
            {
                TenantId = tenantId,
                Cufe = $"CUFE-{anio}{mes:00}-{i:000}",
                TipoDoc = "Factura electronica",
                FechaDoc = baseFecha.AddDays(i),
                NombreEmisor = nombre,
                NitEmisor = nit,
                PrefijoFolio = folio,
                IdCompra = $"OC-{7000 + i}",
                SubtotalBruto = sub,
                DescuentoComercial = 0m,
                Subtotal = sub,
                Iva = iva,
                TotalAntesRet = sub + iva,
                RetencionFuente = Math.Round(sub * 0.025m, 2),
                RetencionIca = Math.Round(sub * 0.007m, 2),
                Total = sub + iva - Math.Round(sub * 0.025m, 2) - Math.Round(sub * 0.007m, 2),
                TipoPago = i % 2 == 0 ? "Contado" : "Credito",
                ProveedorTecnologico = "Proveedor Tecnologico SAS",
            });
            i++;
        }

        // ERP (dummy): referencias que emparejan las 4 primeras (1 doc c/u) y la 5a con 2 docs (inconsistencia).
        void Erp(string reff, string doc) => _db.ConciliacionDianErpRefDummies.Add(
            new ConciliacionDianErpRefDummy { TenantId = tenantId, Referencia = reff, DocumentoInterno = doc });
        Erp("FE-1001", "COMP-0001");
        Erp("FE-2002", "COMP-0002");
        Erp("FAV-3050", "COMP-0003");
        Erp("SETP-4110", "COMP-0004");
        Erp("FE-5500", "COMP-0005");
        Erp("FE-5500", "COMP-0099"); // segundo doc para la misma factura -> Inconsistencia!
        // FE-6600 no existe en ERP -> sin cruce.

        // NEWTON (dummy): las 4 que cruzan limpio tienen representacion grafica (plataforma SI); las otras no.
        void Newton(int idx, bool conPdf) => _db.ConciliacionDianNewtonDummies.Add(new ConciliacionDianNewtonDummy
        {
            TenantId = tenantId,
            Cufe = $"CUFE-{anio}{mes:00}-{idx:000}",
            GuidPdf = conPdf ? Guid.NewGuid().ToString("N") : string.Empty,
            EventId = conPdf ? $"EVT-{idx:000}" : string.Empty,
        });
        for (var k = 0; k < 6; k++) { Newton(k, conPdf: k < 4); }

        await _db.SaveChangesAsync(ct);

        // Crea el CCD del mes, importa del bot y cruza (deja la logica ya ejercitada al abrir el modulo).
        var (doc, err) = await CrearDocumentoAsync(anio, mes, ct);
        if (err is not null || doc is null) { return $"Fuentes dummy sembradas, pero no se creo el CCD: {err}"; }
        var importadas = await ImportarDesdeBotAsync(doc.Id, ct);
        var cruce = await CruzarAsync(doc.Id, ct);

        // Fidelidad al molde: siembra 2 renglones en Buzon y 1 en Procesado (ramas heredadas del molde) para
        // que sus pestanas no salgan vacias.
        SeedRenglonEstado(tenantId, doc.Id, EstadoConciliacionDian.Buzon, "Comercial Buzon 1", "901010101", "BZ-01", baseFecha);
        SeedRenglonEstado(tenantId, doc.Id, EstadoConciliacionDian.Buzon, "Comercial Buzon 2", "901020202", "BZ-02", baseFecha.AddDays(1));
        SeedRenglonEstado(tenantId, doc.Id, EstadoConciliacionDian.Procesado, "Comercial Procesado 1", "901030303", "PR-01", baseFecha.AddDays(2));
        await _db.SaveChangesAsync(ct);

        return $"Seed dummy OK: CCD {doc.Consecutivo} ({mes:00}/{anio}), {importadas} importadas, "
             + $"{cruce.Cruzadas} cruzadas, {cruce.Inconsistencias} inconsistencia(s), {cruce.AutoAprobadas} auto-aprobadas.";
    }

    private void SeedRenglonEstado(Guid tenantId, Guid docId, EstadoConciliacionDian estado,
        string proveedor, string nit, string folio, DateTimeOffset fecha)
    {
        _db.ConciliacionDianRenglones.Add(new ConciliacionDianRenglon
        {
            TenantId = tenantId,
            DocumentoId = docId,
            Estado = estado,
            Cufe = $"CUFE-{estado}-{folio}",
            TipoDocDian = "Factura electronica",
            FechaEmision = fecha,
            NombreProveedor = proveedor,
            NitProveedor = nit,
            NumFacturaProveedor = folio,
            NumFacturaSoldarco = estado == EstadoConciliacionDian.Procesado ? "COMP-9000" : string.Empty,
            OrdenCompraSoldarco = string.Empty,
            SubtotalBruto = 10000m,
            SubtotalNeto = 10000m,
            IvaDescontable = 1900m,
            TotalAntesRetenciones = 11900m,
            TotalFactura = 11900m,
            TipoPago = "Contado",
            FacturaAprobada = estado == EstadoConciliacionDian.Procesado,
            PlataformaProveedor = estado == EstadoConciliacionDian.Procesado,
            Evento30 = estado == EstadoConciliacionDian.Procesado,
            Evento32 = estado == EstadoConciliacionDian.Procesado,
            Evento33 = estado == EstadoConciliacionDian.Procesado,
        });
    }

    // ---- Mapeo ----

    private static ConciliacionDianDocumentoDto MapDoc(ConciliacionDianDocumento d) =>
        new(d.Id, d.Consecutivo, d.Anio, d.Mes, d.Estado, d.CreatedAt);

    private static ConciliacionDianRenglonDto MapRenglon(ConciliacionDianRenglon r) =>
        new(r.Id, r.Estado, r.TipoDocDian, r.FechaEmision, r.NombreProveedor, r.NitProveedor,
            r.NumFacturaProveedor, r.NumFacturaSoldarco, r.OrdenCompraSoldarco, r.SubtotalBruto,
            r.DescuentoComercial, r.SubtotalNeto, r.IvaDescontable, r.TotalAntesRetenciones, r.RetRetefuente,
            r.RetIca, r.TotalFactura, r.TipoPago, r.FacturaAprobada, r.PlataformaProveedor, r.RutEscaneado,
            r.Evento30, r.Evento31, r.Evento32, r.Evento33, r.Evento34, r.Seleccionado, r.Cufe, r.MarkerColor);
}
