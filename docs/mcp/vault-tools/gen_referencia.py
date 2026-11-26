# -*- coding: utf-8 -*-
"""Genera la REFERENCIA del vault MCP de ECOREX a partir del catalogo VIVO (tools/list) del MCP de
tableros y de las specs en codigo de los MCP de Maquetas y WhatsApp. Salida 100% ASCII (regla del repo)."""
import json, os, re, unicodedata

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
CATALOGO = os.path.join(HERE, "catalogo.local", "tools_list.json")   # lo baja bajar_catalogo.ps1 (ignorado por git)
VAULT = os.path.join(REPO, "docs", "vault-mcp-ecorex")
REF = os.path.join(VAULT, "Referencia")
os.makedirs(REF, exist_ok=True)

def ascii_(s):
    if s is None:
        return ""
    s = (s.replace("\u2013", "-").replace("\u2014", "-").replace("\u2192", "->").replace("\u2264", "<=")
          .replace("\u2265", ">=").replace("\u201c", '"').replace("\u201d", '"').replace("\u2018", "'")
          .replace("\u2019", "'").replace("\u2026", "...").replace("\u00d7", "x").replace("\u00b7", "-"))
    s = unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode("ascii")
    return s

def nolinks(s):
    # Las descripciones citan SINTAXIS de enlaces ([[Titulo]], ![[img]]): en Obsidian serian enlaces rotos.
    return re.sub(r"(!?\[\[[^\]]*\]\])", r"`\1`", s)

def write(path, text):
    text = ascii_(text)
    with open(path, "w", encoding="ascii", newline="\n") as f:
        f.write(text)

# ------------------------------------------------------------------ catalogo vivo tableros
tools = json.load(open(CATALOGO, encoding="utf-8"))["result"]["tools"]
by_name = {t["name"]: t for t in tools}

