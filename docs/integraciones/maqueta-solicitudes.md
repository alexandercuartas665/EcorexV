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
