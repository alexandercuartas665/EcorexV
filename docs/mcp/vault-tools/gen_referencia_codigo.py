# -*- coding: utf-8 -*-
"""Genera la nota de REFERENCIA de un cerebro de tema leyendo los specs DIRECTAMENTE del codigo del MCP de tableros,
asi la referencia no se desalinea del servidor aunque aun no este desplegado (ADR-0211).

Uso: python gen_referencia_codigo.py --tema flujos|formularios   (sin --tema genera todos)

Entiende los esquemas escritos como raw string, como concatenacion de literales y constantes, y con el helper
Obj(props, "req1", ...) de TableroMcpToolset.FormulariosConfig.cs."""
import json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
MCP = os.path.join(REPO, "apps", "backend", "src", "Ecorex.Application", "Mcp")

TEMAS = {
    "flujos": {
        "files": ["TableroMcpToolset.Flujos.cs", "TableroMcpToolset.FlujosConfig.cs", "TableroMcpToolset.FlujosAgente.cs"],
        "out": os.path.join("vault-mcp-flujos", "Referencia", "F11 - Referencia de herramientas de flujos.md"),
        "title": "F11 - Referencia de herramientas de flujos",
        "read_only": {"listar_flujos", "ver_flujo", "listar_reglas_disponibles", "opciones_agente_nodo", "ver_agente_nodo"},
        "destructive": {"eliminar_nodo", "quitar_notificacion_nodo", "quitar_formulario_nodo", "quitar_regla_nodo",
                        "desenganchar_flujo_de_concepto", "quitar_agente_nodo"},
        "groups": [
            ("Descubrir", ["listar_flujos", "ver_flujo", "listar_reglas_disponibles"]),
            ("Armar el grafo (solo borrador)", ["crear_flujo", "importar_bpmn", "nueva_version_flujo", "agregar_nodo",
                                                "renombrar_nodo", "conectar_nodos", "nombre_salida", "condicion_ruta",
                                                "eliminar_conexion", "eliminar_nodo"]),
            ("Decision, encargado y ciclo", ["decision_humana_nodo", "asignar_encargado_nodo", "asignar_cargo_nodo",
                                             "reinicio_nodo"]),
            ("Formularios del nodo", ["poner_formulario_a_nodo", "formulario_obligatorio_por_salida",
                                      "formulario_crear_al_llegar", "quitar_formulario_nodo"]),
            ("Avisos", ["agregar_notificacion_nodo", "quitar_notificacion_nodo"]),
            ("Tiempos, tablero y cierre", ["plazo_nodo", "tiempo_arranque_nodo", "tablero_destino_nodo",
                                           "concepto_cierre_nodo", "color_nodo", "nota_nodo"]),
            ("Reglas y encadenamiento", ["agregar_regla_nodo", "regla_autonoma_nodo", "quitar_regla_nodo",
                                         "salto_a_flujo_nodo"]),
            ("Publicar y ciclo de vida", ["publicar_flujo", "enganchar_flujo_a_concepto",
                                          "desenganchar_flujo_de_concepto", "editar_flujo", "pausar_flujo",
                                          "reanudar_flujo"]),
            ("Agente de IA del paso", ["opciones_agente_nodo", "ver_agente_nodo", "agente_nodo", "quitar_agente_nodo",
                                       "instrucciones_agente_nodo", "correo_agente_nodo", "web_agente_nodo",
                                       "whatsapp_agente_nodo", "voz_agente_nodo", "canales_agente_nodo",
                                       "fallo_agente_nodo", "seguimiento_agente_nodo", "tope_preguntas_agente_nodo",
                                       "traspaso_salida_agente_nodo"]),
        ],
    },
    "formularios": {
        "files": ["TableroMcpToolset.FormulariosConfig.cs"],
        "out": os.path.join("vault-mcp-formularios", "Referencia", "FO11 - Referencia de herramientas de reglas y opciones.md"),
        "title": "FO11 - Referencia de herramientas de reglas y opciones",
        "read_only": {"listar_verbos_regla", "listar_reglas", "ver_regla", "reglas_de_formulario", "historial_regla",
                      "listar_versiones_formulario", "listar_enlaces_formulario"},
        "destructive": {"eliminar_regla", "quitar_regla_al_enviar", "quitar_regla_de_campo",
                        "restaurar_version_formulario", "revocar_enlace_formulario"},
        "groups": [
            ("Descubrir reglas", ["listar_verbos_regla", "listar_reglas", "ver_regla", "reglas_de_formulario",
                                  "historial_regla"]),
            ("Crear y mantener reglas (modulo Reglas)", ["crear_regla", "parametros_regla", "estado_regla",
                                                         "renombrar_regla", "eliminar_regla"]),
            ("Reglas en un formulario", ["regla_al_enviar", "quitar_regla_al_enviar", "regla_en_campo",
                                         "quitar_regla_de_campo", "parametros_regla_en_campo",
                                         "condicion_entre_campos"]),
            ("Opciones del formulario", ["ocultar_barra_envio", "formulario_reportable", "codigo_automatico_formulario",
                                         "registro_editable", "cierre_automatico_formulario"]),
            ("Versiones y enlaces", ["guardar_version_formulario", "listar_versiones_formulario",
                                     "restaurar_version_formulario", "listar_enlaces_formulario",
                                     "revocar_enlace_formulario"]),
            ("Propiedades de campo", ["campo_bloqueado", "campo_oculto", "valor_por_defecto_dinamico",
                                      "permisos_campo_por_rol", "subformulario_de_campo", "diseno_campo",
                                      "renombrar_campo"]),
        ],
    },
}


