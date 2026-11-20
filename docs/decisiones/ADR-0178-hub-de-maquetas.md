# ADR-0178: Hub de maquetas /maqueta (hospedaje del anotador + envio por sesion)

Fecha: 2026-11-17
Estado: Aceptada
Version: v0.16.341
Relacionado: ADR-0176 (POST /api/maqueta/solicitudes)

## Contexto

Cada maqueta (ej. `D:\DesarrolloIA\tronox.maqueta`) trae un kit de anotador estatico (`anotador/` +
`anotador.config.js`) que hace picker, captura y envia tarjetas a `POST /api/maqueta/solicitudes` (ADR-0176). El
anotador necesita estar en el MISMO origen que la maqueta (lee el DOM del iframe de la maqueta). Hoy cada maqueta
se sirve en un puerto propio y el envio exige pegar un token `ecx_` en el anotador. La sesion de la maqueta pidio un
area en ECOREX que hospede las maquetas bajo el origen de ECOREX y un launcher detras del login, sin tocar el kit.

## Decision

1. **Registro en configuracion** (`MaquetaHub` en appsettings): `Enabled`, `AllowOutsideDevelopment` y
   `Projects[{Id, Nombre, RutaFisica}]`. El area SOLO se activa con `Enabled` y (entorno Development o
   `AllowOutsideDevelopment`). `appsettings.json` lo deja apagado; `appsettings.Development.json` registra `tronox`.
   Produccion corre con `ASPNETCORE_ENVIRONMENT=Production`: el area no existe. Ids validos `^[a-z0-9][a-z0-9-]{0,39}$`;
   se descartan (con aviso) ids invalidos o repetidos y carpetas inexistentes o relativas.
2. **Hospedaje con StaticFiles** (no YARP): por proyecto, `PhysicalFileProvider(RutaFisica, ExclusionFilters.Sensitive)`
   con `RequestPath=/maqueta/{id}`, dentro de un `UseWhen` que va DESPUES de autenticacion/autorizacion y exige sesion
   de usuario de tenant (anonimo -> /login; sin `tenant_id` -> 403). El proveedor rechaza `..` y rutas absolutas; se
   excluyen dot-files/ocultos (.git, .env); solo tipos conocidos (`ServeUnknownFileTypes=false`); sin listado de
   directorios; `nosniff`, `no-cache` y `X-Frame-Options: SAMEORIGIN`. Se descarto YARP: agrega una dependencia y un
   proceso aparte por maqueta para lo que son archivos en disco.
3. **Launcher `/maqueta`** (Blazor, policy `MaquetaHub.Usar` = usuario de tenant): selector de proyecto + iframe con
   `/maqueta/{id}/anotador/anotador.html` y enlace "Pantalla completa". Con el hub inactivo muestra un aviso.
4. **Envio por sesion**: `POST /api/maqueta/solicitudes` acepta, ADEMAS del Bearer `ecx_`, la cookie de sesion de
   ECOREX cuando NO llega Bearer y el hub esta activo. El actor sale del usuario logueado en su tenant
   (`IMaquetaSolicitudService.ResolveSessionActorAsync`, bajo el filtro global; usuario y tenant activos) con
   `TokenId = Guid.Empty`. Anti-CSRF: `Content-Type: application/json` obligatorio y, si vienen, `Origin` = origen propio
   y `Sec-Fetch-Site: same-origin`. Un Bearer invalido NO cae a la sesion (401). Con el hub apagado (prod) el endpoint
   sigue exigiendo Bearer como en ADR-0176.
5. **Auditoria**: la alta/actualizacion por sesion se audita con motivo `integ-maqueta-hub` y `canal:"hub"` (la del
   token conserva `integ-maqueta`, `canal:"token"`).

## Consecuencias

- Sin migracion. El kit del anotador no cambia; para usar la sesion, su cliente JS debe postear al mismo origen sin
  Authorization (y `credentials:'include'`/`same-origin`). Mientras tanto, pegar el token `ecx_` en el boton ECOREX del
  anotador tambien funciona (mismo origen, sin CORS).
- Agregar una maqueta = una entrada en `MaquetaHub:Projects` y reiniciar.
- Riesgo acotado: se sirven archivos del disco del desarrollador; por eso solo Development/bandera, solo usuarios de
  tenant, solo dentro de `RutaFisica` y sin dot-files.
- Pruebas: `MaquetaHubTests` (registro, gating, sesion exigida, no sale de la carpeta, sin listado) y
  `MaquetaSolicitudesTests.SesionDelHub_*` (dual PG/SQL Server: actor por sesion, aislamiento, auditoria del canal).