MODULES = [
    ("01", "Tableros y tareas", "tableros-tareas",
     "listar_tableros ver_tablero buscar_tareas ver_tarea mis_pasos_pendientes listar_conceptos listar_usuarios "
     "crear_tarea mover_tarea terminar_tarea comentar asignar_tarea checklist registrar_tiempo tomar_paso completar_paso guia_ecorex",
     """Opera el trabajo diario: tableros Kanban, tareas (= actividades, se identifican por numero tipo `T00123`),
comentarios, checklist, horas y los PASOS de flujo que tiene el usuario.

Conceptos clave:
- Una tarea puede estar **gobernada por un flujo** (BPMN). Si lo esta, NO se mueve de columna ni se termina a mano:
  se avanza con `completar_paso` (eligiendo la `opcion` exacta si el paso tiene rutas).
- Un **concepto** (tipo de actividad, ver `listar_conceptos`) clasifica la tarea; si el concepto tiene flujo
  enganchado, `crear_tarea` con ese concepto ARRANCA el flujo.
- Empieza siempre por `listar_tableros` o `mis_pasos_pendientes`.
- `guia_ecorex` es ESTA guia servida por el MCP: sin argumentos da la nota de entrada; `nota` abre una nota por
  titulo; `buscar` encuentra notas por texto (por ejemplo, el texto de un error).

Recetas: [[R01 - Operar tareas y tableros]]"""),
    ("02", "Directorio de contactos", "directorio",
     "buscar_contacto ver_contacto crear_contacto editar_contacto nota_contacto vincular_persona_empresa "
     "contacto_interno tareas_del_contacto listar_campos_contacto inactivar_contacto reactivar_contacto vincular_contacto",
     """El Directorio General (terceros): empresas y personas (clientes, proveedores, prospectos).

Reglas:
- **Busca antes de crear** (`buscar_contacto`): `crear_contacto` no duplica por identificacion/telefono, pero
  buscar primero evita fichas casi-iguales.
- `editar_contacto` cambia SOLO lo que envies. Para campos personalizados consulta `listar_campos_contacto`
  antes de usar `campos`.
- Confirma con el usuario antes de `inactivar_contacto`.

Recetas: [[R02 - Directorio de contactos]]"""),
    ("03", "Flujos de proceso (BPMN)", "flujos",
     "listar_flujos ver_flujo crear_flujo nueva_version_flujo agregar_nodo renombrar_nodo conectar_nodos "
     "eliminar_conexion condicion_ruta asignar_encargado_nodo asignar_cargo_nodo poner_formulario_a_nodo eliminar_nodo "
     "importar_bpmn publicar_flujo enganchar_flujo_a_concepto "
     "nombre_salida decision_humana_nodo reinicio_nodo tablero_destino_nodo concepto_cierre_nodo color_nodo nota_nodo "
     "salto_a_flujo_nodo plazo_nodo tiempo_arranque_nodo agregar_notificacion_nodo quitar_notificacion_nodo "
     "quitar_formulario_nodo formulario_obligatorio_por_salida formulario_crear_al_llegar listar_reglas_disponibles "
     "agregar_regla_nodo quitar_regla_nodo regla_autonoma_nodo editar_flujo pausar_flujo reanudar_flujo "
     "desenganchar_flujo_de_concepto opciones_agente_nodo ver_agente_nodo agente_nodo quitar_agente_nodo "
     "instrucciones_agente_nodo correo_agente_nodo web_agente_nodo whatsapp_agente_nodo voz_agente_nodo "
     "canales_agente_nodo fallo_agente_nodo seguimiento_agente_nodo tope_preguntas_agente_nodo "
     "traspaso_salida_agente_nodo",
     """Diseno de procesos (WorkflowEngine BPMN 2.0): nodos tarea (paso humano), compuertas (decision), inicio y fin.

> Para disenar procesos reales usa el CEREBRO DE FLUJOS: `guia_ecorex` con `tema` = `flujos` (decision humana vs
> compuerta automatica, avisos con enlaces de decision, plazos, tablero y cierre, agente de IA del paso).

Reglas:
- `crear_flujo` nace con inicio->fin YA conectados. Inserta pasos con `agregar_nodo` dando `desde_nodo_id` y
  `hasta_nodo_id` (reemplaza esa conexion directa).
- Un nodo que NO es compuerta sigue TODAS sus salidas en paralelo. Para decidir usa una **compuerta**: automatica
  (cada salida con `nombre_salida` + `condicion_ruta` approval == '<nombre>'; una sin condicion = por defecto) o de
  decision humana (`decision_humana_nodo`).
- Encargado del paso: `asignar_encargado_nodo` (politica/hereda_inicio/hereda_anterior/campo_formulario); con
  `politica` agrega cargos con `asignar_cargo_nodo`.
- Revisa `ver_flujo`: si trae **avisos**, muestraselos al usuario antes de `publicar_flujo`.
- Un flujo publicado no se edita: usa `nueva_version_flujo`.
- Para que las tareas lo arranquen: `enganchar_flujo_a_concepto`.

Recetas: [[R03 - Disenar y publicar un flujo]]"""),
    ("04", "Drones de extraccion web", "drones",
     "listar_drones ver_dron listar_destinos_extraccion listar_reglas_extraccion listar_corridas listar_programaciones "
     "crear_dron editar_dron agregar_paso editar_paso reordenar_pasos definir_variable guardar_programacion "
     "eliminar_paso eliminar_variable eliminar_programacion eliminar_dron",
     """Un **dron** es un robot de navegador que recorre un sitio web y extrae datos a una tabla de contenedor.
Corre en un **agente colmena** (PC/servidor con navegador real del cliente), no en la nube.

Estructura: cabecera (`crear_dron`: nombre + `url_inicio` + tabla destino) -> **pasos** en orden
(`agregar_paso`: navegar, esperar, clic, inyectar_script, extraer, ia, procesar_archivos, ...) -> **variables**
(`definir_variable`, marca `secreta` para claves) -> **programaciones** (`guardar_programacion`: manual, intervalo,
cron o barrido por tramos) -> bitacora (`listar_corridas`).

Reglas:
- El paso `extraer` lleva un `script` JS que DEVUELVE un arreglo de filas + `mapeo_json` (campo->columna) +
  `contenedor_destino`. `ingesta_llave` = columna clave para UPSERT (sin ella, cada corrida AGREGA filas).
- El paso `esperar` usa `espera_ms` (no `mapeo_json`).
- Si el sitio tiene reCAPTCHA o hay API publica, NO uses dron: usa un conector API (ver [[R06 - Conector API e importacion programada]]).

Recetas: [[R08 - Crear un dron de extraccion]]"""),
    ("05", "Contenedores de datos", "contenedores",
     "listar_contenedores_datos ver_contenedor_datos crear_contenedor_datos editar_contenedor_datos "
     "eliminar_contenedor_datos agregar_tabla editar_tabla eliminar_tabla agregar_columna editar_columna "
     "eliminar_columna agregar_relacion eliminar_relacion listar_conectores ver_conector guardar_conector eliminar_conector "
     "listar_programaciones_importacion guardar_programacion_importacion eliminar_programacion_importacion "
     "listar_corridas_importacion ver_publicacion_contenedor publicar_contenedor despublicar_contenedor agregar_filas ver_filas",
     """Un **contenedor de datos** es una mini base de datos del cliente (modelo ER): contenedor -> **tablas** ->
**columnas** (texto, numero, decimal, fecha, booleano, submodelo) + **relaciones** (n1 / nn).

Se llena por **conectores** (fuentes: excel, api REST, bd, tabla) + **programaciones de importacion** (manual,
intervalo, cron; modo append / replace / upsert por `columna_clave`), por drones, por agentes de IA, o en poca
cantidad directo por MCP con `agregar_filas` (max 100 por llamada). `ver_filas` lee filas para verificar.
Cada tabla raiz puede **publicarse** como modulo del menu y es **fuente de reportes** (`container:{tabla_id}`).

Reglas:
- **Los conectores NO reciben secretos por MCP**: claves/API keys se ponen en la pantalla del contenedor.
- **No se puede borrar una columna que ya tiene datos** (ni vaciarla con eliminar). Planea nombres y tipos antes
  de importar; para "resetear" una columna usa `limpiar_marca` o `rellenar_columna` (modulo Clasificacion).
- Para sumar/promediar montos la columna debe ser `numero` o `decimal` (no texto).
- Upsert: la `columna_clave` debe estar MAPEADA en el conector.
- `ver_conector` muestra el endpoint, el mapeo y la paginacion de un conector (sin secretos: los parametros
  sensibles salen como `***`). Leelo SIEMPRE antes de editar.
- `guardar_conector` en EDICION cambia solo lo que envies; lo omitido (mapeo, destino, auth...) se conserva.
  No reenvies un endpoint con `***` (lo rechaza).
- `agregar_filas` valida cada valor contra el tipo de su columna (decimal con punto y sin miles, fecha AAAA-MM-DD,
  booleano true/false/si/no) y las obligatorias: UN error rechaza TODO el lote (la respuesta lista los errores).
  Con `columna_clave` actualiza la fila existente (upsert). No sirve en tablas derivadas.

Recetas: [[R05 - Crear un contenedor de datos]] - [[R06 - Conector API e importacion programada]]"""),
    ("06", "Agentes de IA", "agentes",
     "listar_agentes ver_agente historial_prompts_agente crear_agente editar_agente agente_lector_medios "
     "encender_agente apagar_agente duplicar_agente restaurar_version_prompt agregar_recurso_agente "
     "editar_recurso_agente agregar_prompt_agente editar_prompt_agente eliminar_agente eliminar_recurso_agente "
     "eliminar_prompt_agente",
     """Agentes de IA del modulo de agentes (los mismos que atienden WhatsApp, clasifican datos o leen documentos).
Proveedores: claude, gemini, chatgpt, deepseek (el gateway del plan del cliente debe tener ese proveedor activo).

Reglas:
- `crear_agente` nace **APAGADO**: valida y luego `encender_agente`.
- `herramientas_deshabilitadas` es una lista de exclusion: si la omites, el agente tiene TODAS sus herramientas
  (incluida `agregar_filas_a_contenedor`, con la que escribe en un contenedor).
- `editar_agente` cambia solo lo que envies. El lector de medios (como lee PDF/imagenes) va en `agente_lector_medios`.
- Cada cambio de prompt queda versionado (`historial_prompts_agente`, `restaurar_version_prompt`).

Recetas: [[R09 - Crear y configurar un agente de IA]]"""),
    ("07", "Clasificacion y datos masivos con IA", "clasificacion",
     "clasificar_lote limpiar_marca rellenar_columna configurar_paso_clasificacion quitar_paso_clasificacion "
     "ejecutar_clasificacion_ahora",
     """Enriquecer filas de un contenedor con IA (ej. poner `sector` a miles de procesos) y operaciones masivas sobre
columnas.

Patron de la **marca**: una columna (ej. `clasificado_en`) vacia = fila pendiente. La IA solo toca filas con la
marca vacia y, al escribir el resultado, pone la marca con fecha UTC. Asi todo es incremental e idempotente.

- `clasificar_lote`: un lote manual (max 50 filas). Repetir hasta `restantes` = 0.
- `configurar_paso_clasificacion`: lo vuelve AUTOMATICO como paso de una programacion de importacion (tras cada
  corrida OK clasifica lo nuevo, hasta `max_lotes`).
- `ejecutar_clasificacion_ahora`: encola el paso ya (asincrono; el resultado sale en `listar_corridas_importacion`).
- `limpiar_marca`: pone la marca (y opcionalmente salidas) en NULL para reclasificar todo. **DESTRUCTIVA**.
- `rellenar_columna`: backfill por plantilla `{col}` (ej. `{numero_proceso}|{url}`) sobre filas existentes.

Todas consumen tokens de IA (salvo rellenar/limpiar): confirma con el usuario volumenes grandes.

Recetas: [[R10 - Clasificar datos con IA]]"""),
    ("08", "Reportes e indicadores", "reportes",
     "listar_fuentes_reporte ver_fuente_reporte listar_reportes ver_reporte crear_reporte listar_libros_indicadores "
     "ver_libro_indicadores crear_libro_indicadores agregar_indicador editar_reporte archivar_reporte eliminar_indicador "
     "eliminar_libro_indicadores",
     """Dos formas de visualizar datos:
1. **Galeria de reportes** (`crear_reporte`): un PANEL guardado (tabla, barras, torta, linea) que aparece en la
   pestana de su `categoria`.
2. **Libros de indicadores** (`crear_libro_indicadores` + `agregar_indicador`): un tablero tipo dashboard con hojas y
   widgets (kpi, barras, barras_horizontales, dona, embudo, treemap, linea, area, tabla) en una rejilla de 4 columnas.

Fuentes (`listar_fuentes_reporte`): nativas (`native:taskitem` actividades, `native:tercero` directorio,
`native:worklog` horas...), **cada tabla de contenedor** (`container:{tabla_id}`) y externas. Usa `ver_fuente_reporte`
para ver campos y que se puede agrupar/sumar.

Reglas:
- Ambos EJECUTAN la consulta antes de guardar: si un campo no existe o no es sumable, no guardan y explican.
- `sumar/promedio/minimo/maximo` exigen campo `numero`/`decimal`.
- Ordenar por una agregacion: `orden=[{campo:"conteo"|"suma"|"promedio"..., desc:true}]`.
- Los `filtros` quedan FIJOS en el reporte (no son interactivos). Para "solo activos" crea una version filtrada.
- `ver_reporte` re-ejecuta y da datos frescos (sirve para monitorear).
- **Por periodo**: `consulta.periodo` = dia | semana | mes | trimestre | anio agrupa un campo FECHA (claves
  2026-01-15 / 2026-W03 / 2026-01 / 2026-T1 / 2026, orden cronologico). Solo con `agrupar_por` de tipo fecha.
- `editar_reporte` cambia solo lo que envies (si cambia grafico o consulta, la ejecuta antes de guardar);
  `archivar_reporte` lo saca de la galeria (recuperable desde 'Archivados').

Recetas: [[R07 - Crear reportes e indicadores]]"""),
    ("09", "Formularios dinamicos", "formularios",
     "describe_components list_forms get_form verify_form apply_form_spec delete_container list_templates "
     "list_data_containers describe_data_container list_tercero_fields list_org_units list_activity_types create_form "
     "import_form export_form update_form_header add_container update_container move_container add_question "
     "update_question move_question delete_question set_transactional set_sequence_next set_custom_css set_theme "
     "set_status_ladder wire_submit_task_rule activate deactivate archive create_template update_template "
     "set_default_template wire_print_button wire_convert_button create_share_link create_record get_render_urls "
     "listar_verbos_regla listar_reglas ver_regla historial_regla crear_regla parametros_regla estado_regla "
     "renombrar_regla eliminar_regla reglas_de_formulario regla_al_enviar quitar_regla_al_enviar regla_en_campo "
     "quitar_regla_de_campo parametros_regla_en_campo condicion_entre_campos ocultar_barra_envio formulario_reportable "
     "codigo_automatico_formulario registro_editable cierre_automatico_formulario guardar_version_formulario "
     "listar_versiones_formulario restaurar_version_formulario listar_enlaces_formulario revocar_enlace_formulario "
     "campo_bloqueado campo_oculto valor_por_defecto_dinamico permisos_campo_por_rol subformulario_de_campo "
     "diseno_campo renombrar_campo",
     """Autoria de formularios (DynamicFormRenderer). Estos nombres van en INGLES (es otra familia de herramientas).

Orden de trabajo recomendado:
1. `describe_components` SIEMPRE primero: devuelve tipos de control, fuentes de listas, sintaxis de calculos,
   tokens de prellenado, columnas de grilla, etc. Es la "documentacion viva" de los formularios.
2. `create_form` (cabecera, `code` unico) -> `apply_form_spec` (secciones + campos en UNA llamada, atomica e
   idempotente) -> `verify_form` hasta `errors=0` -> `activate`.
3. Opcional: `set_theme` (marca), `wire_submit_task_rule` (crear tarea al enviar), `wire_print_button`
   (plantilla imprimible), `create_share_link` (enlace publico /f/{token} - confirma antes).

Recetas: [[R04 - Crear un formulario completo]]"""),
]

