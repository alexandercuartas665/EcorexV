# -*- coding: utf-8 -*-
"""Genera la nota de referencia del cerebro de FLUJOS (docs/vault-mcp-flujos/Referencia/F11 - Referencia de
herramientas de flujos.md) leyendo los specs DIRECTAMENTE del codigo (TableroMcpToolset.Flujos.cs y .FlujosConfig.cs),
asi la referencia no se desalinea del servidor aunque aun no este desplegado. Uso: python gen_referencia_flujos.py"""
import json, os, re

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
MCP = os.path.join(REPO, "apps", "backend", "src", "Ecorex.Application", "Mcp")
FILES = ["TableroMcpToolset.Flujos.cs", "TableroMcpToolset.FlujosConfig.cs"]
OUT = os.path.join(REPO, "docs", "vault-mcp-flujos", "Referencia", "F11 - Referencia de herramientas de flujos.md")

READ_ONLY = {"listar_flujos", "ver_flujo", "listar_reglas_disponibles"}
DESTRUCTIVE = {"eliminar_nodo", "quitar_notificacion_nodo", "quitar_formulario_nodo", "quitar_regla_nodo",
               "desenganchar_flujo_de_concepto"}

GROUPS = [
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
    ("Publicar y ciclo de vida", ["publicar_flujo", "enganchar_flujo_a_concepto", "desenganchar_flujo_de_concepto",
                                  "editar_flujo", "pausar_flujo", "reanudar_flujo"]),
]


def read_string(text, i):
    """Lee un literal C# ("..." con escapes o \"\"\"...\"\"\" raw) desde text[i]; devuelve (valor, fin)."""
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


def read_expr(text, i, consts):
    """Lee una expresion 'lit + IDENT + lit ...' hasta la coma o el parentesis de cierre del new(...)."""
    parts = []
    while True:
        while text[i] in " \t\r\n+":
            i += 1
        if text[i] == '"':
            s, i = read_string(text, i)
            parts.append(s)
        elif re.match(r"[A-Za-z_]", text[i]):
            m = re.match(r"[A-Za-z_]\w*", text[i:])
            parts.append(consts.get(m.group(0), ""))
            i += len(m.group(0))
        else:
            return "".join(parts), i
        j = i
        while text[j] in " \t\r\n":
            j += 1
        if text[j] != "+":
            return "".join(parts), j
        i = j


def load_specs(mcp_dir=MCP, files=FILES):
    """{nombre: (descripcion, esquema)} de los specs de flujos leidos del codigo (tambien los armados por concatenacion)."""
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


if __name__ != "__main__":
    raise SystemExit  # importado: solo load_specs (ver abajo)
specs = load_specs()

listed = [n for _, names in GROUPS for n in names]
missing = sorted(set(specs) - set(listed))
assert not missing, "herramientas sin grupo: %s" % missing


def ptype(p):
    t = p.get("type", "")
    if t == "array":
        it = p.get("items", {})
        t = "lista de " + ("objetos" if it.get("type") == "object" else it.get("type", "valores"))
    if p.get("enum"):
        t += " (" + " / ".join(p["enum"]) + ")"
    return t


lines = ["---", "tipo: referencia", "tema: flujos", "generado: gen_referencia_flujos.py (no editar a mano)", "---", "",
         "# F11 - Referencia de herramientas de flujos", "",
         "Volver a [[00 - LEEME PRIMERO]]. Generada del codigo del servidor. %d herramientas del MCP de tableros." % len(specs),
         "", "Marcas: (lectura) no cambia nada; (confirmar) quita o desvincula algo: confirma con el usuario.", ""]
for title, names in GROUPS:
    lines += ["## " + title, ""]
    for n in names:
        desc, schema = specs[n]
        mark = " (lectura)" if n in READ_ONLY else (" (confirmar)" if n in DESTRUCTIVE else "")
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
os.makedirs(os.path.dirname(OUT), exist_ok=True)
open(OUT, "w", encoding="utf-8", newline="\n").write(text)
print("herramientas:", len(specs), "->", OUT)
