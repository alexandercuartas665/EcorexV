# ADR-0148: Guardar capacidades de la colmena escribiendo la boveda directo, no por el pipe

Fecha: 2026-11-04
Estado: Aceptada
Relacionada: ADR-0039 (boveda local del agente, machine-scope + ACL SYSTEM/Admins), ADR-0050
(elevacion por UAC para escribir la boveda sin reiniciar el servicio).

## Contexto

La colmena (GUI WPF) y el Servicio (`EcorexAgent`, LocalSystem) hablan por un named pipe local
(ADR-0039 s3). El Servicio es el dueno de la boveda cifrada en `%ProgramData%\Ecorex\Agent` con ACL
SYSTEM + Administradores. Ahi viven el consentimiento del operador (`consent.dat`) y las allow-lists
(`file-allow.dat` / `browser-allow.dat`).

El flyout de capacidades (Navegador / Archivos) persistia el cambio **por el pipe**: la colmena
enviaba `SetFileAllow` + `SetConsent` y el Servicio, en `AgentIpcServer.RequireAdmin`, exigia que la
CONEXION fuera de administrador (impersonando al cliente con `RunAsClient` + `IsInRole(Administrator)`).

Dos defectos hacian que esto fallara en un servidor:

1. **El cliente abria el pipe con `TokenImpersonationLevel.None`** (constructor por defecto de
   `NamedPipeClientStream`). Con nivel None, cuando el Servicio impersona obtiene un token ANONIMO y
   `IsInRole(Administrator)` da **false SIEMPRE**, aun con la colmena corriendo elevada. Es decir, la
   rama "elevado -> pipe" nunca podia tener exito; solo funcionaba el fallback de UAC.
2. **El flyout elegia el camino por el estado de elevacion del proceso, no por el resultado.** Si
   `ElevationHelper.IsElevated()` daba true (colmena lanzada como administrador), enviaba por el pipe
   (fire-and-forget) y el Servicio lo rechazaba; el operador solo veia en el mini-log
   `Rechazado: Guardar la configuracion exige administrador...` y nada se persistia. En estacion la
   colmena suele estar NO elevada (auto-arranque en bandeja) y tomaba el fallback de UAC, por eso "en
   mi PC si funciona"; en servidor, corrida como administrador, caia en la rama rota del pipe.

Sintoma real (servidor de produccion): "aunque ejecuto la colmena como administrador, no deja activar
'Habilitada por mi' ni guardar la allow-list de Archivos".

## Decision

1. **El guardado de una capacidad NO pasa por el pipe.** Se escribe la boveda DIRECTO, sin depender de
   la impersonacion del Servicio (`HiveViewModel.SaveCapability`):
   - si el proceso YA es admin (`ElevationHelper.IsElevated()`), el ACL de la boveda lo deja escribir
     `file-allow.dat`/`consent.dat` (o los de browser) sin pipe ni UAC extra;
   - si no, se eleva con un UNICO prompt de UAC via `--save-caps` (ADR-0050), que escribe la boveda.
   En ambos casos el Servicio recoge el cambio por su `FileSystemWatcher` (`SetupCapabilityWatcher`) y
   reemite su estado, asi la colmena refleja la lista/consentimiento sin reiniciar. Se envuelve la
   escritura directa en try/catch: si fallara (ACL atipico), cae al fallback de UAC.

2. **El cliente abre el pipe con `TokenImpersonationLevel.Identification`** (`AgentIpcClient`). Con
   Identification el Servicio puede LEER la identidad y los grupos del cliente (suficiente para el
   chequeo de administrador) pero NO suplantarlo para acceder a otros recursos (minimo privilegio).
   Esto arregla la causa raiz del rechazo y beneficia tambien a otras mutaciones por pipe (guardar la
   identidad), que hasta ahora dependian del fallback de UAC.

## Consecuencias

- Guardar Archivos/Navegador funciona en servidor y estacion, este el proceso elevado o no, sin
  depender del baile "instancia unica + ejecutar como administrador" (que a menudo solo surfacea la
  colmena NO elevada de la bandeja).
- La allow-list sigue siendo un control de seguridad: ensancharla EXIGE administrador (el no elevado
  pasa por UAC; el elevado ya lo es).
- `TokenImpersonationLevel.Identification` es el minimo nivel que habilita el chequeo de admin del
  Servicio; no habilita impersonacion de acceso.
- Requiere **reinstalar el agente** en los equipos ya desplegados (build nuevo). El instalado hoy es
  **1.9.2**; sube a **1.9.3**. La version se muestra en la colmena (assembly, `HiveViewModel.AgentVersion`),
  se reporta al hub (`RealHiveConnection.SafeHelloAsync`) y se pasa al instalador (`-p:ProductVersion=1.9.3`),
  los tres en linea. (Nota: el string reportado al hub traia un valor viejo "1.2.0" que no reflejaba lo
  desplegado; se corrige a 1.9.3.)

## Alternativas descartadas

- **Subir el nivel a `Impersonation`**: innecesario; el Servicio solo necesita leer grupos, no actuar
  como el cliente. Identification es menor privilegio.
- **Mantener el guardado por pipe y solo arreglar la impersonacion**: dejaba el guardado dependiendo de
  que la colmena conecte elevada; escribir la boveda directo cuando ya se es admin es mas simple y
  robusto, y el fallback de UAC cubre el no elevado.
