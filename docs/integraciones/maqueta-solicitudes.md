# Integracion maqueta -> tarjetas (POST /api/maqueta/solicitudes)

Decision y detalle: `docs/decisiones/ADR-0176-integracion-maqueta-anotaciones-a-tarjetas.md`.
Contrato del cliente: `tronox.maqueta/docs/INTEGRACION-ECOREX-ANOTACIONES.md` (no cambiar nombres ni ruta).

## 1. Emitir el token (una vez por usuario)

1. Entrar a ECOREX con un usuario del tenant donde deben caer las tarjetas (ej. ACUARTAS).
2. Barra superior -> **Conectar IA** (`/conectar-ia`) -> en "Nuevo conector" elegir **Maqueta (anotaciones)**,
   nombre "Maqueta TRONOX" -> **Generar token**. Se muestra UNA sola vez; se revoca desde la misma pantalla.
3. En el admin de la maqueta (`http://localhost:8096/admin.html`), boton **ECOREX**: URL base de ECOREX
   (ej. `https://app2.bitcode.com.co` o `http://localhost:5234`) y el token.

## 2. Prueba de aceptacion (curl)

```bash
curl -i -X POST "http://localhost:5234/api/maqueta/solicitudes" \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Content-Type: application/json" \
  --data '{"origen":"maqueta-tronox","idLocal":"an-test-1","tablero":"Maqueta TRONOX - Ajustes","titulo":"Prueba de integracion","descripcion":"cuerpo","categoria":"Ajuste","prioridad":"Media","autor":"QA","modulo":"RQ07","pantalla":"Catalogo de Terceros","ruta":"modulo/terceros","archivoMaqueta":"rq07-terceros.html","origenApp":"Pages/Terceros.razor","handoff":"HANDOFF-rq07.md","elemento":{"tag":"button","texto":"+ Nuevo Tercero","clases":"btn","selector":"button.btn","rect":{"x":0,"y":0,"w":10,"h":10}},"tema":"classic-light","viewport":"desktop","creadoEn":"2026-11-17T00:00:00Z","captura":null}'
```

- Primera vez: `201` con `{"id":"T00012","url":".../actividades?board=...&task=...","estado":"creada"}`.
- Repetir el mismo `idLocal`: `200` con `estado:"actualizada"` y el MISMO `id` (no duplica).
- Sin token o token de otra integracion: `401`. JSON invalido o sin `idLocal`/`titulo`: `400`.

Preflight CORS (lo hace el navegador):

```bash
curl -i -X OPTIONS "http://localhost:5234/api/maqueta/solicitudes" -H "Origin: http://localhost:8096" -H "Access-Control-Request-Method: POST"
```

Debe responder `204` con `Access-Control-Allow-Origin: http://localhost:8096`.

## 3. Configuracion

| Clave | Por defecto | Uso |
|---|---|---|
| `MaquetaIntegration:AllowedOrigins` | `http://localhost:8096,http://127.0.0.1:8096` | Origenes del navegador permitidos (coma) |
| `ECOREX_PUBLIC_BASE_URL` | (la del request) | Base del enlace `url` de la respuesta |

No hay clave secreta en configuracion: la autenticacion es el token personal (hash SHA-256 en BD).

## 4. Hub de maquetas en ECOREX (ADR-0178, solo Development)

Con `MaquetaHub` activo (por defecto en `appsettings.Development.json`, proyecto `tronox`):

1. Entrar a ECOREX local y abrir `/maqueta`: elegir el proyecto; el anotador se abre en
   `/maqueta/<id>/anotador/anotador.html` (mismo origen que ECOREX, detras del login).
2. Envio de tarjetas: si el anotador postea a `/api/maqueta/solicitudes` SIN `Authorization`, se usa la sesion de
   ECOREX (el tenant y el solicitante salen del usuario logueado; auditoria `integ-maqueta-hub`). Requiere
   `Content-Type: application/json` y mismo origen. Con un token `ecx_` en el boton ECOREX tambien funciona.
3. Agregar otra maqueta: una entrada en `MaquetaHub:Projects` (`Id` en minusculas, `Nombre`, `RutaFisica` absoluta)
   y reiniciar.

| Clave | Por defecto | Uso |
|---|---|---|
| `MaquetaHub:Enabled` | `false` (`true` en Development) | Activa el area /maqueta |
| `MaquetaHub:AllowOutsideDevelopment` | `false` | Permite activarla fuera de Development |
| `MaquetaHub:Projects` | `[]` | `{ Id, Nombre, RutaFisica }` por maqueta |

## 5. Maquetas en la nube (ADR-0179)

1. En ECOREX (prod o local) abrir `/maqueta` con un Owner/Admin -> **+ Nuevo proyecto** (nombre; el identificador de
   URL se sugiere solo, ej. `tronox-sgdea`).
2. **Versiones** -> subir el ZIP de la carpeta de la maqueta (con `anotador/` para poder anotar). Cada subida es una
   version nueva y queda como actual; en la tabla se puede **Ver** una version (`/maqueta/<slug>~<n>/...`) o **Usar esta**.
3. Cualquier usuario de la empresa abre `/maqueta`, elige el proyecto y anota; las tarjetas llegan con su sesion.

Subir una version desde el PC (script o Claude Code), con un token "Maqueta (anotaciones)" de un Owner/Admin:

```bash
curl -X POST "https://app2.bitcode.com.co/api/maqueta/proyectos/tronox-sgdea/versiones?notas=Ajustes%20RQ07" \
  -H "Authorization: Bearer <TOKEN>" -H "Content-Type: application/zip" --data-binary "@tronox-maqueta.zip"
```

Respuesta `201 {"proyecto","version","archivos","entrada","anotador","url"}`; `400` ZIP invalido, `401` token,
`403` no es Owner/Admin, `404` proyecto inexistente, `413` mayor a 100 MB.

## 6. Editor del sistema y maqueta en HTML puro (ADR-0180, v0.16.343)

- El menu trae **Sistema - Actividades > Maquetas** en todos los tenants (debajo de "Administrar actividades").
- `/maqueta` es una **galeria de tarjetas**: una por proyecto, con vista previa, version, archivos y solicitudes
  abiertas. Owner/Admin: **+ Nuevo proyecto** y **Versiones** (configuracion: nombre y tablero destino; subir ZIP;
  abrir o usar una version; archivar).
- `/maqueta/<slug>` es el **editor** (tipo Lovable): solicitudes a la izquierda, maqueta a la derecha. **Seleccionar**
  -> clic en un elemento -> captura + selector -> **Crear tarjeta**. La tarjeta cae en el tablero del proyecto (por
  defecto "Maqueta <Nombre> - Ajustes") con origen `maqueta-<slug>`. Cada solicitud tiene **Ver en la maqueta**
  (abre la pantalla y resalta el elemento) y **Abrir tarjeta**. `?v=<n>` anota una version especifica.
- El ZIP ya **no necesita** el kit `anotador/`: basta el HTML de la maqueta (index.html + pantallas). Si la raiz trae
  `anotador.config.js` (window.ANOTADOR_MANIFEST) el editor ofrece sus temas. Las pantallas salen de los `.html` del
  ZIP (titulo de `<meta name="maq-titulo">` o `<title>`, modulo de `<meta name="maq-modulo">`).
- La respuesta de `POST /api/maqueta/proyectos/{slug}/versiones` agrega `editor` (URL del editor del proyecto).
