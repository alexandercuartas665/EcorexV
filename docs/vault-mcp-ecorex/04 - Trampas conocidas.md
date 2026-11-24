---
tipo: guia
tema: trampas
origen: lecciones reales de uso en produccion
---

# Trampas conocidas (y como evitarlas)

Volver a [[00 - LEEME PRIMERO]]. Todo lo de esta nota paso de verdad operando el sistema por MCP. Leerla
antes de construir ahorra horas y evita danos en datos.

## Contenedores de datos

1. **No se puede borrar una columna que ya tiene datos** (`eliminar_columna` lo rechaza) ni vaciarla con ella.
   -> Disena nombres y tipos ANTES de importar. Si necesitas "resetear": `limpiar_marca` (pone NULL) o
   `rellenar_columna` (recalcula). Si definitivamente sobra, renombrala (`editar_columna` nombre `x_old`) y crea otra.
2. **`editar_columna` SI cambia tipo y nombre aunque haya datos.** Si importaste un monto como `texto`, pasalo a
   `decimal` con `editar_columna` (tipo `decimal`) y ya se puede sumar.
3. **Para sumar/promediar, la columna debe ser `numero` o `decimal`.** Un reporte sobre columnas texto responde
   "Ningun campo es numerico: solo se puede contar". Usa `decimal` para dinero.
4. **Eliminar y recrear una tabla cambia su `tabla_id`.** Eso rompe los reportes (su fuente es
   `container:{tabla_id}`) y el destino de drones y conectores. Evitalo; si no hay remedio, re-apunta todo.
5. **Upsert exige que la `columna_clave` este MAPEADA** en el conector (`fields`). Si no: "La columna clave debe
   estar mapeada a un campo del API".
6. **Tope de 50.000 filas por corrida.** El detalle dice "Se alcanzo el limite de 50000 filas por corrida; el
   resto no se importo". Parte la carga por ventanas (fechas) o pagina en varias corridas.
7. **La URL del conector admite maximo 1.000 caracteres.** Un `$where` con muchas palabras clave no cabe:
   prioriza las mas fuertes o filtra despues con IA.
8. **Paginacion sin `maxPages` = hasta 1.000 paginas.** Los defaults son `offsetParam:"start"`,
   `limitParam:"limit"`, `pageSize:100`, `maxPages:1000`. Fija SIEMPRE `pageSize` y `maxPages` explicitos.
   La paginacion para sola cuando una pagina trae menos de `pageSize` filas.
9. **Timeout "Tiempo de espera agotado al llamar al API".** Paginas enormes (50.000 filas) tardan ~30 s cada una;
   varias seguidas revientan la corrida y **no se importa nada**. -> paginas mas chicas, `maxPages` bajo y una
   consulta mas angosta (traer "lo reciente" ordenado por fecha con `$limit`, no "todo").
10. **Fechas de APIs suelen llegar como texto** (`dd/mm/yyyy H:mm:ss`). Sirven para mostrar, no para ordenar o
    filtrar por rango. Si necesitas rangos, crea la columna como `fecha` y verifica el formato.
11. **Datos abiertos (Socrata, `datos.gov.co`):** un `$where` sobre una fecha guardada como texto obliga a recorrer
    todo (lento); `$order=<fecha> DESC&$limit=N` es rapidisimo para "lo mas reciente". Un `$where` con fecha fija
    NO "rueda": la ventana se queda quieta. Socrata topa `$limit` en 50.000.
12. **Campos anidados del JSON** se mapean con ruta de puntos: `{"column":"cliente","path":"customer.name"}`.
13. **No hay herramienta MCP para LEER filas** ni para "importar ahora". Para ver datos: crea un reporte en modo
    tabla (o usa `ver_reporte`), que devuelve una muestra. Para importar ya: el boton de la pantalla del
    contenedor, o una programacion.
14. **Lee el conector con `ver_conector` antes de tocarlo** (`listar_conectores` NO muestra endpoint ni mapeo).
    `guardar_conector` en edicion cambia SOLO lo que envias y conserva lo demas. Si envias `mapeo_json`, ese mapeo
    REEMPLAZA al anterior completo (campos + paginacion): parte del `mapeo_json` que devuelve `ver_conector`.
    Los parametros sensibles del endpoint salen como `***`: no reenvies ese endpoint (lo rechaza). Ojo con la
    paginacion `Offset`/`Page`: el motor reescribe el `$limit` del endpoint con `pageSize` en cada pagina; si quieres
    que mande el `$limit` del endpoint usa `"paging":{"mode":"None"}`. Una importacion REST paginada tiene tope de
    20 min por corrida (se detiene, conserva lo importado y deja el motivo en el detalle).
