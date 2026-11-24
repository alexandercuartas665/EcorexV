# -*- coding: utf-8 -*-
"""Empaqueta docs/vault-mcp-ecorex/ en un ZIP para importarlo en el Super Admin (/cerebros-plantilla ->
"Importar vault"). El importador titula cada nota con el NOMBRE del archivo e ignora las carpetas, asi que dos notas
no pueden llamarse igual aunque esten en carpetas distintas: este script lo verifica antes de empaquetar.
Uso: python empaquetar_vault.py [ruta_zip]   (por defecto salida.local/guia-ecorex-vault.zip, ignorado por git)."""
import os, sys, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
VAULT = os.path.join(REPO, "docs", "vault-mcp-ecorex")
ENTRADA = "00 - LEEME PRIMERO"      # guia_ecorex reconoce la nota de entrada por este titulo: no renombrarla
RESERVADOS = set("[]|#^/\\")
MAX_CHARS = 200000                  # tope por nota del importador

dest = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "salida.local", "guia-ecorex-vault.zip")
os.makedirs(os.path.dirname(os.path.abspath(dest)), exist_ok=True)

notas, titulos, errores = [], {}, []
for root, dirs, files in os.walk(VAULT):
    dirs[:] = sorted(d for d in dirs if not d.startswith("."))
    for f in sorted(files):
        if not f.endswith(".md"):
            continue
        full = os.path.join(root, f)
        rel = os.path.relpath(full, VAULT).replace("\\", "/")
        titulo = f[:-3]
        clave = titulo.strip().lower()
        if clave in titulos:
            errores.append("titulo repetido '%s' (%s y %s)" % (titulo, titulos[clave], rel))
        titulos[clave] = rel
        if RESERVADOS & set(titulo):
            errores.append("titulo con caracteres reservados: %s" % rel)
        raw = open(full, "rb").read()
        if any(b > 127 for b in raw):
            errores.append("no-ASCII: %s" % rel)
        if len(raw.decode("utf-8")) > MAX_CHARS:
            errores.append("supera %d caracteres: %s" % (MAX_CHARS, rel))
        notas.append((full, rel))

if ENTRADA.lower() not in titulos:
    errores.append("falta la nota de entrada '%s.md'" % ENTRADA)
if errores:
    print("NO se empaqueto:")
    for e in errores:
        print(" -", e)
    sys.exit(1)

with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED) as z:
    for full, rel in notas:
        z.write(full, rel)
print("ZIP:", os.path.abspath(dest))
print("notas:", len(notas), "- bytes:", os.path.getsize(dest))
