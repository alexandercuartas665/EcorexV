# -*- coding: utf-8 -*-
"""Valida el vault MCP de ECOREX: ASCII, enlaces [[...]], datos privados y llamadas de ejemplo contra los esquemas
reales (catalogo vivo + specs en codigo). Uso: python validar_vault.py [--vault carpeta]  (despues de
bajar_catalogo.ps1). --vault = carpeta bajo docs/ (defecto vault-mcp-ecorex; ej. vault-mcp-flujos)."""
import json, os, re, glob, sys
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
CATALOGO = os.path.join(HERE, "catalogo.local", "tools_list.json")
PATRONES_LOCALES = os.path.join(HERE, "patrones-privados.local")   # una regex por linea; ignorado por git
VAULT_NAME = sys.argv[sys.argv.index("--vault") + 1] if "--vault" in sys.argv else "vault-mcp-ecorex"
VAULT = os.path.join(REPO, "docs", VAULT_NAME)
files = sorted(glob.glob(os.path.join(VAULT, "**", "*.md"), recursive=True))
print("archivos:", len(files))

# ---- esquemas conocidos
schemas = {t["name"]: t["inputSchema"] for t in json.load(open(CATALOGO, encoding="utf-8"))["result"]["tools"]}
SPEC_RE = re.compile(r'new\(\s*"([a-z_]+)"\s*,\s*((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+)\s*,\s*"""(.*?)"""\s*\)', re.S)
mcp_dir = os.path.join(REPO, "apps", "backend", "src", "Ecorex.Application", "Mcp")
CONST_RE = re.compile(r'private const string (\w+)\s*=\s*"((?:[^"\\]|\\.)*)";')
consts = {}
cs_files = ["MaquetaMcpToolset.cs", "WhatsAppMcpToolset.cs", "WhatsAppMcpToolset.Envio.cs", "WhatsAppMcpToolset.Gestion.cs"]
for f in cs_files:
    for m in CONST_RE.finditer(open(os.path.join(mcp_dir, f), encoding="utf-8").read()):
        consts[m.group(1)] = m.group(2).replace('\\"', '"').replace("\\\\", "\\")
for f in cs_files:
    for m in SPEC_RE.finditer(open(os.path.join(mcp_dir, f), encoding="utf-8").read()):
        raw = re.sub(r'"""\s*\+\s*(\w+)\s*\+\s*"""', lambda x: consts.get(x.group(1), x.group(0)), m.group(3))
        schemas.setdefault(m.group(1), json.loads(raw))
# Herramientas del MCP de tableros AUN NO DESPLEGADAS (no estan en el catalogo vivo): se reconocen por su nombre
# en el codigo; si su esquema es un raw string se valida, si se arma por concatenacion solo se valida que existan.
SIN_ESQUEMA = set()
import importlib.util
_spec = importlib.util.spec_from_file_location("genref_codigo", os.path.join(HERE, "gen_referencia_codigo.py"))
_mod = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_mod)
for _n, (_d, _sc) in _mod.all_specs().items():
    schemas[_n] = _sc   # el codigo (rama) manda sobre el catalogo vivo: puede traer parametros nuevos
# guia_ecorex (nombre por constante): su esquema se toma del codigo (puede traer 'tema', ADR-0211).
_guia = open(os.path.join(mcp_dir, "TableroMcpToolset.Guia.cs"), encoding="utf-8").read()
_gm = re.search(r'GuideSpec = new\(GuideToolName,.*?"{3}(\{.*?\})"{3}\);', _guia, re.S)
if _gm:
    schemas["guia_ecorex"] = json.loads(_gm.group(1))
print("herramientas conocidas:", len(schemas), "+ sin esquema en codigo:", len(SIN_ESQUEMA))

problems = []
# ---- 1. ASCII
for f in files:
    raw = open(f, "rb").read()
    bad = [i for i, b in enumerate(raw) if b > 127]
    if bad:
        problems.append("NO-ASCII %s (%d bytes, primero en %d)" % (os.path.relpath(f, VAULT), len(bad), bad[0]))