assigned = set()
for _, _, _, names, _ in MODULES:
    for n in names.split():
        assigned.add(n)
missing = [t["name"] for t in tools if t["name"] not in assigned]
extra = [n for n in assigned if n not in by_name]
print("tools vivos:", len(tools), "asignados:", len(assigned), "sin modulo:", missing, "inexistentes:", extra)

def kind(t):
    a = t.get("annotations", {}) or {}
    if a.get("destructiveHint"):
        return "DESTRUCTIVA (confirma con el usuario)"
    if a.get("readOnlyHint"):
        return "Lectura"
    return "Escritura"

def type_str(p):
    if not isinstance(p, dict):
        return "?"
    t = p.get("type", "")
    if isinstance(t, list):
        t = "|".join(t)
    if "enum" in p:
        return t + " (" + " / ".join(str(e) for e in p["enum"]) + ")"
    if t == "array":
        it = p.get("items", {}) or {}
        if it.get("type") == "object":
            return "array de objetos"
        return "array de " + str(it.get("type", "?"))
    rng = []
    if "minimum" in p:
        rng.append(">=" + str(p["minimum"]))
    if "maximum" in p:
        rng.append("<=" + str(p["maximum"]))
    return t + (" " + " ".join(rng) if rng else "")

def cell(s):
    return ascii_(str(s)).replace("|", "/").replace("\n", " ")

