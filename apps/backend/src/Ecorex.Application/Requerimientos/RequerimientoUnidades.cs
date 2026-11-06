namespace Ecorex.Application.Requerimientos;

/// <summary>
/// Catalogo por defecto de las unidades de medida mas comunes para los requerimientos de compra.
/// Es la FUENTE UNICA: sirve de fallback cuando el tenant no ha personalizado su lista
/// (TenantConfiguration clave "requerimientos.unidades") y alimenta las opciones estaticas del verbo
/// GENERAR_REQUERIMIENTOS_DESDE_TABLA. Texto en ASCII (convencion del proyecto).
/// </summary>
public static class RequerimientoUnidades
{
    /// <summary>Unidades mas comunes (orden de presentacion). El usuario puede ampliar/recortar por tenant.</summary>
    public static readonly IReadOnlyList<string> Comunes = new[]
    {
        "Unidad",
        "Caja",
        "Paquete",
        "Bulto",
        "Docena",
        "Par",
        "Juego",
        "Kit",
        "Rollo",
        "Resma",
        "Metro",
        "Metro cuadrado",
        "Metro cubico",
        "Centimetro",
        "Pulgada",
        "Kilogramo",
        "Gramo",
        "Libra",
        "Tonelada",
        "Litro",
        "Mililitro",
        "Galon",
        "Caneca",
        "Hora",
        "Dia",
        "Servicio",
        "Global",
    };
}
