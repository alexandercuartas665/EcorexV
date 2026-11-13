using System.Globalization;
using System.Text;
using Ecorex.Application.Voice;

namespace Ecorex.Application.Agendas;

/// <summary>Lo que resolvio el cliente en la llamada de recordatorio (ADR-0167 etapa 2).</summary>
public enum AgendaLlamadaResultado
{
    /// <summary>Asistira: el turno queda Confirmado.</summary>
    Confirma,

    /// <summary>No asistira y no quiere otro horario: el turno se cancela (libera el cupo).</summary>
    Cancela,

    /// <summary>Quiere otro horario: queda anotado en el turno para que la empresa (o el agente de WhatsApp) lo reprograme.</summary>
    Reprogramar,

    /// <summary>No contesto, buzon de voz o la llamada no se conecto.</summary>
    NoContesto,

    /// <summary>Hablo pero no quedo claro: queda anotado para revision.</summary>
    SinDefinir
}

/// <summary>
/// Partes PURAS del recordatorio por llamada (ADR-0167 etapa 2): el guion del agente de voz (constante por regla,
/// los datos del turno van como variables dinamicas para reutilizar el mismo agente de Retell), los campos que el
/// agente debe dejar en el analisis post-llamada, el telefono en E.164 y la interpretacion del resultado.
/// </summary>
public static class AgendaRecordatorioLlamada
{
    /// <summary>Campo del analisis post-llamada con el resultado (confirma | cancela | reprogramar | no_contesto | sin_definir).</summary>
    public const string CampoResultado = "resultado_turno";

    /// <summary>Campo del analisis con la nota libre: motivo de la cancelacion o el dia/hora que prefiere el cliente.</summary>
    public const string CampoNota = "nota_turno";

    /// <summary>Origen de la llamada en la metadata de Retell.</summary>
    public const string Origen = "ecorex-agenda-recordatorio";

    /// <summary>Campos que el agente de voz deja en call_analysis.custom_analysis_data.</summary>
    public static readonly IReadOnlyList<RetellAnalysisField> CamposAnalisis = new[]
    {
        new RetellAnalysisField(CampoResultado, "enum",
            "Resultado del recordatorio del turno: confirma (asistira), cancela (no asistira y no quiere otro horario), "
            + "reprogramar (quiere otro dia u hora), no_contesto (no contesto, buzon de voz o colgo sin hablar), "
            + "sin_definir (hablo pero no decidio).",
            new[] { "confirma", "cancela", "reprogramar", "no_contesto", "sin_definir" }),
        new RetellAnalysisField(CampoNota, "string",
            "Si cancela: el motivo. Si quiere reprogramar: el dia y la hora que prefiere. Vacio en otro caso.")
    };

    /// <summary>
    /// Guion del agente de voz. Usa variables {{...}} (cliente, servicio, fecha, hora, recurso, empresa) que se llenan
    /// por llamada (RetellVoiceService no manda variables vacias, por eso todas llevan un valor); asi el prompt no
    /// cambia entre turnos y Retell reutiliza el mismo agente.
    /// </summary>
    public static string Guion(string? instruccionEmpresa)
    {
        var sb = new StringBuilder();
        sb.Append("Eres el asistente telefonico de {{empresa}}. Llamas a {{cliente}} para recordarle su turno de {{servicio}} ")
          .Append("el {{fecha}} a las {{hora}}. Con quien o donde: {{recurso}} (si dice \"no indicado\", no lo menciones).\n")
          .Append("Pasos:\n")
          .Append("1. Saluda con cordialidad y confirma que hablas con {{cliente}}. Si no es la persona, pide que le den el mensaje y despidete.\n")
          .Append("2. Recuerdale el turno con su fecha, hora y servicio.\n")
          .Append("3. Pregunta si asistira.\n")
          .Append("   - Si confirma: agradece y recuerdale llegar unos minutos antes.\n")
          .Append("   - Si no puede asistir: pregunta si prefiere cancelar o cambiarlo para otro dia.\n")
          .Append("   - Si quiere cambiarlo: pregunta que dia y hora le sirven y dile que le confirmaran el nuevo horario por WhatsApp. ")
          .Append("No prometas un horario concreto.\n")
          .Append("   - Si cancela: pregunta el motivo con amabilidad y confirma que el turno queda cancelado.\n")
          .Append("4. Despidete.\n")
          .Append("Se breve (menos de dos minutos), habla en espanol y no inventes datos que no esten en este mensaje.");
        if (!string.IsNullOrWhiteSpace(instruccionEmpresa))
        {
            sb.Append("\n\nInstruccion de la empresa:\n").Append(instruccionEmpresa.Trim());
        }
        return sb.ToString();
    }

