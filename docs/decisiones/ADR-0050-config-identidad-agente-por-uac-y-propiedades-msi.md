# ADR-0050: Configurar la identidad del agente Colmena sin la trampa de elevacion (UAC + propiedades MSI)

- Estado: Aceptado
- Fecha: 2026-07-28
- Contexto: apps/agent (Ecorex.Agent.Gui, Ecorex.Agent.Service, Ecorex.Agent.Core) + apps/agent/installer
- Relacionado: ADR-0039 (servicio dueno de identidad/canal/boveda; boveda machine-scope DPAPI con
  ACL SYSTEM+Administradores), ADR-0049 (instalador MSI del agente).

## Contexto

Bug real en una instalacion en SOLDARCO (Windows Server, MSI 1.0.0): el agente instala bien pero NO
se puede configurar la identidad desde la colmena. Al dar Guardar/Probar en el flyout de
Configuracion, el mini-log muestra:

> "Rechazado: Cambiar la configuracion del agente exige permisos de administrador en este equipo."

Diagnostico:
- La boveda (ADR-0039) es machine-scope con ACL SYSTEM+Administradores: cambiar la identidad EXIGE
  elevacion. El servicio decide "admin?" impersonando la conexion del pipe (`AgentIpcServer.IsClientAdmin`).
  La comprobacion es correcta (seguridad) y NO se debilita.
- La colmena se auto-lanza tras el MSI como el usuario logueado, **sin elevar** (ADR-0049, custom
  action `--tray`). Esa instancia de bandeja tiene conexion NO-admin -> el servicio rechaza el cambio.
- "Ejecutar como administrador" abria una SEGUNDA instancia (no habia mutex), y el usuario solia
  seguir en el icono de la instancia vieja -> seguia rechazado.
- El unico camino que funcionaba era abrir una consola realmente elevada y correr
  `Ecorex.Agent.Gui.exe --save-config ...` (escribe la boveda directo). Paso manual no obvio.

## Decision

### A. Auto-elevar por UAC al guardar (camino principal)
Cuando el operador da **Guardar** o **Probar** en la colmena y el proceso NO esta elevado, en vez de
fallar se dispara **una elevacion UAC**:
- La GUI se relanza a si misma ELEVADA (`Verb=runas`) con `--save-config <clientId> <hubUrl> [secret]`
  (`ElevationHelper.SaveConfigElevated`). Un solo prompt de UAC. `--save-config` ya escribe la boveda
  directo (`DpapiConfigStore`), sin pasar por el pipe.
- El secreto solo viaja por linea de comandos cuando es NUEVO: vacio significa "conserva el actual",
  que `--save-config` resuelve leyendo la boveda (antes lo sobrescribia con vacio: se corrigio).
- El **servicio recarga solo**: un `FileSystemWatcher` sobre `%ProgramData%\Ecorex\Agent\config.dat`
  (con debounce) dispara el rearme del canal, sin `Restart-Service`. Antes el rearme solo lo disparaba
  la ruta por pipe (`_onConfigChanged`); la escritura directa quedaba invisible hasta el reintento.
- El mensaje de rechazo del servicio se volvio accionable ("usa 'Guardar' en la colmena y confirma el
  aviso de administrador (UAC)").
- **Mutex de instancia unica** (per-sesion) + evento nombrado: un segundo lanzamiento (p.ej. "Run as
  administrator") no abre otra ventana; le hace una senal a la existente para que se muestre y sale.

No se debilita el modelo: la boveda sigue machine-scope con su ACL; cambiar identidad SIGUE exigiendo
elevacion. Solo se logra que la elevacion ocurra por UAC de forma fluida, en vez de fallar.

### B. El MSI acepta la identidad como propiedades (install desatendido)
    msiexec /i Ecorex-AgenteColmena-1.1.0.msi /qn CLIENTID=cli_xxx HUBURL=https://app2.bitcode.com.co SECRET=xxx
Una custom action **deferida + no-impersonada (SYSTEM, ya elevada)** invoca `--save-config` con esas
propiedades tras instalar los archivos, dejando el agente configurado de una vez. Ideal para varios
servidores (SOLDARCO tiene mas). El comando se pasa por `CustomActionData` (una CA deferida no lee la
tabla de propiedades). `SECRET` va en `MsiHiddenProperties` (no aparece en el log del MSI).

## Consecuencias / caveats

- Un secreto en linea de comandos (GUI elevada o `msiexec`) es visible en el arbol de procesos
  mientras corre; documentado. Para B se marca `SECRET` como oculto en el log; conviene distribuir el
  secreto por un canal seguro. La ruta comun (cambiar solo ClientId/URL) NO manda secreto.
- El `FileSystemWatcher` puede emitir varios eventos por escritura: se coalescen con debounce y el
  worker drena senales extra para no rearmar el canal varias veces.
- La CA deferida de B usa el QuietExec de la extension WixToolset.Util (ya referenciada por el MSI).
- Nueva version del MSI: **1.1.0** (para redistribuir a SOLDARCO). Identidad de prueba:
  ClientId `cli_bf450c7fc275`, hub `https://app2.bitcode.com.co`.
