using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Ecorex.Application.Organization;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Formacion;

/// <summary>
/// FORMACION ola 2 (ADR-0202): evaluacion final de opcion multiple (puntaje minimo, intentos, bloqueo y desbloqueo,
/// calificada en el servidor; la respuesta correcta nunca viaja al alumno), certificado con codigo verificable al
/// terminar el curso (lecciones completas + evaluacion aprobada si la tiene) y reporte de avance por curso con
/// exportacion a Excel. Tenant-scoped por el filtro global; la verificacion publica del certificado es la unica
/// lectura sin tenant (por codigo no adivinable, datos minimos).
/// </summary>
public interface IFormacionEvaluacionService
{
    Task<EvaluacionConfigDto?> ConfigEvaluacionAsync(Guid cursoId, CancellationToken ct = default);
    Task<FormacionResult> ConfigurarEvaluacionAsync(Guid cursoId, bool activa, int puntajeMinimo, int intentosMax, CancellationToken ct = default);
    Task<IReadOnlyList<PreguntaAdminDto>> ListarPreguntasAsync(Guid cursoId, CancellationToken ct = default);
    Task<FormacionResult> GuardarPreguntaAsync(GuardarPreguntaRequest req, CancellationToken ct = default);
    Task<FormacionResult> EliminarPreguntaAsync(Guid preguntaId, CancellationToken ct = default);
    Task<FormacionResult> MoverPreguntaAsync(Guid preguntaId, int delta, CancellationToken ct = default);

    Task<EvaluacionAlumnoDto?> EvaluacionAsync(Guid cursoId, bool puedeEditar, CancellationToken ct = default);
    Task<ResultadoEvaluacionDto> ResponderAsync(Guid cursoId, IReadOnlyDictionary<Guid, int> respuestas, bool puedeEditar, CancellationToken ct = default);

    Task<AvanceCursoDto?> AvanceAsync(Guid cursoId, CancellationToken ct = default);
    Task<FormacionResult> DesbloquearAsync(Guid cursoId, Guid tenantUserId, CancellationToken ct = default);
    Task<byte[]?> ExportarAvanceAsync(Guid cursoId, CancellationToken ct = default);

    /// <summary>Certificado del usuario actual (emite el codigo la primera vez). Null si aun no termino el curso.</summary>
    Task<CertificadoDto?> CertificadoAsync(Guid cursoId, CancellationToken ct = default);
    /// <summary>Verificacion PUBLICA por codigo (sin tenant en contexto). Null si no existe.</summary>
    Task<CertificadoDto?> VerificarCertificadoAsync(string codigo, CancellationToken ct = default);
}

public sealed partial class FormacionService : IFormacionEvaluacionService
{
    public const int MaxPreguntas = 200;
    public const int MaxOpciones = 8;
    private const string CodigoAlfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    // ============================================ Evaluacion (admin) ============================================

    public async Task<EvaluacionConfigDto?> ConfigEvaluacionAsync(Guid cursoId, CancellationToken ct = default)
    {
        var c = await _db.Cursos.AsNoTracking().Where(x => x.Id == cursoId)
            .Select(x => new { x.EvaluacionActiva, x.PuntajeMinimo, x.IntentosMax }).FirstOrDefaultAsync(ct);
        if (c is null) { return null; }
        var n = await _db.CursoPreguntas.CountAsync(p => p.CursoId == cursoId, ct);
        return new EvaluacionConfigDto(c.EvaluacionActiva, c.PuntajeMinimo, c.IntentosMax, n);
    }

