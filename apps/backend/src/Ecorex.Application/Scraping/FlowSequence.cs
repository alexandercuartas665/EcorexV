using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ecorex.Application.Scraping;

/// <summary>
/// Secuencia de tramos de una programacion de flujo (ADR-0171). En vez de UNA corrida con los valores
/// guardados, la programacion corre el flujo VARIAS veces seguidas, cada una con un desplazamiento distinto:
/// tramo k = { <see cref="VarDesde"/> = Desde + k*Paso, <see cref="VarLargo"/> = Paso } hasta cubrir
/// [Desde, Hasta). Sirve a cualquier flujo que reciba "desde cuantos dias atras" y "cuantos dias" (p.ej. el
/// DRON DIAN: DIAS_DESDE / DIAS_ATRAS), para que cada corrida quepa en la vida de la sesion del sitio.
/// Los valores van como overrides de la corrida (capa de sesion): NUNCA se persisten en las variables.
/// </summary>
public sealed record FlowSequenceSpec
{
    /// <summary>Variable del flujo que recibe el desplazamiento del tramo (p.ej. DIAS_DESDE).</summary>
    public string VarDesde { get; init; } = "DIAS_DESDE";

    /// <summary>Variable del flujo que recibe el largo del tramo (p.ej. DIAS_ATRAS).</summary>
    public string VarLargo { get; init; } = "DIAS_ATRAS";

    /// <summary>Primer desplazamiento (0 = hoy).</summary>
    public int Desde { get; init; }

    /// <summary>Limite (exclusivo) del barrido: el ultimo tramo termina antes de este desplazamiento.</summary>
    public int Hasta { get; init; } = 6;

    /// <summary>Largo de cada tramo.</summary>
    public int Paso { get; init; } = 3;

    /// <summary>Pausa entre tramos, en minutos (deja respirar al sitio y a su correo de acceso).</summary>
    public int PausaMin { get; init; } = 5;

    /// <summary>Espera antes de reintentar un tramo que fallo, en minutos.</summary>
    public int ReintentoMin { get; init; } = 20;

    /// <summary>Cuantas veces se reintenta un tramo que fallo (0 = ninguna). Si se agotan, se anota y se
    /// sigue con el siguiente tramo.</summary>
    public int Reintentos { get; init; } = 1;

    /// <summary>Detener la secuencia si lleva mas de estas horas (0 = sin limite). Se revisa ENTRE tramos: un
    /// tramo que ya arranco termina.</summary>
    public int MaxHoras { get; init; }

    /// <summary>Techo de tramos por secuencia (defensa contra un rango absurdo).</summary>
    public const int MaxTramos = 120;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Lee la secuencia guardada; null si no hay o si el JSON no se entiende (la programacion
    /// corre entonces como antes: una sola corrida).</summary>
    public static FlowSequenceSpec? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try { return JsonSerializer.Deserialize<FlowSequenceSpec>(json, Json); }
        catch (JsonException) { return null; }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Motivo por el que la secuencia no es valida, o null si lo es.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(VarDesde) || string.IsNullOrWhiteSpace(VarLargo))
        {
            return "Indica las dos variables del tramo (desplazamiento y largo).";
        }
        if (string.Equals(VarDesde.Trim(), VarLargo.Trim(), StringComparison.Ordinal))
        {
            return "La variable de desplazamiento y la de largo deben ser distintas.";
        }
        if (Desde < 0) { return "\"Desde\" no puede ser negativo."; }
        if (Paso < 1) { return "El largo de cada tramo debe ser al menos 1."; }
        if (Hasta <= Desde) { return "\"Hasta\" debe ser mayor que \"Desde\"."; }
        if (PausaMin < 0 || ReintentoMin < 0 || Reintentos < 0)
        {
            return "La pausa, la espera de reintento y los reintentos no pueden ser negativos.";
        }
        if (Reintentos > 5) { return "Maximo 5 reintentos por tramo."; }
        if (MaxHoras is < 0 or > 48) { return "El tiempo maximo debe estar entre 0 (sin limite) y 48 horas."; }
        var n = (Hasta - Desde + Paso - 1) / Paso;
        if (n > MaxTramos) { return $"La secuencia daria {n} tramos; el maximo es {MaxTramos}. Agranda el largo o acorta el rango."; }
        return null;
    }

    /// <summary>Los tramos de la secuencia, en orden (del mas reciente al mas viejo). El ultimo se recorta
    /// para no pasar de <see cref="Hasta"/>.</summary>
    public IReadOnlyList<FlowTramo> Tramos()
    {
        var list = new List<FlowTramo>();
        if (Paso < 1 || Hasta <= Desde) { return list; }
        for (var d = Desde; d < Hasta && list.Count < MaxTramos; d += Paso)
        {
            list.Add(new FlowTramo(list.Count + 1, d, Math.Min(Paso, Hasta - d)));
        }
        return list;
    }

    /// <summary>Overrides de la corrida de un tramo.</summary>
    public Dictionary<string, string> OverridesFor(FlowTramo t) => new(StringComparer.Ordinal)
    {
        [VarDesde.Trim()] = t.Desde.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [VarLargo.Trim()] = t.Largo.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>Resumen humano ("20 tramos de 3: 0-2, 3-5, ... 57-59").</summary>
    public string Describe()
    {
        var t = Tramos();
        if (t.Count == 0) { return "Sin tramos."; }
        static string R(FlowTramo x) => x.Largo == 1 ? $"{x.Desde}" : $"{x.Desde}-{x.Desde + x.Largo - 1}";
        var parts = t.Count <= 4
            ? string.Join(", ", t.Select(R))
            : $"{R(t[0])}, {R(t[1])}, ... {R(t[^1])}";
        return $"{t.Count} tramo(s) de {Paso}: {parts}";
    }
}

/// <summary>Un tramo de la secuencia: numero (1..n), desplazamiento y largo.</summary>
public sealed record FlowTramo(int Numero, int Desde, int Largo);