15. **`publicar_contenedor` necesita `padre`** (seccion del menu) si la vista del menu no tiene donde colgarlo.

## Drones de extraccion

16. **Sin `ingesta_llave` el paso `extraer` AGREGA filas en cada corrida (duplica).** Ponle una columna unica y
    mapeada (ej. `url`) para que haga upsert.
17. **Si ninguna columna es unica** (ej. el mismo PDF aparece bajo varios procesos): genera una **clave compuesta**
    en el JS (`clave = numero + '|' + url`) en una columna nueva. Orden obligatorio: (1) crear la columna,
    (2) `rellenar_columna` en las filas viejas con la MISMA plantilla (`{numero}|{url}`), (3) recien ahi poner
    `ingesta_llave`. Si activas la llave sin el paso 2, las filas viejas (clave vacia) quedan como duplicados
    huerfanos para siempre.
18. **El paso `esperar` usa `espera_ms`**, no `mapeo_json {"ms":6000}`. Si no: "no tiene ni selector ni
    milisegundos".
19. **No puedes leer el script de un paso por MCP** (`ver_dron` solo dice `tiene_script`). `editar_paso` con
    `script` lo REEMPLAZA completo. Conserva tu script en un archivo.
20. **Dedup dentro de la corrida:** en el JS usa un objeto `vistos` para no emitir la misma fila dos veces.
21. **El dron corre en un agente colmena** (navegador real del cliente). Si esta apagado: resultado
    `agente_desconectado`. No hay herramienta para "correr ya": usa una programacion o "Ejecutar ahora" en
    /extraccion-datos.
22. **reCAPTCHA = no uses dron.** Busca la API o los datos abiertos y usa un conector. Pero los retos JS tipo
    anti-bot (Radware y similares) normalmente SI los pasa el navegador real: prueba antes de descartar.

## Clasificacion con IA

23. **El agente debe estar ENCENDIDO** (`encender_agente`) o `clasificar_lote` falla.
24. **El prompt debe forzar salida SOLO JSON** con la columna clave y las salidas exactas:
    `[{"id_pedido":"...","categoria":"..."}]`. Texto libre = filas `sin_respuesta`.
25. **Clasifica por el OBJETO** (nombre + descripcion), no por quien lo pide. Dilo en el prompt.
26. **`sin_respuesta` en filas con nombres genericos o solo codigos** es limite de la data, no del agente.
27. **`ejecutar_clasificacion_ahora` es asincrona.** El motor la toma en < 1 min, pero cada lote es una llamada de
    IA: 200 lotes pueden tardar horas. El resultado sale en `listar_corridas_importacion` (detalle).
28. **El paso automatico solo corre si la importacion termina OK.** Si la corrida falla (timeout), no clasifica y
    el pendiente se congela. Drena con `ejecutar_clasificacion_ahora` y arregla la importacion.
29. **`max_lotes` por defecto 20** (1.000 filas por corrida con lotes de 50); tope 200. Para drenar un backlog
    subelo temporalmente y luego bajalo (con OK del usuario: tokens).
30. **Para cambiar la taxonomia** no borres columnas: `limpiar_marca` y reclasifica (confirma: tokens).
31. **Monitorea con un reporte** agrupado por la columna de salida: el grupo `null` son los pendientes.
    `ver_reporte` re-ejecuta y da el numero fresco.
32. **Bucles locales mueren si el PC se reinicia.** Para volumenes grandes prefiere lo que corre en el servidor
    (paso de clasificacion + `max_lotes`) sobre un script en el PC.

## Reportes e indicadores

33. **Ordenar por una agregacion** usa el nombre de la agregacion: `orden=[{"campo":"suma","desc":true}]`
    (`conteo`, `suma`, `promedio`, `minimo`, `maximo`), no el nombre de la columna.
34. **Los filtros quedan fijos** en el reporte (no son interactivos). Para "solo activos" crea un reporte aparte
    con el filtro (`operador:"en"`, `valores:["Abierto","Publicado"]`).