def example_value(name, p):
    t = p.get("type")
    if "enum" in p:
        return p["enum"][0]
    if t == "integer":
        return p.get("minimum", 1)
    if t == "number":
        return 1
    if t == "boolean":
        return True
    if t == "array":
        it = p.get("items", {}) or {}
        if it.get("type") == "object":
            sub = {}
            for k in (it.get("required") or list((it.get("properties") or {}).keys())[:2]):
                sp = (it.get("properties") or {}).get(k, {})
                sub[k] = example_value(k, sp)
            return [sub]
        return ["<" + name + ">"]
    if t == "object":
        return {}
    return "<" + name + ">"

def _rows(props, req, prefix, lines, depth):
    for k, p in props.items():
        full = prefix + k
        lines.append("| `%s` | %s | %s | %s |" % (full, cell(type_str(p)), "SI" if k in req else "", cell(p.get("description", ""))))
        if depth >= 3 or not isinstance(p, dict):
            continue
        sub, suffix = None, ""
        if p.get("type") == "object" and p.get("properties"):
            sub, suffix = p, "."
        elif p.get("type") == "array" and (p.get("items") or {}).get("type") == "object":
            sub, suffix = p["items"], "[]."
        if sub:
            _rows(sub.get("properties") or {}, set(sub.get("required", []) or []), full + suffix, lines, depth + 1)

