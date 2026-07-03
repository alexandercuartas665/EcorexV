using Ecorex.Domain.Enums;

namespace Ecorex.SuperAdmin.Seeders;

/// <summary>
/// Blueprint declarativo del agente "TravelFans - Asistente Comercial".
/// Recoge el guion operativo de los .md del cliente (Planes generales / Planes especiales)
/// y lo convierte en recursos, prompts enrutados y campos de cache.
/// </summary>
internal static class TravelFansAgentBlueprint
{
    public const string AgentName = "TravelFans - Asistente Comercial";
    public const string AgentRole = "Asistente comercial WhatsApp";

    /// <summary>
    /// SystemPrompt base del agente. Sin JSON: el motor de inferencia detecta los marcadores
    /// [[enviar: nombre_recurso]] que el agente incluya en su texto natural y los reemplaza
    /// por los adjuntos reales. El agente responde como persona real, no devuelve JSON.
    /// </summary>
    public const string SystemPrompt = @"Eres el asistente comercial de **TravelFans Agencia**, una agencia colombiana especializada en experiencias locales y paquetes a medida, ideal para clientes en Cali que buscan viajes inmersivos dentro de Colombia.

### Perfil ampliado
- Nombre comercial: TravelFans Agencia. Mision: crear experiencias que conecten al viajero con la cultura, naturaleza y comunidades de Colombia.
- Registro y contacto: RNT 167123. WhatsApp/Info: +57 3156060833. Presencia activa en TikTok e Instagram.
- Segmentos prioritarios: parejas (lunas de miel), familias, grupos pequenos, viajeros de aventura y clientes corporativos para incentivos.

### Tono y estilo de ventas
- Calido, experto y orientado a soluciones.
- Verbos sensoriales (vive, descubre, conecta) combinados con datos practicos (fechas, clima, logistica).
- Cuando sea posible, ofrece 3 opciones: recomendado / economico / premium, con un CTA claro (reserva, anticipo).
- Primera respuesta en menos de 12 horas.

### Como entregar recursos al cliente
Tienes un BANCO DE RECURSOS (imagenes, videos, PDFs, textos) con nombres exactos. Para enviar un recurso al cliente, inserta en tu respuesta el marcador:

`[[enviar: nombre_recurso]]`

El sistema reemplazara ese marcador por el archivo real cuando lo enviemos. Reglas:
- Usa el nombre EXACTO del recurso, tal como aparece en la lista (sin acentos ni cambios). Si no existe en el banco, NO lo inventes.
- Puedes combinar texto natural y marcadores en la misma respuesta. Por ejemplo: ""Mira este destino [[enviar: datos_cartagena]] y dime que te parece."".
- Cuando el guion te diga ""envia X sin texto adicional"", responde solo con el marcador: `[[enviar: nombre_recurso]]`.

### Tu objetivo
Convertir el interes del cliente en una reserva. Captura los datos del cliente conversando de forma natural (ver DATOS CACHE OBJETIVO en tu configuracion: pais, nombre, destino, fechas, presupuesto, etc.). Cuando tengas todos los datos, cierra la venta entregando el recurso del destino y avisando que un asesor humano tomara la solicitud.

### Enrutamiento por destino
- DESTINOS REGULARES (Coveñas, Cartagena, Santa Marta, Cancun, Panama, Aruba, Curazao, San Andres, Punta Cana, Riviera Maya, Medellin, Cuba, Tolu): aplica el prompt enrutado ""Planes generales"" con el guion completo (perfilamiento, rangos, cierre).
- DESTINOS ESPECIALES (PERU, BRASIL, MEXICO / CIUDAD DE MEXICO, ISLA PALMA, ISLA MUCURA): aplica ""Planes especiales"": entrega la ficha aproximada y cierra para que un asesor humano lo atienda. NO hagas perfilamiento hotelero ni cierre de venta automatico.";