def read_string(text, i):
    """Lee un literal C# ("..." con escapes o raw de tres comillas) desde text[i]; devuelve (valor, fin)."""
    if text.startswith('"""', i):
        end = text.index('"""', i + 3)
        return text[i + 3:end], end + 3
    assert text[i] == '"'
    j, out = i + 1, []
    while text[j] != '"':
        if text[j] == "\\":
            nxt = text[j + 1]
            out.append({"n": "\n", "t": "\t", '"': '"', "\\": "\\"}.get(nxt, nxt))
            j += 2
        else:
            out.append(text[j])
            j += 1
    return "".join(out), j + 1


def skip_ws(text, i):
    while text[i] in " \t\r\n":
        i += 1
    return i


def obj_schema(args):
    """Emula el helper C# Obj(props, required...)."""
    props, req = args[0], args[1:]
    out = '{"type":"object","properties":{' + props + "}"
    if req:
        out += ',"required":[' + ",".join('"' + r + '"' for r in req) + "]"
    return out + ',"additionalProperties":false}'


def read_expr(text, i, consts):
    """Lee 'lit + IDENT + Obj(...) ...' hasta la coma o el parentesis que cierra la llamada."""
    parts = []
    while True:
        i = skip_ws(text, i)
        while text[i] == "+":
            i = skip_ws(text, i + 1)
        if text[i] == '"':
            s, i = read_string(text, i)
            parts.append(s)
        elif re.match(r"[A-Za-z_]", text[i]):
            m = re.match(r"[A-Za-z_]\w*", text[i:])
            ident = m.group(0)
            i += len(ident)
            k = skip_ws(text, i)
            if text[k] == "(":
                args, i = [], k + 1
                while True:
                    val, i = read_expr(text, i, consts)
                    args.append(val)
                    i = skip_ws(text, i)
                    if text[i] == ",":
                        i += 1
                        continue
                    assert text[i] == ")", text[i:i + 40]
                    i += 1
                    break
                parts.append(obj_schema(args) if ident == "Obj" else "")
            else:
                parts.append(consts.get(ident, ""))
        else:
            return "".join(parts), i
        j = skip_ws(text, i)
        if text[j] != "+":
            return "".join(parts), j
        i = j