# ---- 2. enlaces
names = {os.path.splitext(os.path.basename(f))[0] for f in files}
link_re = re.compile(r"\[\[([^\]|#]+)(?:[#|][^\]]*)?\]\]")
for f in files:
    txt = open(f, encoding="ascii", errors="replace").read()
    # ignorar enlaces dentro de bloques de codigo
    txt_nc = re.sub(r"```.*?```", "", txt, flags=re.S)
    txt_nc = re.sub(r"`[^`]*`", "", txt_nc)
    for m in link_re.finditer(txt_nc):
        target = m.group(1).strip()
        if target not in names:
            problems.append("ENLACE ROTO [[%s]] en %s" % (target, os.path.relpath(f, VAULT)))

# ---- 3. privacidad
# Genericos (token personal de ECOREX); los propios de cada instalacion (ids de empresa, nombres de clientes,
# dominios, correos) van en patrones-privados.local para NO publicarlos en este repo.
priv_patterns = [r"ecx_[A-Za-z0-9_\-]{10,}"]
if os.path.exists(PATRONES_LOCALES):
    for line in open(PATRONES_LOCALES, encoding="utf-8"):
        line = line.strip()
        if line and not line.startswith("#"):
            priv_patterns.append(line)
else:
    print("AVISO: falta %s; solo se revisan patrones genericos" % os.path.basename(PATRONES_LOCALES))
for f in files:
    txt = open(f, encoding="ascii", errors="replace").read()
    for p in priv_patterns:
        for m in re.finditer(p, txt):
            problems.append("PRIVADO '%s' en %s" % (m.group(0), os.path.relpath(f, VAULT)))

# ---- 4. llamadas de ejemplo vs esquema (solo notas escritas a mano + ejemplos curados de referencia)
dec = json.JSONDecoder()
calls = 0
for f in files:
    txt = open(f, encoding="ascii", errors="replace").read()
    for blk in re.findall(r"```json\n(.*?)```", txt, flags=re.S):
        i = 0
        while i < len(blk):
            while i < len(blk) and blk[i] in " \r\n\t":
                i += 1
            if i >= len(blk):
                break
            try:
                obj, j = dec.raw_decode(blk, i)
            except Exception as e:
                problems.append("JSON INVALIDO en %s cerca de: %s" % (os.path.relpath(f, VAULT), blk[i:i+70].replace("\n", " ")))
                break
            i = j
            if not (isinstance(obj, dict) and "name" in obj and "arguments" in obj):
                continue
            calls += 1
            n, a = obj["name"], obj["arguments"]
            sc = schemas.get(n)
            if sc is None and n in SIN_ESQUEMA:
                continue
            if sc is None:
                problems.append("TOOL INEXISTENTE '%s' en %s" % (n, os.path.relpath(f, VAULT)))
                continue
            props = sc.get("properties", {}) or {}
            for k in a:
                if k not in props:
                    problems.append("PARAM '%s' no existe en %s (%s)" % (k, n, os.path.relpath(f, VAULT)))
                else:
                    en = props[k].get("enum")
                    if en and isinstance(a[k], str) and a[k] not in en:
                        problems.append("VALOR '%s' fuera de enum %s en %s.%s (%s)" % (a[k], en, n, k, os.path.relpath(f, VAULT)))
            for r in sc.get("required", []) or []:
                if r not in a:
                    problems.append("FALTA obligatorio '%s' en %s (%s)" % (r, n, os.path.relpath(f, VAULT)))
            # anidados de un nivel (consulta, columnas, containers, fields)
            for k, v in a.items():
                p = props.get(k, {})
                sub = None
                if p.get("type") == "object" and p.get("properties") and isinstance(v, dict):
                    sub = (p, [v])
                elif p.get("type") == "array" and (p.get("items") or {}).get("type") == "object" and isinstance(v, list):
                    sub = (p["items"], v)
                if sub and not (sub[0].get("properties") or {}):
                    sub = None   # objeto libre (p.ej. filas columna->valor): no se validan sus llaves
                if sub:
                    sp, items = sub
                    for it in items:
                        if not isinstance(it, dict):
                            continue
                        for sk in it:
                            if sk not in (sp.get("properties") or {}) and sp.get("additionalProperties") is not True:
                                problems.append("PARAM '%s.%s' no existe en %s (%s)" % (k, sk, n, os.path.relpath(f, VAULT)))