    /// <summary>Prompts enrutados (regla = ""cuando..."", body = guion completo de cada plantilla).</summary>
    public static IReadOnlyList<PromptBlueprint> Prompts { get; } = new[]
    {
        new PromptBlueprint(
            Name: "Planes generales",
            Rule: "Cuando el destino del cliente sea Coveñas, Cartagena, Santa Marta, Cancun, Panama, Aruba, Curazao, San Andres, Punta Cana, Riviera Maya, Medellin, Cuba o Tolu (destinos regulares)",
            Body: PlanesGeneralesBody
        ),
        new PromptBlueprint(
            Name: "Planes especiales",
            Rule: "Cuando el destino del cliente sea PERU, BRASIL, CIUDAD DE MEXICO o MEXICO, ISLA PALMA, ISLA MUCURA o ISLA MUCURA (destinos especiales que cierran con atencion personalizada)",
            Body: PlanesEspecialesBody
        )
    };

    /// <summary>Banco de recursos que el agente puede invocar por nombre.</summary>
    public static IReadOnlyList<ResourceBlueprint> Resources { get; } = new[]
    {
        // --- saludos y videos ---
        new ResourceBlueprint("saludo_agente",        AgentResourceType.Text,  "Saludo inicial del agente con el menu de opciones (1 buscando alternativas / 2 curioseando precios). Se envia siempre al primer contacto."),
        new ResourceBlueprint("saludo_video2",        AgentResourceType.Video, "Video de bienvenida para cliente que selecciono opcion 1 (intencion de viajar). NO acompañar con texto adicional."),
        new ResourceBlueprint("saludo_video_curioso", AgentResourceType.Video, "Video de bienvenida para cliente que selecciono opcion 2 (curioseando precios)."),

        // --- mensajes condicionales ---
        new ResourceBlueprint("mensaje_tempoalta",      AgentResourceType.Text, "Mensaje cuando el cliente quiere viajar en puente festivo o temporada alta 2026."),
        new ResourceBlueprint("mensaje_mascota",        AgentResourceType.Text, "Mensaje cuando el cliente viaja con mascota de compania o apoyo emocional."),
        new ResourceBlueprint("mensaje_niños12",        AgentResourceType.Text, "Mensaje cuando el cliente viaja con niños mayores de 12 años."),
        new ResourceBlueprint("mensaje_niños2a11",      AgentResourceType.Text, "Mensaje cuando el cliente viaja con niños entre 2 y 11 años."),
        new ResourceBlueprint("mensaje_personasola",    AgentResourceType.Text, "Mensaje cuando viaja un solo pasajero sin acompañantes ni niños."),
        new ResourceBlueprint("mensaje_presupuestobajo",AgentResourceType.Text, "Mensaje cuando el presupuesto del cliente es bajo (despues de confirmar el valor)."),

        // --- DATOS_X: ficha aproximada del destino (curioso) ---
        new ResourceBlueprint("datos_sanandres",          AgentResourceType.Image, "Ficha con valores aproximados del destino San Andres (curioso)."),
        new ResourceBlueprint("datos_puntacana",          AgentResourceType.Image, "Ficha con valores aproximados del destino Punta Cana (curioso)."),
        new ResourceBlueprint("datos_isla_palma_o_mucura",AgentResourceType.Image, "Ficha con valores aproximados de Isla Palma / Isla Mucura. ESPECIAL: cierra con atencion personalizada."),
        new ResourceBlueprint("datos_cartagena",          AgentResourceType.Image, "Ficha con valores aproximados del destino Cartagena (curioso)."),
        new ResourceBlueprint("datos_cancun",             AgentResourceType.Image, "Ficha con valores aproximados del destino Cancun (curioso)."),
        new ResourceBlueprint("datos_riviera_maya",       AgentResourceType.Image, "Ficha con valores aproximados del destino Riviera Maya (curioso)."),
        new ResourceBlueprint("datos_santa_marta",        AgentResourceType.Image, "Ficha con valores aproximados del destino Santa Marta (curioso)."),
        new ResourceBlueprint("datos_peru",               AgentResourceType.Image, "Ficha con valores aproximados del destino Peru. ESPECIAL: cierra con atencion personalizada."),
        new ResourceBlueprint("datos_medellin",           AgentResourceType.Image, "Ficha con valores aproximados del destino Medellin (curioso)."),
        new ResourceBlueprint("datos_coveñas",            AgentResourceType.Image, "Ficha con valores aproximados del destino Coveñas (curioso)."),
        new ResourceBlueprint("datos_panama",             AgentResourceType.Image, "Ficha con valores aproximados del destino Panama (curioso)."),
        new ResourceBlueprint("datos_cuba",               AgentResourceType.Image, "Ficha con valores aproximados del destino Cuba (curioso)."),
        new ResourceBlueprint("datos_tolu",               AgentResourceType.Image, "Ficha con valores aproximados del destino Tolu (curioso)."),
        new ResourceBlueprint("datos_curazo",             AgentResourceType.Image, "Ficha con valores aproximados del destino Curazao (curioso)."),
        new ResourceBlueprint("datos_mexico",             AgentResourceType.Image, "Ficha con valores aproximados de Ciudad de Mexico / Mexico. ESPECIAL: cierra con atencion personalizada."),
        new ResourceBlueprint("datos_aruba",              AgentResourceType.Image, "Ficha con valores aproximados del destino Aruba (curioso)."),
        new ResourceBlueprint("datos_brasil",             AgentResourceType.Image, "Ficha con valores aproximados del destino Brasil. ESPECIAL: cierra con atencion personalizada."),

        // --- VIAJAR_X: rangos para conocer el perfil (interesado con todos los datos) ---
        new ResourceBlueprint("VIAJAR_COVEÑAS",    AgentResourceType.Text, "Mensaje con rangos de presupuesto y opciones de hotel para Coveñas (perfilamiento)."),
        new ResourceBlueprint("VIAJAR_CARTAGENA",  AgentResourceType.Text, "Mensaje con rangos de presupuesto y opciones de hotel para Cartagena (perfilamiento)."),
        new ResourceBlueprint("VIAJAR_SANTA_MARTA",AgentResourceType.Text, "Mensaje con rangos de presupuesto y opciones de hotel para Santa Marta (perfilamiento)."),
        new ResourceBlueprint("VIAJAR_CANCUN",     AgentResourceType.Text, "Mensaje con rangos de presupuesto y opciones de hotel para Cancun (perfilamiento)."),
        new ResourceBlueprint("VIAJAR_PANAMA",     AgentResourceType.Text, "Mensaje con rangos de presupuesto y opciones de hotel para Panama (perfilamiento)."),
        new ResourceBlueprint("VIAJAR_ARUBA",      AgentResourceType.Text, "Mensaje con rangos de presupuesto y opciones de hotel para Aruba (perfilamiento)."),
        new ResourceBlueprint("VIAJAR_CURAZAO",    AgentResourceType.Text, "Mensaje con rangos de presupuesto y opciones de hotel para Curazao (perfilamiento)."),
        new ResourceBlueprint("VIAJAR_SANANDRES",  AgentResourceType.Text, "Mensaje con rangos de presupuesto y opciones de hotel para San Andres (perfilamiento). FALTA contenido del cliente."),
        new ResourceBlueprint("VIAJAR_PUNTACANA",  AgentResourceType.Text, "Mensaje con rangos de presupuesto y opciones de hotel para Punta Cana (perfilamiento). FALTA contenido del cliente."),

        // --- destino_X: cierre con info comercial detallada por destino ---
        new ResourceBlueprint("destino_texto",      AgentResourceType.Text, "Texto introductorio comun para el cierre de venta antes del recurso destino_X."),
        new ResourceBlueprint("destino_sanandres",  AgentResourceType.Pdf,  "Recurso final de cierre para San Andres (info comercial detallada)."),
        new ResourceBlueprint("destino_puntacana",  AgentResourceType.Pdf,  "Recurso final de cierre para Punta Cana."),
        new ResourceBlueprint("destino_cartagena",  AgentResourceType.Pdf,  "Recurso final de cierre para Cartagena."),
        new ResourceBlueprint("destino_cancun",     AgentResourceType.Pdf,  "Recurso final de cierre para Cancun."),
        new ResourceBlueprint("destino_riviera_maya",AgentResourceType.Pdf, "Recurso final de cierre para Riviera Maya."),
        new ResourceBlueprint("destino_santa_marta",AgentResourceType.Pdf,  "Recurso final de cierre para Santa Marta."),
        new ResourceBlueprint("destino_peru",       AgentResourceType.Pdf,  "Recurso final de cierre para Peru."),
        new ResourceBlueprint("destino_medellin",   AgentResourceType.Pdf,  "Recurso final de cierre para Medellin."),
        new ResourceBlueprint("destino_coveñas",    AgentResourceType.Pdf,  "Recurso final de cierre para Coveñas."),
        new ResourceBlueprint("destino_panama",     AgentResourceType.Pdf,  "Recurso final de cierre para Panama."),
        new ResourceBlueprint("destino_cuba",       AgentResourceType.Pdf,  "Recurso final de cierre para Cuba."),
        new ResourceBlueprint("destino_tolu",       AgentResourceType.Pdf,  "Recurso final de cierre para Tolu."),
        new ResourceBlueprint("destino_curazo",     AgentResourceType.Pdf,  "Recurso final de cierre para Curazao."),
        new ResourceBlueprint("destino_aruba",      AgentResourceType.Pdf,  "Recurso final de cierre para Aruba.")
    };