def params_block(schema):
    props = (schema or {}).get("properties", {}) or {}
    req = set((schema or {}).get("required", []) or [])
    if not props:
        return "_Sin parametros._\n"
    lines = ["| Parametro | Tipo | Oblig. | Detalle |", "|---|---|---|---|"]
    _rows(props, req, "", lines, 1)
    return "\n".join(lines) + "\n"

# Ejemplos REALES (probados en produccion) para las herramientas clave. Valores genericos, sin ids reales.
CURATED = {
    "crear_tarea": {"tablero": "Comercial", "concepto": "Cotizacion", "titulo": "Cotizar 20 sillas ergonomicas",
                    "descripcion": "Cliente pide entrega en 15 dias", "prioridad": "alta", "vence": "2026-12-15",
                    "asignado": "ana@miempresa.com"},
    "completar_paso": {"numero": "T00123", "opcion": "Aprobado", "comentario": "Revisado con el cliente"},
    "crear_contacto": {"nombre": "Industrias Ejemplo SAS", "tipo": "empresa", "identificacion": "900123456",
                       "tipo_identificacion": "nit", "ciudad": "Bogota", "email": "compras@ejemplo.com"},
    "agregar_nodo": {"flujo": "<definicion_id>", "tipo": "tarea", "nombre": "Revisar solicitud",
                     "desde_nodo_id": "<id_inicio>", "hasta_nodo_id": "<id_fin>"},
    "crear_dron": {"nombre": "Convocatorias portal X", "url_inicio": "https://www.ejemplo.gov.co/contratacion/",
                   "contenedor": "Convocatorias", "descripcion": "Baja convocatorias publicadas"},
    "agregar_paso": {"dron": "Convocatorias portal X", "tipo": "extraer", "nombre": "Extraer filas",
                     "script": "var rows=[]; document.querySelectorAll('table tr').forEach(function(tr){var td=tr.querySelectorAll('td'); if(td.length<2)return; rows.push({titulo:td[0].innerText.trim(), url:(tr.querySelector('a')||{}).href||''});}); return rows;",
                     "contenedor_destino": "Convocatorias",
                     "mapeo_json": "{\"titulo\":\"titulo\",\"url\":\"url\"}", "ingesta_llave": "url"},
    "guardar_programacion": {"dron": "Convocatorias portal X", "nombre": "Diaria 7am", "tipo": "cron",
                             "cron": "0 7 * * *", "activa": True},
    "crear_contenedor_datos": {"nombre": "Ventas", "descripcion": "Pedidos y clientes importados del ERP"},
    "agregar_tabla": {"contenedor": "Ventas", "nombre": "Pedidos",
                      "columnas": [{"nombre": "id_pedido", "tipo": "texto", "requerido": True},
                                   {"nombre": "cliente", "tipo": "texto"},
                                   {"nombre": "fecha", "tipo": "fecha"},
                                   {"nombre": "valor", "tipo": "decimal"}]},
    "agregar_relacion": {"contenedor": "Ventas", "origen": "Pedidos", "destino": "Clientes", "cardinalidad": "n1",
                         "nombre": "cliente del pedido"},
    "guardar_conector": {"contenedor": "Ventas", "nombre": "API pedidos", "tipo": "api", "destino": "Pedidos",
                         "endpoint": "https://api.ejemplo.com/v1/pedidos", "metodo": "GET", "auth": "ninguna",
                         "mapeo_json": "{\"arrayPath\":\"data\",\"paging\":{\"mode\":\"Offset\",\"offsetParam\":\"offset\",\"limitParam\":\"limit\",\"pageSize\":500,\"maxPages\":20},\"fields\":[{\"column\":\"id_pedido\",\"path\":\"id\"},{\"column\":\"cliente\",\"path\":\"customer.name\"},{\"column\":\"valor\",\"path\":\"total\"}]}"},
    "ver_conector": {"contenedor": "Ventas", "conector": "API pedidos"},
    "guia_ecorex": {"buscar": "Tiempo de espera agotado"},
    "agregar_filas": {"contenedor": "Ventas", "tabla": "Pedidos", "columna_clave": "id_pedido",
                      "filas": [{"id_pedido": "P-001", "cliente": "Industrias Ejemplo", "fecha": "2026-01-10", "valor": 1250.5},
                                {"id_pedido": "P-002", "cliente": "Comercial Demo", "fecha": "2026-02-03", "valor": 980}]},
    "ver_filas": {"contenedor": "Ventas", "tabla": "Pedidos", "filtros": [{"columna": "cliente", "valor": "ejemplo"}], "limite": 10},
    "editar_reporte": {"reporte": "Ventas por mes", "consulta": {"agrupar_por": "fecha", "periodo": "trimestre",
                       "agregaciones": [{"funcion": "sumar", "campo": "valor"}]}},
    "guardar_programacion_importacion": {"contenedor": "Ventas", "nombre": "Pedidos diaria 5am", "tipo": "cron",
                                         "cron": "0 5 * * *", "modo": "upsert", "columna_clave": "id_pedido",
                                         "conector": "API pedidos", "activa": True},
    "publicar_contenedor": {"contenedor": "Ventas", "tabla": "Pedidos", "padre": "Comercial"},
    "crear_agente": {"nombre": "Clasificador de pedidos", "rol": "Clasificador", "proveedor": "deepseek",
                     "prompt": "Recibes filas JSON de pedidos. Para cada una devuelve SOLO un arreglo JSON [{\"id_pedido\":\"...\",\"categoria\":\"...\"}] con categoria en: hardware, software, servicios, otros."},
    "clasificar_lote": {"contenedor": "Ventas", "tabla": "Pedidos", "agente": "Clasificador de pedidos",
                        "columna_clave": "id_pedido", "columna_marca": "clasificado_en",
                        "columnas_salida": ["categoria"], "columnas_entrada": ["cliente", "descripcion"], "limite": 50},
    "configurar_paso_clasificacion": {"contenedor": "Ventas", "programacion": "Pedidos diaria 5am",
                                      "agente": "Clasificador de pedidos", "columna_clave": "id_pedido",
                                      "columna_marca": "clasificado_en", "columnas_salida": ["categoria"],
                                      "columnas_entrada": ["cliente", "descripcion"], "limite": 50, "max_lotes": 20},
    "rellenar_columna": {"contenedor": "Ventas", "tabla": "Pedidos", "columna": "clave_ingesta",
                         "plantilla": "{id_pedido}|{cliente}", "solo_vacias": True},
    "crear_reporte": {"nombre": "Ventas por categoria", "categoria": "Comercial", "grafico": "barras",
                      "contenedor": "Ventas", "tabla": "Pedidos",
                      "consulta": {"agrupar_por": "categoria",
                                   "agregaciones": [{"funcion": "sumar", "campo": "valor"}],
                                   "orden": [{"campo": "suma", "desc": True}]}},
    "agregar_indicador": {"libro": "Tablero comercial", "titulo": "Pedidos abiertos", "tipo": "kpi", "ancho": 1,
                          "contenedor": "Ventas", "tabla": "Pedidos",
                          "consulta": {"filtros": [{"campo": "estado", "operador": "en", "valores": ["Abierto", "Pendiente"]}],
                                       "agregaciones": [{"funcion": "contar"}]}},
    "create_form": {"code": "SOL-VISITA", "title": "Solicitud de visita tecnica"},
    "apply_form_spec": {"form_id": "<form_id>",
                        "containers": [{"key": "datos", "name": "Datos del solicitante", "container_type": "Section"}],
                        "fields": [{"container_key": "datos", "field_code": "nombre", "label": "Nombre", "control_type": "Text", "width": 6, "required": True},
                                   {"container_key": "datos", "field_code": "ciudad", "label": "Ciudad", "control_type": "Select", "width": 6,
                                    "options_json": "[{\"id\":\"BOG\",\"label\":\"Bogota\"},{\"id\":\"MED\",\"label\":\"Medellin\"}]"}]},
}