print("llamadas de ejemplo validadas:", calls)

# ---- 5. menciones de herramientas en backticks que parecen tool pero no existen
tool_like = re.compile(r"`([a-z]+(?:_[a-z]+)+)`")
known_params = set()
for sc in schemas.values():
    def walk(s):
        for k, v in (s.get("properties") or {}).items():
            known_params.add(k)
            if isinstance(v, dict):
                if v.get("type") == "object":
                    walk(v)
                if v.get("type") == "array" and isinstance(v.get("items"), dict):
                    walk(v["items"])
    walk(sc)
extra_ok = {"agregar_filas_a_contenedor", "actualizar_filas_por_clave", "clasificado_en", "id_pedido", "nit_cliente",
            "id_proceso", "clave_ingesta", "fecha_publicacion", "url_proceso", "nombre_proceso", "tipo_contrato",
            "categoria_unspsc", "precio_base", "razon_social", "numero_proceso", "fecha_visita", "ciudad_otra",
            "id_del_proceso", "codigo_principal_de_categoria", "fecha_de_publicacion_del", "nombre_del_procedimiento",
            "descripci_n_del_procedimiento", "modalidad_de_contratacion", "tipo_de_contrato", "estado_del_procedimiento",
            "tamano_empresa", "x_old", "grid_column_schema", "ver_conector", "agente_desconectado", "sin_respuesta",
            "sin_asignar", "hereda_inicio", "hereda_anterior", "campo_formulario", "barras_horizontales", "mayor_igual",
            "menor_igual", "solo_vacias", "primer_contacto_confirmado", "mcp_servers", "pedir_al_correr", "send_as_image",
            "fixed_title", "title_key", "table_field_code", "title_prefix", "status_ladder", "ver_conector",
            # campos de RESPUESTA de las herramientas de flujos (ver_flujo, listar_flujos, crear_flujo)
            "definicion_id", "instancias_en_marcha", "obligatorio_en_salidas", "concepto_cierre", "decision_humana",
            "encargado_origen", "reinicia_en", "tablero_destino", "columna_destino", "salta_a_flujo", "tiempo_arranque",
            "avisos_al_llegar", "crear_al_llegar", "columna_de_cierre", "contacto_tarea", "encargado_paso",
            "whatsapp_grupo", "nodo_id", "arista_id",
            # ids de respuesta y tipos de paso de dron citados en las recetas generales
            "tabla_id", "tiene_script", "contacto_id", "reporte_id", "libro_id", "hoja_id", "widget_id", "dron_id",
            "inyectar_script", "procesar_archivos", "cerrar_navegador", "crear_actividad", "fecha_de_ultima_publicaci",
            # cerebro de formularios: campos de ejemplo y de respuesta
            "tarea_creada", "codigo_interno", "firma_cliente", "btn_aprobar", "btn_copiar", "nit_factura", "nombre_factura",
            "valor_aprobado", "documento_activo", "regla_id", "version_id", "enlace_id",
            # tipos de parametro de verbo que devuelve listar_verbos_regla
            "codigo_de_campo", "si_no", "lista_de_codigos_de_campo", "nodo_de_flujo",
            # agente del paso (ola 3): campo de ver_flujo y valor de accion de fallo
            "agente_ia", "devolver_a_persona"}
seen = set()
for f in files:
    if os.path.basename(os.path.dirname(f)) == "Referencia":
        continue
    txt = open(f, encoding="ascii", errors="replace").read()
    for m in tool_like.finditer(txt):
        w = m.group(1)
        if w in schemas or w in known_params or w in extra_ok:
            continue
        key = (w, os.path.basename(f))
        if key not in seen:
            seen.add(key)
            problems.append("MENCION DUDOSA `%s` en %s" % (w, os.path.relpath(f, VAULT)))

print()
print("PROBLEMAS:", len(problems))
for p in problems:
    print(" -", p)