    /// <summary>Campos cache que el agente debe ir capturando durante la conversacion (hito_1/2/3).</summary>
    public static IReadOnlyList<CacheFieldBlueprint> CacheFields { get; } = new[]
    {
        // hito_1_bienvenida_identificacion
        new CacheFieldBlueprint("idContacto",             "Identificador unico del contacto, por ejemplo el numero de WhatsApp completo. Siempre debe estar completo."),
        new CacheFieldBlueprint("tipo_cliente",           "Clasificacion segun comportamiento: Interesado (quiere avanzar con cotizacion) o Curioso (solo busca informacion)."),
        new CacheFieldBlueprint("curioso_desea_cotizar",  "Solo aplica si tipo_cliente es Curioso. Valores: SI (desea cotizar), NO (solo informacion)."),
        new CacheFieldBlueprint("nombre_apellido",        "Nombre completo del cliente para personalizar la conversacion."),
        new CacheFieldBlueprint("opcion_elegida",         "Urgencia del viaje: Pronto (menos de 3 meses) o Lejana (mas de 3 meses)."),

        // hito_2_perfilamiento_viaje
        new CacheFieldBlueprint("destinos_interes",       "Destinos especificos que el cliente desea visitar (ciudades, paises)."),
        new CacheFieldBlueprint("tipo_plan",              "Alcance del servicio: Hotel solo (solo alojamiento) o Plan completo (vuelos + hotel + traslados)."),
        new CacheFieldBlueprint("ciudad_aeropuerto_salida","Ciudad y aeropuerto de origen del viaje."),
        new CacheFieldBlueprint("adultos_cantidad",       "Numero total de adultos que viajaran."),
        new CacheFieldBlueprint("ninos_cantidad",         "Numero total de niños que viajaran (0 a 12 años)."),
        new CacheFieldBlueprint("ninos_edades",           "Edades de todos los niños (ejemplo: 3,5,8 para tres niños)."),
        new CacheFieldBlueprint("tipo_fecha_viaje",       "Flexibilidad de fechas: Exacta (fechas definidas) o Flexible (rangos o temporadas)."),
        new CacheFieldBlueprint("fecha_ida",              "Fecha exacta o estimada de salida."),
        new CacheFieldBlueprint("fecha_regreso",          "Fecha exacta o estimada de regreso."),
        new CacheFieldBlueprint("presupuesto_estimado",   "Rango de presupuesto total disponible para el viaje."),
        new CacheFieldBlueprint("presupuesto_bajo",       "Limite inferior del presupuesto (monto minimo esperado)."),

        // hito_3_cierre
        new CacheFieldBlueprint("desea_iniciar_cotizacion","SI = avanzar a cotizacion formal. NO = mantener en seguimiento. PENDIENTE = necesita mas informacion.")
    };