def curated_block(name):
    if name not in CURATED:
        return ""
    return ("Ejemplo real (valores genericos):\n\n```json\n" +
            json.dumps({"name": name, "arguments": CURATED[name]}, ensure_ascii=True, indent=2) + "\n```\n")

def example_block(name, schema):
    props = (schema or {}).get("properties", {}) or {}
    req = (schema or {}).get("required", []) or []
    args = {k: example_value(k, props.get(k, {})) for k in req}
    return "```json\n" + json.dumps({"name": name, "arguments": args}, ensure_ascii=True, indent=2) + "\n```\n"

index_rows = []
for num, title, slug, names, intro in MODULES:
    fname = "T%s - %s.md" % (num, title)
    # Las que aun no estan en el catalogo vivo (codigo sin desplegar) se omiten hasta el proximo deploy.
    pending = [n for n in names.split() if n not in by_name]
    if pending:
        print("aun no desplegadas (se omiten): %s" % pending)
    names_l = [n for n in names.split() if n in by_name]
    out = ["---", "tipo: referencia", "servidor: ecorex-tableros", "modulo: " + slug,
           "herramientas: %d" % len(names_l), "fuente: catalogo vivo tools/list (generado)", "---", "",
           "# MCP Tableros - %s" % title, "", "Servidor: `/api/mcp/tableros` - ver [[01 - Conectar los MCP]]. Volver a [[00 - LEEME PRIMERO]].", "",
           "## Para que sirve", "", intro, "", "## Herramientas (%d)" % len(names_l), ""]
    ro = [n for n in names_l if kind(by_name[n]) == "Lectura"]
    de = [n for n in names_l if kind(by_name[n]).startswith("DESTRUCTIVA")]
    wr = [n for n in names_l if n not in ro and n not in de]
    out.append("- **Lectura:** " + ", ".join("`%s`" % n for n in ro))
    out.append("- **Escritura:** " + ", ".join("`%s`" % n for n in wr))
    out.append("- **Destructivas:** " + (", ".join("`%s`" % n for n in de) if de else "_ninguna_"))
    out.append("")
    for n in names_l:
        t = by_name[n]
        out.append("### `%s`" % n)
        out.append("")
        out.append("**Tipo:** %s" % kind(t))
        out.append("")
        out.append("> " + nolinks(ascii_(t.get("description", "")).replace("\n", " ")))
        out.append("")
        out.append(params_block(t.get("inputSchema")))
        out.append("Llamada minima (solo obligatorios):")
        out.append("")
        out.append(example_block(n, t.get("inputSchema")))
        cb = curated_block(n)
        if cb:
            out.append(cb)
        index_rows.append((n, title, fname[:-3], kind(t)))
    write(os.path.join(REF, fname), "\n".join(out))
    print("escrito", fname, len(names_l))