    public async Task<FormacionResult> ConfigurarEvaluacionAsync(Guid cursoId, bool activa, int puntajeMinimo, int intentosMax, CancellationToken ct = default)
    {
        var curso = await _db.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId, ct);
        if (curso is null) { return FormacionResult.Mal("Curso no encontrado."); }
        if (puntajeMinimo is < 0 or > 100) { return FormacionResult.Mal("El puntaje minimo va de 0 a 100."); }
        if (intentosMax is < 1 or > 50) { return FormacionResult.Mal("Los intentos van de 1 a 50."); }
        curso.EvaluacionActiva = activa;
        curso.PuntajeMinimo = puntajeMinimo;
        curso.IntentosMax = intentosMax;
        await _db.SaveChangesAsync(ct);
        return FormacionResult.Bien(curso.Id);
    }

    public async Task<IReadOnlyList<PreguntaAdminDto>> ListarPreguntasAsync(Guid cursoId, CancellationToken ct = default)
    {
        var preguntas = await _db.CursoPreguntas.AsNoTracking().Where(p => p.CursoId == cursoId)
            .OrderBy(p => p.Orden).ThenBy(p => p.CreatedAt).ToListAsync(ct);
        return preguntas.Select(p => new PreguntaAdminDto(p.Id, p.Orden, p.Enunciado, Opciones(p.OpcionesJson), p.IndiceCorrecto, p.Retroalimentacion)).ToList();
    }

    public async Task<FormacionResult> GuardarPreguntaAsync(GuardarPreguntaRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return FormacionResult.Mal("No hay tenant activo."); }
        var enunciado = Clip(req.Enunciado, 2000);
        if (enunciado is null) { return FormacionResult.Mal("Escribe el enunciado de la pregunta."); }
        var opciones = (req.Opciones ?? Array.Empty<string>()).Select(o => Clip(o, 500)).Where(o => o is not null).Select(o => o!).ToList();
        if (opciones.Count < 2) { return FormacionResult.Mal("La pregunta necesita al menos 2 opciones."); }
        if (opciones.Count > MaxOpciones) { return FormacionResult.Mal($"Maximo {MaxOpciones} opciones."); }
        if (req.IndiceCorrecto < 0 || req.IndiceCorrecto >= opciones.Count) { return FormacionResult.Mal("Marca cual opcion es la correcta."); }
        if (!await _db.Cursos.AnyAsync(c => c.Id == req.CursoId, ct)) { return FormacionResult.Mal("Curso no encontrado."); }

        CursoPregunta? p;
        if (req.Id is Guid id)
        {
            p = await _db.CursoPreguntas.FirstOrDefaultAsync(x => x.Id == id && x.CursoId == req.CursoId, ct);
            if (p is null) { return FormacionResult.Mal("Pregunta no encontrada."); }
        }
        else
        {
            var n = await _db.CursoPreguntas.CountAsync(x => x.CursoId == req.CursoId, ct);
            if (n >= MaxPreguntas) { return FormacionResult.Mal($"Maximo {MaxPreguntas} preguntas por curso."); }
            var orden = await _db.CursoPreguntas.Where(x => x.CursoId == req.CursoId).Select(x => (int?)x.Orden).MaxAsync(ct) ?? 0;
            p = new CursoPregunta { TenantId = tenantId, CursoId = req.CursoId, Orden = orden + 1 };
            _db.CursoPreguntas.Add(p);
        }
        p.Enunciado = enunciado;
        p.OpcionesJson = JsonSerializer.Serialize(opciones);
        p.IndiceCorrecto = req.IndiceCorrecto;
        p.Retroalimentacion = Clip(req.Retroalimentacion, 2000);
        await _db.SaveChangesAsync(ct);
        return FormacionResult.Bien(p.Id);
    }

    public async Task<FormacionResult> EliminarPreguntaAsync(Guid preguntaId, CancellationToken ct = default)
    {
        var p = await _db.CursoPreguntas.FirstOrDefaultAsync(x => x.Id == preguntaId, ct);
        if (p is null) { return FormacionResult.Mal("Pregunta no encontrada."); }
        _db.CursoPreguntas.Remove(p);
        await _db.SaveChangesAsync(ct);
        return FormacionResult.Bien(preguntaId);
    }

    public async Task<FormacionResult> MoverPreguntaAsync(Guid preguntaId, int delta, CancellationToken ct = default)
    {
        var p = await _db.CursoPreguntas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == preguntaId, ct);
        if (p is null) { return FormacionResult.Mal("Pregunta no encontrada."); }
        var hermanas = await _db.CursoPreguntas.Where(x => x.CursoId == p.CursoId).OrderBy(x => x.Orden).ThenBy(x => x.CreatedAt).ToListAsync(ct);
        Reordenar(hermanas, h => h.Id == preguntaId, delta, (h, o) => h.Orden = o);
        await _db.SaveChangesAsync(ct);
        return FormacionResult.Bien(preguntaId);
    }

    // =========================================== Evaluacion (alumno) ===========================================

    public async Task<EvaluacionAlumnoDto?> EvaluacionAsync(Guid cursoId, bool puedeEditar, CancellationToken ct = default)
    {
        var curso = await _db.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cursoId && (c.Publicado || puedeEditar), ct);
        if (curso is null || !curso.EvaluacionActiva) { return null; }
        var preguntas = await _db.CursoPreguntas.AsNoTracking().Where(p => p.CursoId == cursoId)
            .OrderBy(p => p.Orden).ThenBy(p => p.CreatedAt).ToListAsync(ct);
        if (preguntas.Count == 0) { return null; }
        var me = await TenantUserIdAsync(ct);
        var progreso = me is Guid u
            ? await _db.CursoProgresos.AsNoTracking().FirstOrDefaultAsync(p => p.CursoId == cursoId && p.TenantUserId == u, ct)
            : null;
        var usados = progreso is null ? 0 : progreso.Intentos - progreso.IntentosPerdonados;
        return new EvaluacionAlumnoDto(curso.PuntajeMinimo, curso.IntentosMax, usados, Math.Max(0, curso.IntentosMax - usados),
            progreso?.Bloqueado ?? false, progreso?.Aprobado ?? false, progreso?.MejorNota, progreso?.FechaCompletado is not null,
            preguntas.Select(p => new PreguntaAlumnoDto(p.Id, p.Enunciado, Opciones(p.OpcionesJson))).ToList());
    }

    public async Task<ResultadoEvaluacionDto> ResponderAsync(Guid cursoId, IReadOnlyDictionary<Guid, int> respuestas, bool puedeEditar, CancellationToken ct = default)
    {
        var me = await TenantUserIdAsync(ct);
        if (me is not Guid u || _tenant.TenantId is not Guid tenantId) { return ResultadoEvaluacionDto.Mal("No se pudo identificar al usuario."); }
        var curso = await _db.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cursoId && (c.Publicado || puedeEditar), ct);
        if (curso is null || !curso.EvaluacionActiva) { return ResultadoEvaluacionDto.Mal("Este curso no tiene evaluacion activa."); }
        var preguntas = await _db.CursoPreguntas.AsNoTracking().Where(p => p.CursoId == cursoId).OrderBy(p => p.Orden).ToListAsync(ct);
        if (preguntas.Count == 0) { return ResultadoEvaluacionDto.Mal("La evaluacion aun no tiene preguntas."); }

        await using var tx = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(ct);
        var progreso = await _db.CursoProgresos.FirstOrDefaultAsync(p => p.CursoId == cursoId && p.TenantUserId == u, ct);
        if (progreso?.FechaCompletado is null) { return ResultadoEvaluacionDto.Mal("Primero termina todas las lecciones del curso."); }
        if (progreso.Aprobado) { return ResultadoEvaluacionDto.Mal("Ya aprobaste esta evaluacion."); }
        if (progreso.Bloqueado || progreso.Intentos - progreso.IntentosPerdonados >= curso.IntentosMax)
        {
            return ResultadoEvaluacionDto.Mal("Agotaste los intentos. Pide al administrador del curso que te desbloquee.");
        }
        if (preguntas.Any(p => !respuestas.TryGetValue(p.Id, out var r) || r < 0 || r >= Opciones(p.OpcionesJson).Count))
        {
            return ResultadoEvaluacionDto.Mal("Responde todas las preguntas antes de enviar.");
        }

        // Calificacion en el servidor: la respuesta correcta nunca sale de aqui.
        var falladas = preguntas.Where(p => respuestas[p.Id] != p.IndiceCorrecto).ToList();
        var correctas = preguntas.Count - falladas.Count;
        var puntaje = (int)Math.Round(correctas * 100.0 / preguntas.Count, MidpointRounding.AwayFromZero);
        var aprobado = puntaje >= curso.PuntajeMinimo;
        var ahora = _clock.GetUtcNow();

        _db.CursoIntentos.Add(new CursoIntento
        {
            TenantId = tenantId,
            CursoId = cursoId,
            TenantUserId = u,
            Puntaje = puntaje,
            Correctas = correctas,
            Total = preguntas.Count,
            Aprobado = aprobado,
            Fecha = ahora,
            RespuestasJson = JsonSerializer.Serialize(preguntas.ToDictionary(p => p.Id.ToString("N"), p => respuestas[p.Id]))
        });
        progreso.Intentos++;
        progreso.MejorNota = Math.Max(progreso.MejorNota ?? 0, puntaje);
        if (aprobado)
        {
            progreso.Aprobado = true;
            progreso.FechaAprobacion = ahora;
            progreso.CertificadoCodigo ??= await NuevoCodigoAsync(ct);
        }
        progreso.Bloqueado = !aprobado && progreso.Intentos - progreso.IntentosPerdonados >= curso.IntentosMax;
        await _db.SaveChangesAsync(ct);
        if (tx is not null) { await tx.CommitAsync(ct); }

        return new ResultadoEvaluacionDto(true, null, puntaje, correctas, preguntas.Count, aprobado,
            Math.Max(0, curso.IntentosMax - (progreso.Intentos - progreso.IntentosPerdonados)), progreso.Bloqueado,
            falladas.Select(p => new PreguntaFalladaDto(p.Enunciado, p.Retroalimentacion)).ToList());
    }

    // ================================================= Avance =================================================

    public async Task<AvanceCursoDto?> AvanceAsync(Guid cursoId, CancellationToken ct = default)
    {
        var curso = await _db.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cursoId, ct);
        if (curso is null) { return null; }
        var requiereEval = curso.EvaluacionActiva && await _db.CursoPreguntas.AnyAsync(p => p.CursoId == cursoId, ct);

        var usuarios = await _db.TenantUsers.AsNoTracking()
            .Select(x => new { x.Id, x.Email, x.Status, Nombre = x.PlatformUser != null ? x.PlatformUser.DisplayName : null })
            .ToListAsync(ct);
        var porId = usuarios.ToDictionary(x => x.Id);
        var activos = usuarios.Where(x => x.Status == Domain.Enums.PlatformUserStatus.Active).Select(x => x.Id).ToList();

        // Obligatorios: quienes cubren las asignaciones, con la fecha limite mas cercana.
        var limites = new Dictionary<Guid, DateOnly?>();
        var asignaciones = await _db.CursoAsignaciones.AsNoTracking().Where(a => a.CursoId == cursoId).ToListAsync(ct);
        OrganigramaCargado? graph = null;
        foreach (var a in asignaciones)
        {
            IEnumerable<Guid> personas = a.Destino switch
            {
                CursoAsignacionDestino.Todos => activos,
                CursoAsignacionDestino.Usuario => a.TenantUserId is Guid tu ? new[] { tu } : Array.Empty<Guid>(),
                _ => a.OrgUnitId is Guid o
                    ? OrgAssigneeTree.ResolveForUnit(o, (graph ??= await CargarOrganigramaAsync(ct)).Units, graph.Members)
                    : Array.Empty<Guid>()
            };
            foreach (var pid in personas)
            {
                if (!limites.TryGetValue(pid, out var actual)) { limites[pid] = a.FechaLimite; }
                else if (a.FechaLimite is DateOnly f && (actual is null || f < actual)) { limites[pid] = f; }
            }
        }

        var progresos = await _db.CursoProgresos.AsNoTracking().Where(p => p.CursoId == cursoId).ToDictionaryAsync(p => p.TenantUserId, ct);
        var hoy = await HoyAsync(ct);
        var ids = limites.Keys.Union(progresos.Keys).Where(porId.ContainsKey).ToList();
        var filas = ids.Select(id =>
        {
            var usr = porId[id];
            progresos.TryGetValue(id, out var p);
            var obligatorio = limites.TryGetValue(id, out var limite);
            var terminado = p is not null && Terminado(p, requiereEval);
            var estado = p is null ? AvanceEstado.NoIniciado
                : terminado ? AvanceEstado.Terminado
                : p.Bloqueado ? AvanceEstado.Bloqueado
                : p.FechaCompletado is not null && requiereEval ? AvanceEstado.PendienteEvaluacion
                : AvanceEstado.EnCurso;
            return new AvanceFilaDto(id, string.IsNullOrWhiteSpace(usr.Nombre) ? usr.Email : usr.Nombre!, usr.Email, obligatorio, limite, estado,
                obligatorio && !terminado && limite is DateOnly l && l < hoy,
                p?.Porcentaje ?? 0, p?.FechaInicio, p?.FechaCompletado, p?.Intentos ?? 0, p?.MejorNota, p?.FechaAprobacion,
                terminado ? p!.CertificadoCodigo : null);
        })
        .OrderBy(f => f.Estado == AvanceEstado.Terminado).ThenByDescending(f => f.Vencido).ThenBy(f => f.Nombre, StringComparer.OrdinalIgnoreCase)
        .ToList();
        return new AvanceCursoDto(cursoId, curso.Titulo, requiereEval, filas);
    }

    public async Task<FormacionResult> DesbloquearAsync(Guid cursoId, Guid tenantUserId, CancellationToken ct = default)
    {
        var p = await _db.CursoProgresos.FirstOrDefaultAsync(x => x.CursoId == cursoId && x.TenantUserId == tenantUserId, ct);
        if (p is null) { return FormacionResult.Mal("Esa persona no ha iniciado el curso."); }
        if (!p.Bloqueado) { return FormacionResult.Mal("Esa persona no esta bloqueada."); }
        // Nueva tanda de intentos sin borrar la historia (los intentos totales se conservan para el reporte).
        p.IntentosPerdonados = p.Intentos;
        p.Bloqueado = false;
        p.DesbloqueadoPorTenantUserId = await TenantUserIdAsync(ct);
        p.FechaDesbloqueo = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return FormacionResult.Bien(p.Id);
    }

    public async Task<byte[]?> ExportarAvanceAsync(Guid cursoId, CancellationToken ct = default)
    {
        var avance = await AvanceAsync(cursoId, ct);
        if (avance is null) { return null; }
        var tz = await ZonaAsync(ct);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Avance");
        string[] headers = ["Persona", "Correo", "Obligatorio", "Fecha limite", "Estado", "Vencido", "Avance %", "Inicio", "Lecciones completas",
            "Intentos", "Mejor nota", "Aprobacion", "Certificado"];
        for (var i = 0; i < headers.Length; i++) { ws.Cell(1, i + 1).Value = headers[i]; }
        ws.Row(1).Style.Font.Bold = true;
        var row = 2;
        foreach (var f in avance.Filas)
        {
            ws.Cell(row, 1).Value = f.Nombre;
            ws.Cell(row, 2).Value = f.Email;
            ws.Cell(row, 3).Value = f.Obligatorio ? "Si" : "No";
            ws.Cell(row, 4).Value = f.FechaLimite?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 5).Value = EstadoTexto(f.Estado);
            ws.Cell(row, 6).Value = f.Vencido ? "Si" : "No";
            ws.Cell(row, 7).Value = f.Porcentaje;
            ws.Cell(row, 8).Value = Local(f.FechaInicio, tz);
            ws.Cell(row, 9).Value = Local(f.FechaCompletado, tz);
            ws.Cell(row, 10).Value = f.Intentos;
            ws.Cell(row, 11).Value = f.MejorNota?.ToString() ?? "";
            ws.Cell(row, 12).Value = Local(f.FechaAprobacion, tz);
            ws.Cell(row, 13).Value = f.CertificadoCodigo ?? "";
            row++;
        }
        ws.Columns().AdjustToContents(1, Math.Min(row, 200));
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static string EstadoTexto(AvanceEstado e) => e switch
    {
        AvanceEstado.NoIniciado => "No iniciado",
        AvanceEstado.EnCurso => "En curso",
        AvanceEstado.PendienteEvaluacion => "Falta la evaluacion",
        AvanceEstado.Bloqueado => "Bloqueado",
        _ => "Terminado"
    };

    // ============================================== Certificado ==============================================

    public async Task<CertificadoDto?> CertificadoAsync(Guid cursoId, CancellationToken ct = default)
    {
        var me = await TenantUserIdAsync(ct);
        if (me is not Guid u) { return null; }
        var curso = await _db.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cursoId, ct);
        if (curso is null) { return null; }
        var requiereEval = curso.EvaluacionActiva && await _db.CursoPreguntas.AnyAsync(p => p.CursoId == cursoId, ct);
        var p = await _db.CursoProgresos.FirstOrDefaultAsync(x => x.CursoId == cursoId && x.TenantUserId == u, ct);
        if (p is null || !Terminado(p, requiereEval)) { return null; }
        if (p.CertificadoCodigo is null)
        {
            p.CertificadoCodigo = await NuevoCodigoAsync(ct);
            await _db.SaveChangesAsync(ct);
        }
        return await ArmarCertificadoAsync(p, curso, requiereEval, ignoreFilters: false, ct);
    }

    public async Task<CertificadoDto?> VerificarCertificadoAsync(string codigo, CancellationToken ct = default)
    {
        var c = (codigo ?? string.Empty).Trim().ToUpperInvariant();
        if (c.Length is < 8 or > 20 || !c.All(ch => CodigoAlfabeto.Contains(ch))) { return null; }
        var p = await _db.CursoProgresos.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.CertificadoCodigo == c, ct);
        if (p is null) { return null; }
        var curso = await _db.Cursos.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == p.CursoId && x.TenantId == p.TenantId, ct);
        if (curso is null) { return null; }
        var requiereEval = curso.EvaluacionActiva
            && await _db.CursoPreguntas.IgnoreQueryFilters().AnyAsync(q => q.CursoId == curso.Id && q.TenantId == p.TenantId, ct);
        return await ArmarCertificadoAsync(p, curso, requiereEval, ignoreFilters: true, ct);
    }

    private async Task<CertificadoDto?> ArmarCertificadoAsync(CursoProgreso p, Curso curso, bool requiereEval, bool ignoreFilters, CancellationToken ct)
    {
        var users = ignoreFilters ? _db.TenantUsers.IgnoreQueryFilters() : _db.TenantUsers;
        var persona = await users.AsNoTracking().Where(x => x.Id == p.TenantUserId && x.TenantId == p.TenantId)
            .Select(x => new { x.Email, Nombre = x.PlatformUser != null ? x.PlatformUser.DisplayName : null }).FirstOrDefaultAsync(ct);
        var tenant = await _db.Tenants.AsNoTracking().Where(t => t.Id == p.TenantId).Select(t => new { t.Name, t.TimeZoneId }).FirstOrDefaultAsync(ct);
        if (persona is null || tenant is null || p.CertificadoCodigo is null) { return null; }
        var cuando = requiereEval ? p.FechaAprobacion ?? p.FechaCompletado : p.FechaCompletado ?? p.FechaAprobacion;
        var tz = Zona(tenant.TimeZoneId);
        var fecha = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(cuando ?? _clock.GetUtcNow(), tz).DateTime);
        return new CertificadoDto(p.CertificadoCodigo, string.IsNullOrWhiteSpace(persona.Nombre) ? persona.Email : persona.Nombre!, curso.Titulo,
            tenant.Name, fecha, requiereEval ? p.MejorNota : null, curso.DuracionMinutos);
    }

    // ================================================ Helpers ================================================

    /// <summary>Curso terminado: todas las lecciones vistas y, si exige evaluacion, aprobada.</summary>
    internal static bool Terminado(CursoProgreso p, bool requiereEvaluacion)
        => p.FechaCompletado is not null && (!requiereEvaluacion || p.Aprobado);

    private async Task<string> NuevoCodigoAsync(CancellationToken ct)
    {
        for (var i = 0; i < 5; i++)
        {
            var codigo = string.Create(12, 0, (span, _) =>
            {
                for (var k = 0; k < span.Length; k++) { span[k] = CodigoAlfabeto[RandomNumberGenerator.GetInt32(CodigoAlfabeto.Length)]; }
            });
            if (!await _db.CursoProgresos.IgnoreQueryFilters().AnyAsync(p => p.CertificadoCodigo == codigo, ct)) { return codigo; }
        }
        throw new InvalidOperationException("No se pudo generar un codigo de certificado unico.");
    }

    private static List<string> Opciones(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }

    private async Task<TimeZoneInfo> ZonaAsync(CancellationToken ct)
    {
        var tzId = _tenant.TenantId is Guid t
            ? await _db.Tenants.AsNoTracking().Where(x => x.Id == t).Select(x => x.TimeZoneId).FirstOrDefaultAsync(ct)
            : null;
        return Zona(tzId);
    }

    private static TimeZoneInfo Zona(string? tzId)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(tzId) ? "America/Bogota" : tzId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("COT", TimeSpan.FromHours(-5), "COT", "COT");
        }
    }

    private static string Local(DateTimeOffset? at, TimeZoneInfo tz)
        => at is DateTimeOffset a ? TimeZoneInfo.ConvertTime(a, tz).ToString("yyyy-MM-dd HH:mm") : "";
}