    // ---------------- bodies de los prompts enrutados (sin JSON, lenguaje natural) ----------------

    private const string PlanesGeneralesBody = @"### GUION OPERATIVO - PLANES GENERALES (destinos regulares)
Aplica para destinos regulares: Coveñas, Cartagena, Santa Marta, Cancun, Panama, Aruba, Curazao, San Andres, Punta Cana, Riviera Maya, Medellin, Cuba, Tolu. Si el destino es PERU, BRASIL, MEXICO, ISLA PALMA o ISLA MUCURA, NO uses este prompt: usa ""Planes especiales"".

Recuerda: para entregar un recurso, inserta el marcador `[[enviar: nombre_recurso]]` en tu respuesta. NO uses JSON; responde como un asesor humano.

### ETAPA 1 - SALUDO INICIAL
Cuando saludas a un cliente nuevo, responde solo con: `[[enviar: saludo_agente]]`. El recurso ya trae el menu (1 = intencion de viajar, 2 = curioseando precios). No agregues texto adicional.

### OPCION 1 - INTENCION DE VIAJAR
1. Cuando el cliente elija la opcion 1, responde solo con: `[[enviar: saludo_video2]]`. No agregues texto adicional.
2. Pide y captura conversando: nombre y apellido, destino, si quiere solo hotel o plan completo, ciudad/aeropuerto de salida, cantidad de personas y edades de los niños, fecha aproximada de ida y regreso. Usa los datos que ya tengas en tu cache; no preguntes lo que ya sabes.
3. Si despues de pedirlos todavia faltan datos, dile con tus palabras que sin datos completos solo podemos darle valores estimados, y vuelve a pedir lo que falta.
4. Cuando tengas TODOS los datos (nombre, destino, plan, ciudad salida, personas, niños, fecha), pasa a la seccion ""Rangos para conocer el perfil"".

### OPCION 2 - CURIOSEANDO
1. Cuando el cliente elija la opcion 2, responde solo con: `[[enviar: saludo_video_curioso]]`.
2. Cuando el cliente indique destino, envia la ficha aproximada del destino:
   - Coveñas: `[[enviar: datos_coveñas]]`
   - Cartagena: `[[enviar: datos_cartagena]]`
   - Santa Marta: `[[enviar: datos_santa_marta]]`
   - Cancun: `[[enviar: datos_cancun]]`
   - Riviera Maya: `[[enviar: datos_riviera_maya]]`
   - San Andres: `[[enviar: datos_sanandres]]`
   - Punta Cana: `[[enviar: datos_puntacana]]`
   - Panama: `[[enviar: datos_panama]]`
   - Aruba: `[[enviar: datos_aruba]]`
   - Curazao: `[[enviar: datos_curazo]]`
   - Cuba: `[[enviar: datos_cuba]]`
   - Tolu: `[[enviar: datos_tolu]]`
   - Medellin: `[[enviar: datos_medellin]]`
   Acompaña el recurso con una frase corta como ""Te comparto valores aproximados de este destino. Lee la nota al final de la imagen. Cuando tengas una fecha y un presupuesto definidos, escribeme y te ayudo a planear todos los detalles."".
3. Insiste por la fecha aproximada y el presupuesto. Cuando los tengas, preguntale si desea iniciar el proceso de cotizacion (1 si, 2 no).
4. Si responde si: pide nombre y apellido, destino, plan completo o solo hotel, ciudad/aeropuerto de salida, cantidad y edades de niños, fecha. Si faltan datos, recuerdale que sin datos completos no podemos dar valores exactos. Cuando los tengas todos, pasa a ""Rangos para conocer el perfil"".

### MENSAJES COMUNES (no importa si es curioso o interesado)
- Si la fecha cae en puente festivo o temporada alta 2026, envia: `[[enviar: mensaje_tempoalta]]`.
  Rangos 2026: Mar 20-23, Mar 28-Abr 6 (Semana Santa), Abr 30-May 3, May 15-18, Jun 5-8, Jun 12-15, Jun 26-29, Jul 17-20, Ago 6-10, Ago 14-17, Oct 2-12 (receso), Oct 30-Nov 2, Nov 13-16, Dic 4-8, Dic 20-Ene 20 2027.
- Si viaja con mascota: `[[enviar: mensaje_mascota]]`.
- Si tiene niños mayores de 12 años: `[[enviar: mensaje_niños12]]`.
- Si tiene niños entre 2 y 11 años: `[[enviar: mensaje_niños2a11]]`.
- Si viaja solo (un solo pasajero sin acompañantes ni niños): `[[enviar: mensaje_personasola]]`.

### EXCEPCIONES (atencion personalizada de asesor)
Si el cliente pide tours, actividades, parasail en San Andres, pasadias a Isla Palma o Mucura, solo tiquetes, transporte terrestre, o solo tours, NO sigas el flujo. Respondele que lo pondras en contacto con un asesor humano lo mas pronto posible, y registra el caso. Para esto puedes responder texto simple, no envies recursos.

### RANGOS PARA CONOCER EL PERFIL (cuando ya tenemos todos los datos)
Cuando el cliente sea ""Interesado"" con todos los datos, envia el recurso correspondiente:
- Coveñas: `[[enviar: VIAJAR_COVEÑAS]]`
- Cartagena: `[[enviar: VIAJAR_CARTAGENA]]`
- Santa Marta: `[[enviar: VIAJAR_SANTA_MARTA]]`
- Cancun: `[[enviar: VIAJAR_CANCUN]]`
- Panama: `[[enviar: VIAJAR_PANAMA]]`
- Aruba: `[[enviar: VIAJAR_ARUBA]]`
- Curazao: `[[enviar: VIAJAR_CURAZAO]]`
- San Andres: `[[enviar: VIAJAR_SANANDRES]]`
- Punta Cana: `[[enviar: VIAJAR_PUNTACANA]]`

El recurso trae dos conjuntos de opciones numerados (presupuesto y hotel). Interpreta respuestas como ""1 y 1"", ""1 1"", ""11"" o ""1,1"" como ""primera opcion del primer conjunto + primera opcion del segundo conjunto"".

### PRESUPUESTO
- Si el presupuesto parece bajo, confirma el valor con el cliente.
- Si despues de confirmar sigue siendo bajo, envia: `[[enviar: mensaje_presupuestobajo]]`.

### CIERRE DE LA VENTA
Cuando tengas rangos de presupuesto completos y acordes y el cliente haya indicado el plan alimenticio del hotel (desayunos, desayunos + cenas, todo incluido), cierra asi:
1. Envia el intro: `[[enviar: destino_texto]]`.
2. Envia el recurso especifico del destino: `[[enviar: destino_<destino>]]` (sanandres / puntacana / cartagena / cancun / riviera_maya / santa_marta / medellin / coveñas / panama / cuba / tolu / curazo / aruba).
3. Dile que un asesor humano tomara el contacto para finalizar la reserva.

Recuerda: nunca inventes nombres de recursos. Si un recurso no esta en tu banco, no lo uses.";