# ------------------------------------------------------------------ specs en codigo (Maquetas / WhatsApp)
SPEC_RE = re.compile(r'new\(\s*"([a-z_]+)"\s*,\s*((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+)\s*,\s*"""(.*?)"""\s*\)', re.S)
STR_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')

CONST_RE = re.compile(r'private const string (\w+)\s*=\s*"((?:[^"\\]|\\.)*)";')
_CONSTS = {}

def load_consts(paths):
    for p in paths:
        for m in CONST_RE.finditer(open(p, encoding="utf-8").read()):
            _CONSTS[m.group(1)] = m.group(2).replace('\\"', '"').replace("\\\\", "\\")

def resolve_concat(raw):
    # fragmentos compartidos: """ + ParaProp + """  ->  valor de la constante
    return re.sub(r'"""\s*\+\s*(\w+)\s*\+\s*"""', lambda m: _CONSTS.get(m.group(1), m.group(0)), raw)

def parse_specs(path):
    src = open(path, encoding="utf-8").read()
    res = []
    for m in SPEC_RE.finditer(src):
        name = m.group(1)
        desc = "".join(s.encode("utf-8").decode("unicode_escape", errors="ignore") if "\\u" in s else s
                       for s in STR_RE.findall(m.group(2)))
        raw = resolve_concat(m.group(3))
        schema = json.loads(raw)   # sin try: si falla, que falle fuerte (nada de fichas "sin parametros" falsas)
        res.append((name, desc, schema))
    return res

def name_set(path, varname):
    src = open(path, encoding="utf-8").read()
    m = re.search(varname + r'\s*=\s*new\([^)]*\)\s*\{(.*?)\};', src, re.S)
    return set(re.findall(r'"([a-z_]+)"', m.group(1))) if m else set()

mcp_dir = os.path.join(REPO, "apps", "backend", "src", "Ecorex.Application", "Mcp")
load_consts([os.path.join(mcp_dir, f) for f in
             ["MaquetaMcpToolset.cs", "WhatsAppMcpToolset.cs", "WhatsAppMcpToolset.Envio.cs", "WhatsAppMcpToolset.Gestion.cs"]])
print("constantes de esquema:", sorted(_CONSTS))