    /// <summary>Variables dinamicas de la llamada a partir de los valores de los tokens del turno.</summary>
    public static Dictionary<string, string> VariablesDinamicas(IReadOnlyDictionary<string, string> valores)
    {
        string V(string token) => valores.TryGetValue(token, out var v) ? v : string.Empty;
        var recurso = string.IsNullOrWhiteSpace(V("{turno.recurso}")) ? "no indicado" : V("{turno.recurso}");
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cliente"] = V("{turno.cliente}"),
            ["servicio"] = V("{turno.servicio}"),
            ["fecha"] = V("{turno.fecha}"),
            ["hora"] = V("{turno.hora}"),
            ["hora_fin"] = V("{turno.hora_fin}"),
            ["recurso"] = recurso,
            ["ticket"] = V("{turno.ticket}"),
            ["empresa"] = string.IsNullOrWhiteSpace(V("{empresa.nombre}")) ? "la empresa" : V("{empresa.nombre}")
        };
    }

    /// <summary>
    /// Telefono en E.164. Con '+' se respeta; 10 digitos = numero nacional (se antepone el indicativo del pais);
    /// 11 a 15 digitos = ya trae indicativo. Null si no se puede.
    /// </summary>
    public static string? TelefonoE164(string? telefono, string indicativoPais = "57")
    {
        if (string.IsNullOrWhiteSpace(telefono))
        {
            return null;
        }
        var digitos = new string(telefono.Where(char.IsAsciiDigit).ToArray());
        if (telefono.TrimStart().StartsWith('+'))
        {
            return digitos.Length is >= 8 and <= 15 && digitos[0] != '0' ? "+" + digitos : null;
        }
        digitos = digitos.TrimStart('0');
        return digitos.Length switch
        {
            10 => "+" + indicativoPais + digitos,
            >= 11 and <= 15 => "+" + digitos,
            _ => null
        };
    }

    /// <summary>
    /// Interpreta el resultado de la llamada. <paramref name="estado"/> es el VoiceCallStatus (texto). Si la llamada
    /// no se conecto es NoContesto; si se analizo, manda el campo resultado_turno (tolerante a mayusculas, tildes y
    /// sinonimos); sin ese campo, SinDefinir.
    /// </summary>
    public static (AgendaLlamadaResultado Resultado, string? Nota) Interpretar(string estado, IReadOnlyDictionary<string, string>? datos)
    {
        string? nota = null;
        if (datos is not null && datos.TryGetValue(CampoNota, out var n) && !string.IsNullOrWhiteSpace(n))
        {
            nota = n.Trim().Length > 300 ? n.Trim()[..300] : n.Trim();
        }
        if (estado is "Failed" or "Error")
        {
            return (AgendaLlamadaResultado.NoContesto, nota);
        }
        if (datos is null || !datos.TryGetValue(CampoResultado, out var r) || string.IsNullOrWhiteSpace(r))
        {
            return (AgendaLlamadaResultado.SinDefinir, nota);
        }
        var v = SinTildes(r).ToLowerInvariant().Replace(' ', '_');
        var resultado = v switch
        {
            _ when v.StartsWith("confirm") || v is "si" || v.StartsWith("asist") => AgendaLlamadaResultado.Confirma,
            _ when v.StartsWith("cancel") => AgendaLlamadaResultado.Cancela,
            _ when v.StartsWith("reprogram") || v.StartsWith("cambi") || v.StartsWith("reagend") => AgendaLlamadaResultado.Reprogramar,
            _ when v.StartsWith("no_contest") || v.Contains("buzon") || v.StartsWith("no_respond") => AgendaLlamadaResultado.NoContesto,
            _ => AgendaLlamadaResultado.SinDefinir
        };
        return (resultado, nota);
    }

    /// <summary>Texto corto del resultado para el historial y la bitacora de envios.</summary>
    public static string Describir(AgendaLlamadaResultado resultado, string? nota)
    {
        var baseTxt = resultado switch
        {
            AgendaLlamadaResultado.Confirma => "el cliente confirmo que asiste",
            AgendaLlamadaResultado.Cancela => "el cliente cancelo el turno",
            AgendaLlamadaResultado.Reprogramar => "el cliente pidio cambiar el turno",
            AgendaLlamadaResultado.NoContesto => "el cliente no contesto",
            _ => "la llamada no dejo una respuesta clara"
        };
        return string.IsNullOrWhiteSpace(nota) ? baseTxt : $"{baseTxt} ({nota})";
    }

    private static string SinTildes(string s)
    {
        var norm = s.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var c in norm)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
