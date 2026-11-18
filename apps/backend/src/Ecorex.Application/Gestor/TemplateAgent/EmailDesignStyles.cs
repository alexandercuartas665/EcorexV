using System.Text.RegularExpressions;

namespace Ecorex.Application.Gestor.TemplateAgent;

/// <summary>Un estilo de diseno de correo: clave estable, nombre visible, descripcion corta, colores de la
/// muestra (UI) y la GUIA detallada que recibe el agente.</summary>
public sealed record EmailDesignStyle(string Key, string Nombre, string Descripcion, string[] Muestra, string Guia);

/// <summary>
/// Catalogo de ESTILOS DE DISENO del agente de correos (ADR-0177). El usuario elige uno en el selector del
/// editor; el estilo viaja al inicio de su mensaje como "[estilo: clave]" y el arnes trae la guia de todos
/// (texto estable -> aprovecha el cache del prompt). Cada guia es concreta (medidas, colores, componentes)
/// para que el modelo afine el detalle en vez de improvisar. Solo ASCII.
/// </summary>
public static class EmailDesignStyles
{
    public const string DefaultKey = "corporativo";

    public static readonly IReadOnlyList<EmailDesignStyle> All = new[]
    {
        new EmailDesignStyle("corporativo", "Corporativo",
            "Sobrio y confiable: banda de marca, tarjetas con icono, boton en acento.",
            new[] { "#0d2c4b", "#f4f5f7", "#e8833a" },
            "Fondo exterior gris claro (#f4f5f7); tarjeta blanca de 600px con radio 8px y sombra sutil. Banda superior solida "
            + "en el color PRINCIPAL (padding 28-32px) con el logo (alto 36-44px) o el nombre como wordmark blanco. Hero de "
            + "24-28px bold en gris muy oscuro. Beneficios como filas de mini-tarjetas: celda de icono (emoji en circulo de "
            + "40px con fondo del principal al 10%) + subtitulo bold + una linea. CTA solido en ACENTO, radio 6px. Separadores "
            + "1px #eaeaea. Pie gris claro con datos de contacto y la baja en 11-12px."),

        new EmailDesignStyle("moderno", "Moderno degradado",
            "Fresco y tecnologico: degradado de marca, esquinas amplias, boton pildora.",
            new[] { "#4f46e5", "#9333ea", "#f5f3ff" },
            "Encabezado con DEGRADADO lineal 135deg del color PRINCIPAL al ACENTO (pon tambien background-color solido de "
            + "respaldo para Outlook), padding 40px, hero blanco de 30-34px bold DENTRO del degradado y una bajada de 16px "
            + "en blanco al 85%. Tarjeta con radio 16px. Bloques de contenido sobre fondo tintado muy suave (principal al "
            + "5-8%) con radio 12px. Chips/etiquetas en mayusculas pequenas (11px, letter-spacing 1px) con fondo tintado. "
            + "Una franja de cifras (3 columnas: numero grande 28px bold en principal + rotulo 12px gris). CTA tipo PILDORA "
            + "(border-radius 999px, padding 16x34px) en el acento o en el degradado. Mucho aire (36-44px entre secciones)."),

        new EmailDesignStyle("minimalista", "Minimalista",
            "Limpio y elegante: tipografia protagonista, mucho blanco, cero adornos.",
            new[] { "#111111", "#ffffff", "#2563eb" },
            "Fondo exterior BLANCO (o #fafafa), sin sombra ni banda de color. Logo pequeno (alto 28-32px) alineado a la "
            + "izquierda arriba y, debajo, una linea de acento de 3px x 48px. Titulo 26-30px peso 700 color #111, texto 16px "
            + "#444 con line-height 1.7, ancho de lectura comodo (padding lateral 40-48px). SIN iconos, SIN tarjetas: listas "
            + "con guion largo o numeradas. CTA como boton CONTORNEADO (borde 2px del acento, texto en acento, fondo blanco) "
            + "o un enlace subrayado 'Conoce mas ->'. Pie minimo en una sola linea gris 12px."),

        new EmailDesignStyle("editorial", "Newsletter editorial",
            "Tipo revista: secciones con antetitulo, imagen destacada y columnas.",
            new[] { "#1f2937", "#fef3c7", "#d97706" },
            "Cabecera blanca: logo a la izquierda y a la derecha un rotulo de edicion (p.ej. 'Boletin | Noviembre') en 12px "
            + "gris; debajo una regla de 2px en el principal. Si hay imagen en la galeria, usala como IMAGEN DESTACADA a todo el "
            + "ancho (600px, sin padding). Cada seccion: ANTETITULO en mayusculas 11px letter-spacing 2px color acento, titular "
            + "20-22px bold, resumen de 2-3 lineas y enlace 'Leer mas ->' en el acento. Items secundarios en GRILLA DE 2 COLUMNAS "
            + "(tablas de 50% que en movil quedan legibles), cada uno con imagen pequena si existe. Separadores 1px. Pie con "
            + "sitio web, redes y la baja."),

        new EmailDesignStyle("elegante", "Elegante premium",
            "Exclusivo y sofisticado: fondo oscuro, serif, detalles dorados finos.",
            new[] { "#141414", "#c9a45c", "#2a2a2a" },
            "Fondo exterior casi negro (#0f0f0f) y tarjeta oscura (#1b1b1b o el principal muy oscuro). Titulos en SERIF "
            + "(Georgia, 'Times New Roman', serif) 28-32px color #f5f1e8; texto sans 15px #d6d3cc con line-height 1.7. "
            + "Detalles en dorado/acento (#c9a45c si no hay acento): lineas de 1px, un ornamento centrado (p.ej. una regla "
            + "corta), numeros o iniciales en el acento. Mucho espacio negativo y POCAS palabras. CTA con borde fino 1px del "
            + "acento y texto en mayusculas espaciadas (letter-spacing 2px) o fondo acento con texto oscuro. Logo en version "
            + "clara. Pie sobrio en #8a8780."),

        new EmailDesignStyle("promocional", "Promocional de impacto",
            "Vende ya: oferta gigante, urgencia, beneficios con check y CTA enorme.",
            new[] { "#dc2626", "#fde047", "#111827" },
            "Barra superior delgada de URGENCIA en el acento ('Solo hasta el ...' / 'Cupos limitados') 13px bold centrada. "
            + "Hero con la OFERTA en grande (40-48px, peso 800) y un BADGE (p.ej. '-20%' o 'NUEVO') en circulo o cinta de "
            + "color contrastante. Beneficios con check (la entidad &#10003; en el color acento) en filas cortas. CTA ENORME a todo el ancho de la columna "
            + "(padding 18px, texto 18px bold, radio 8px) y REPETIDO al final. Colores vivos pero con contraste AA (texto "
            + "blanco sobre rojo/oscuro, oscuro sobre amarillo). Fecha limite como texto (sin GIF ni JS). Pie normal con la baja."),

        new EmailDesignStyle("personal", "Carta personal",
            "Escrito por una persona: sin adornos, maxima entregabilidad (llega a Principal).",
            new[] { "#ffffff", "#374151", "#2563eb" },
            "EXCEPCION a la regla de 'no parrafos planos': debe parecer un correo escrito a mano por el asesor. Fondo blanco, "
            + "sin banda, sin tarjetas ni sombras, sin iconos; ancho 560-600px alineado a la izquierda. Texto 15-16px #222, "
            + "parrafos CORTOS (2-3 lineas), saludo con {nombre}, tono cercano y directo. Maximo UN enlace en texto (o un boton "
            + "discreto pequeno). La {firma} es la protagonista al final; el logo, si va, pequeno dentro de la firma. Pie de una "
            + "linea con la baja en 11px gris. Pocas imagenes = mejor entregabilidad."),
    };

    private static readonly Regex Tag = new(@"^\s*\[estilo:\s*([a-z0-9_-]+)\s*\]\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Estilo por clave (insensible a mayusculas); null si no existe.</summary>
    public static EmailDesignStyle? Find(string? key)
        => string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(s => string.Equals(s.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Clave valida normalizada, o null si no corresponde a un estilo del catalogo.</summary>
    public static string? Normalize(string? key) => Find(key)?.Key;

    /// <summary>Etiqueta que el editor antepone al mensaje del usuario para indicar el estilo elegido.</summary>
    public static string TagFor(string key) => $"[estilo: {key}] ";

    /// <summary>Quita la etiqueta de estilo del inicio de un mensaje (para mostrarlo en el chat).</summary>
    public static string StripTag(string text) => string.IsNullOrEmpty(text) ? text : Tag.Replace(text, string.Empty, 1);

    /// <summary>Lee la clave de estilo de la etiqueta al inicio de un mensaje (null si no trae).</summary>
    public static string? ReadTag(string? text)
    {
        if (string.IsNullOrEmpty(text)) { return null; }
        var m = Tag.Match(text);
        return m.Success ? Normalize(m.Groups[1].Value) : null;
    }
}