def code_ref(title, fname, server, route, scope, where_token, intro, files, ro_names, de_names):
    specs = []
    for f in files:
        specs += parse_specs(os.path.join(mcp_dir, f))
    out = ["---", "tipo: referencia", "servidor: " + server, "herramientas: %d" % len(specs),
           "fuente: specs en codigo (generado)", "---", "",
           "# %s" % title, "",
           "- **Ruta:** `%s`" % route, "- **Token:** alcance `%s` (%s). Un token de tableros NO abre este servidor." % (scope, where_token),
           "- Volver a [[00 - LEEME PRIMERO]] - conexion en [[01 - Conectar los MCP]].", "",
           "## Para que sirve", "", intro, "", "## Herramientas (%d)" % len(specs), ""]
    for name, desc, schema in specs:
        k = "DESTRUCTIVA (confirma con el usuario)" if name in de_names else ("Lectura" if name in ro_names else "Escritura")
        out += ["### `%s`" % name, "", "**Tipo:** %s" % k, "", "> " + nolinks(ascii_(desc)), "", params_block(schema),
                "Llamada minima (solo obligatorios):", "", example_block(name, schema)]
        index_rows.append((name, title, fname[:-3], k))
    write(os.path.join(REF, fname), "\n".join(out))
    print("escrito", fname, len(specs))

maq = os.path.join(mcp_dir, "MaquetaMcpToolset.cs")
code_ref("MCP Maquetas", "M01 - MCP Maquetas.md", "ecorex-maquetas", "/api/mcp/maquetas", "integ:maqueta",
         "token personal de maquetas, se emite desde el editor de maquetas del sistema",
         """Para la IA del PC local (Claude Code abierto en la carpeta de una maqueta HTML): lee las **solicitudes de
ajuste** anotadas en el editor de maquetas del sistema (con captura y selector CSS del elemento), las comenta,
mueve y resuelve, y obtiene el comando para **publicar una version** nueva. Incluye el **cerebro digital** del
proyecto (notas markdown enlazadas tipo Obsidian).

Flujo: `listar_proyectos` -> `listar_solicitudes` -> `ver_solicitud` (trae la captura) -> editar archivos locales ->
`comentar_solicitud` -> (con OK del usuario) publicar con el comando de `como_publicar_version` ->
`resolver_solicitud`. La maqueta es HTML puro: no agregues scripts de ECOREX. Nunca escribas el token en archivos.""",
         ["MaquetaMcpToolset.cs"], name_set(maq, "ReadOnly"), name_set(maq, "Destructive"))

wa_files = ["WhatsAppMcpToolset.cs", "WhatsAppMcpToolset.Envio.cs", "WhatsAppMcpToolset.Gestion.cs"]
wa_ro, wa_de = set(), set()
for f in wa_files:
    p = os.path.join(mcp_dir, f)
    wa_ro |= name_set(p, "ReadOnly")
    wa_de |= name_set(p, "Destructive")
# las de lectura del archivo base viven en ReadSpecs
wa_ro |= {n for n, _, _ in parse_specs(os.path.join(mcp_dir, "WhatsAppMcpToolset.cs"))}
code_ref("MCP WhatsApp por linea", "W01 - MCP WhatsApp por linea.md", "ecorex-whatsapp",
         "/api/mcp/whatsapp/{lineId}", "mcp:whatsapp",
         "se configura desde la pantalla /lineas, una configuracion por linea",
         """Cada **linea de WhatsApp (Evolution API)** del cliente es un servidor MCP propio (en el cliente se nombra p.ej.
`whatsapp-ventas`, `whatsapp-soporte`). Lo que envias queda en Conversaciones a nombre del usuario "(via IA)" y el
agente SARA deja de responder ese chat (usa `pausar_agente` / `reanudar_agente` para controlarlo).

Reglas de seguridad (obligatorias):
- Empieza con `estado_linea`. Antes de responder un chat, lee sus mensajes (`leer_mensajes`).
- **Un destinatario por llamada. Nada de envios masivos.**
- Escribirle a un numero que NUNCA escribio a la linea puede hacer que WhatsApp bloquee el numero: confirma con el
  usuario y usa `primer_contacto_confirmado=true`.
- Confirma antes de borrar mensajes, bloquear, salir de grupos, crear grupos o publicar estados.
- Archivos: por URL https publica (o base64 pequeno). Los grupos no quedan en Conversaciones: leelos con
  `fuente='whatsapp'`.""",
         wa_files, wa_ro, wa_de)

# ------------------------------------------------------------------ indice A-Z
index_rows.sort(key=lambda r: r[0])
idx = ["---", "tipo: indice", "---", "", "# Indice de herramientas (A-Z)", "",
       "Todas las herramientas de los 3 servidores MCP, con su modulo. Volver a [[00 - LEEME PRIMERO]].", "",
       "| Herramienta | Modulo | Tipo |", "|---|---|---|"]
for n, title, note, k in index_rows:
    idx.append("| `%s` | [[%s]] | %s |" % (n, note, k.split(" ")[0]))
write(os.path.join(REF, "Indice de herramientas (A-Z).md"), "\n".join(idx) + "\n")
print("indice:", len(index_rows), "filas")