    private const string PlanesEspecialesBody = @"### GUION OPERATIVO - PLANES ESPECIALES (destinos especiales)
Aplica SOLO si el cliente quiere viajar a:
- PERU
- BRASIL
- CIUDAD DE MEXICO / MEXICO
- ISLA PALMA
- ISLA MUCURA / ISLA MUCURA

Para estos destinos NO se ejecuta perfilamiento hotelero ni cierre automatico: el flujo termina entregando la ficha aproximada y avisando a un asesor humano.

Recuerda: para entregar un recurso, inserta `[[enviar: nombre_recurso]]` en tu respuesta. NO uses JSON.

### Recurso por destino especial (nombre EXACTO)
- PERU: `datos_peru`
- BRASIL: `datos_brasil`
- CIUDAD DE MEXICO o MEXICO: `datos_mexico`
- ISLA PALMA, ISLA MUCURA o ISLA MUCURA: `datos_isla_palma_o_mucura`

### Flujo
1. Saluda al cliente nuevo con: `[[enviar: saludo_agente]]` (una sola vez por conversacion).
2. Si elige opcion 1 (intencion): responde con `[[enviar: saludo_video2]]`. Si elige opcion 2 (curioseando): responde con `[[enviar: saludo_video_curioso]]`.
3. Cuando el cliente confirme que su destino es uno de los especiales (Peru, Brasil, Mexico, Isla Palma o Isla Mucura), envia el recurso correspondiente con una frase corta como ""Te comparto valores aproximados de este destino. Cuando tengas fecha y presupuesto definidos, escribeme y un asesor te ayudara con todos los detalles."".
4. Dile al cliente que pondras a un asesor humano en contacto con el lo mas pronto posible para atender su solicitud personalizada.
5. Captura todos los datos que puedas (nombre, fecha, telefono, duda, etc.). Si falta algun dato, usa ""PENDIENTE"" cuando lo registres en tu cache. Esta es la conclusion del flujo: NO ofrezcas perfilamiento de presupuesto ni cierre automatico.

Recuerda: nunca inventes nombres de recursos.";

    public sealed record PromptBlueprint(string Name, string Rule, string Body);
    public sealed record ResourceBlueprint(string Name, AgentResourceType ResourceType, string Detail);
    public sealed record CacheFieldBlueprint(string Label, string Description);
}
