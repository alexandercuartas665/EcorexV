namespace Ecorex.Application.Formacion;

/// <summary>Configuracion de la evaluacion final de un curso (ADR-0202 ola 2).</summary>
public sealed record EvaluacionConfigDto(bool Activa, int PuntajeMinimo, int IntentosMax, int Preguntas);

/// <summary>Pregunta vista por el administrador (incluye la respuesta correcta).</summary>
public sealed record PreguntaAdminDto(Guid Id, int Orden, string Enunciado, IReadOnlyList<string> Opciones, int IndiceCorrecto, string? Retroalimentacion);

public sealed record GuardarPreguntaRequest(Guid CursoId, Guid? Id, string Enunciado, IReadOnlyList<string> Opciones, int IndiceCorrecto, string? Retroalimentacion);

/// <summary>Pregunta vista por el alumno (SIN la respuesta correcta).</summary>
public sealed record PreguntaAlumnoDto(Guid Id, string Enunciado, IReadOnlyList<string> Opciones);

/// <summary>Estado de la evaluacion para el alumno actual.</summary>
public sealed record EvaluacionAlumnoDto(
    int PuntajeMinimo, int IntentosMax, int IntentosUsados, int IntentosRestantes, bool Bloqueado, bool Aprobado, int? MejorNota,
    bool LeccionesCompletas, IReadOnlyList<PreguntaAlumnoDto> Preguntas)
{
    /// <summary>Puede rendir: vio todas las lecciones, no esta bloqueado y no ha aprobado.</summary>
    public bool PuedeRendir => LeccionesCompletas && !Bloqueado && !Aprobado && IntentosRestantes > 0;
}

/// <summary>Pregunta fallada con su explicacion (lo que ve el alumno tras rendir).</summary>
public sealed record PreguntaFalladaDto(string Enunciado, string? Retroalimentacion);

/// <summary>Resultado de un intento (calificado en el servidor).</summary>
public sealed record ResultadoEvaluacionDto(
    bool Ok, string? Error, int Puntaje = 0, int Correctas = 0, int Total = 0, bool Aprobado = false,
    int IntentosRestantes = 0, bool Bloqueado = false, IReadOnlyList<PreguntaFalladaDto>? Falladas = null)
{
    public static ResultadoEvaluacionDto Mal(string error) => new(false, error);
}

/// <summary>Estado de una persona en un curso para el reporte de avance.</summary>
public enum AvanceEstado
{
    NoIniciado = 0,
    EnCurso = 1,
    PendienteEvaluacion = 2,
    Bloqueado = 3,
    Terminado = 4
}

/// <summary>Fila del reporte de avance de un curso.</summary>
public sealed record AvanceFilaDto(
    Guid TenantUserId, string Nombre, string Email, bool Obligatorio, DateOnly? FechaLimite, AvanceEstado Estado, bool Vencido,
    int Porcentaje, DateTimeOffset? FechaInicio, DateTimeOffset? FechaCompletado, int Intentos, int? MejorNota,
    DateTimeOffset? FechaAprobacion, string? CertificadoCodigo);

/// <summary>Reporte de avance de un curso: filas + resumen.</summary>
public sealed record AvanceCursoDto(Guid CursoId, string Curso, bool TieneEvaluacion, IReadOnlyList<AvanceFilaDto> Filas)
{
    public int Asignados => Filas.Count(f => f.Obligatorio);
    public int Terminados => Filas.Count(f => f.Estado == AvanceEstado.Terminado);
    public int Vencidos => Filas.Count(f => f.Vencido);
    public int Bloqueados => Filas.Count(f => f.Estado == AvanceEstado.Bloqueado);
}

/// <summary>Datos impresos en el certificado (y en la verificacion publica).</summary>
public sealed record CertificadoDto(
    string Codigo, string Persona, string Curso, string Empresa, DateOnly Fecha, int? Nota, int? DuracionMinutos);