def load_specs(files, mcp_dir=MCP):
    """{nombre: (descripcion, esquema)} leidos del codigo."""
    specs, consts = {}, {}
    for f in files:
        text = open(os.path.join(mcp_dir, f), encoding="utf-8").read()
        for m in re.finditer(r"private const string (\w+)\s*=", text):
            val, _ = read_expr(text, m.end(), consts)
            consts[m.group(1)] = val
    for f in files:
        text = open(os.path.join(mcp_dir, f), encoding="utf-8").read()
        for m in re.finditer(r'new\(\s*"([a-z_]+)"\s*,', text):
            name = m.group(1)
            desc, i = read_expr(text, m.end(), consts)
            assert text[i] == ",", (name, text[i:i + 40])
            schema_txt, i = read_expr(text, i + 1, consts)
            specs[name] = (desc, json.loads(schema_txt))
    return specs


def all_specs():
    out = {}
    for cfg in TEMAS.values():
        out.update(load_specs(cfg["files"]))
    return out


def ptype(p):
    t = p.get("type", "")
    if t == "array":
        it = p.get("items", {})
        t = "lista de " + ("objetos" if it.get("type") == "object" else it.get("type", "valores"))
        if it.get("enum"):
            t += " (" + " / ".join(it["enum"]) + ")"
    if p.get("enum"):
        t += " (" + " / ".join(p["enum"]) + ")"
    return t


def generate(tema):
    cfg = TEMAS[tema]
    specs = load_specs(cfg["files"])
    listed = [n for _, names in cfg["groups"] for n in names]
    missing = sorted(set(specs) - set(listed))
    assert not missing, "herramientas sin grupo: %s" % missing
    lines = ["---", "tipo: referencia", "tema: " + tema, "generado: gen_referencia_codigo.py (no editar a mano)", "---", "",
             "# " + cfg["title"], "",
             "Volver a [[00 - LEEME PRIMERO]]. Generada del codigo del servidor. %d herramientas del MCP de tableros." % len(specs),
             "", "Marcas: (lectura) no cambia nada; (confirmar) quita, borra o reemplaza algo: confirma con el usuario.", ""]
    for title, names in cfg["groups"]:
        lines += ["## " + title, ""]
        for n in names:
            desc, schema = specs[n]
            mark = " (lectura)" if n in cfg["read_only"] else (" (confirmar)" if n in cfg["destructive"] else "")
            lines += ["### `%s`%s" % (n, mark), "", desc, ""]
            props = schema.get("properties") or {}
            req = set(schema.get("required") or [])
            if props:
                lines += ["| Parametro | Tipo | Obligatorio | Nota |", "|---|---|---|---|"]
                for k, p in props.items():
                    lines.append("| `%s` | %s | %s | %s |" % (k, ptype(p), "si" if k in req else "", (p.get("description") or "").replace("|", "/")))
                    if p.get("type") == "array" and (p.get("items") or {}).get("properties"):
                        sub = p["items"]
                        sreq = set(sub.get("required") or [])
                        for sk, sp in sub["properties"].items():
                            lines.append("| `%s[].%s` | %s | %s | %s |" % (k, sk, ptype(sp), "si" if sk in sreq else "", (sp.get("description") or "").replace("|", "/")))
                lines.append("")
            else:
                lines += ["Sin parametros.", ""]
    text = "\n".join(lines) + "\n"
    bad = [c for c in text if ord(c) > 127]
    assert not bad, "no-ASCII en la referencia: %r" % "".join(sorted(set(bad)))
    out = os.path.join(REPO, "docs", cfg["out"])
    os.makedirs(os.path.dirname(out), exist_ok=True)
    open(out, "w", encoding="utf-8", newline="\n").write(text)
    print(tema, "- herramientas:", len(specs), "->", out)


if __name__ == "__main__":
    temas = [sys.argv[sys.argv.index("--tema") + 1]] if "--tema" in sys.argv else list(TEMAS)
    for t in temas:
        generate(t)