35. **Un contenedor admite un solo `agrupar_por`, y agrupa por el valor EXACTO del campo**: no hay agrupacion por
    mes/anio de una fecha. Para "ventas por mes" la tabla necesita una columna de texto `mes` (`AAAA-MM`) que llene
    quien importa los datos (mapeo del conector, script del dron o agente); `rellenar_columna` NO extrae el mes de
    una fecha. Disena esa columna desde [[R05 - Crear un contenedor de datos]] si el usuario pedira reportes por
    periodo.
36. **Una tabla pre-filtrada sesga los porcentajes** (si solo entra lo de tecnologia, "todo es tecnologia"). Los %
    reales salen de la tabla universo.
37. **El libro de indicadores nace privado del usuario** que lo crea (con la "Hoja 1"); los reportes de galeria
    aparecen en la pestana de su `categoria`.

## Formularios

38. **Llama `describe_components` primero**: es la documentacion viva (tipos de control, formatos de grilla, calculo).
39. **`options_json` de listas = `[{"id":"BOG","label":"Bogota"}]`**: `id` es OBLIGATORIO (`value` y `desc` opcionales).
40. **Calculos:** referencias SIEMPRE `{codigo}`; no existe `vlookup()` (es una columna `resolve` de una grilla);
    formato `percent` guarda 5 = 5 (divide entre 100); sumar una columna de grilla NO es formula: columna con
    `agg=Sum` + `rollup=<campo destino>`, y el destino sin `calc_expression`.
41. **`apply_form_spec`**: contenedores en orden (padres antes que hijos); re-aplicar la misma spec ACTUALIZA, no
    duplica; si algo falla se revierte todo.
42. **`verify_form` hasta `errors=0`** antes de `activate`.
43. **Marca/colores con `set_theme(color=#RRGGBB)`**, no con CSS a mano. En `eyebrow` usa el emoji directo.
44. **`create_share_link` devuelve el token del enlace UNA sola vez**: entregalo al usuario en ese momento.

- **`update_question` NO es parcial:** los atributos que omitas vuelven a su valor por defecto (puedes borrar sin
  querer opciones, formulas o lookups). Para editar, re-aplica `apply_form_spec` (upsert por `field_code`) o
  envia TODOS los atributos actuales tomados de `get_form`.

## Flujos

45. **Un nodo que no es compuerta sigue TODAS sus salidas en paralelo.** Dos flechas saliendo de una tarea =
    ramas paralelas (casi nunca es lo que se quiere). Para decidir usa una **compuerta**.
46. **Condicion de ruta** en formato del motor: `approval == 'Aprobado'`; vacia = ruta por defecto.
47. **`agregar_nodo` sin `desde_nodo_id`/`hasta_nodo_id` queda suelto**: conectalo con `conectar_nodos`.
48. **`publicar_flujo` no publica si hay avisos**: muestralos; `forzar=true` solo con OK del usuario.
49. **Un flujo publicado no se edita:** `nueva_version_flujo` devuelve ids NUEVOS de nodos y aristas; vuelve a leer
    con `ver_flujo` antes de seguir editando.
50. **Sin `enganchar_flujo_a_concepto`** las tareas nuevas no arrancan el flujo.

## Tareas

51. **Tarea con flujo en curso:** no `mover_tarea` ni `terminar_tarea`; `completar_paso` con la `opcion` EXACTA que
    muestra `ver_tarea`.
52. **Columnas de cierre** pueden exigir `motivo_cierre`.
53. **`ver_tablero` puede forzar `alcance=mias`** si el tablero restringe la vista del usuario.

## Agentes de IA

54. **Nace apagado.** Valida (prueba con un lote chico o una conversacion) y luego enciendelo.
55. **Omitir `herramientas_deshabilitadas` = TODAS habilitadas** (incluida la de escribir en contenedores).
    Para un agente de solo lectura, deshabilita lo que no deba usar.
56. **El proveedor debe estar activo en el gateway de IA del plan** de la empresa; si no, el agente falla al responder.
57. **Al enviar una lista en `editar_agente`** (herramientas, tableros, round-robin) reemplazas la lista completa.

## Generales

58. **Scripts: cuerpo en UTF-8** y la respuesta util esta en `result.content[0].text` (quita el prefijo `data:`).
59. **Casi todo acepta nombre o id**, pero si hay dos cosas con el mismo nombre usa el id.
60. **Lee los avisos** de cada respuesta: suelen decir exactamente que falta o que se hizo a medias.

Siguiente: [[05 - Solucion de problemas]].
