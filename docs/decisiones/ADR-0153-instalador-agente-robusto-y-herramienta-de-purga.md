# ADR-0153: Agente Colmena - instalador 1.9.5 robusto (sin trampa de rollback + verificacion) y herramienta de purga total

> Nota de numeracion: originalmente redactado como ADR-0152 en una sesion paralela; renumerado a
> ADR-0153 al integrar porque ADR-0152 ya quedo asignado a la ventana horaria de pulsos + festivos de
> Colombia (release v0.16.276). El contenido tecnico no cambia.

**Status:** Accepted
**Date:** 2026-11-05
**Deciders:** Usuario (dueno del producto), Claude Opus 4.8

## Contexto

Al instalar el agente 1.9.4 en un equipo de produccion fallo asi:

1. El servicio de Windows "no subio bien" durante la instalacion.
2. Tras arrancarlo a mano, el agente no se conectaba al servicio web.
3. Al ir a desinstalarlo, el agente NO aparecia en "Programas y caracteristicas", de modo que no
   habia como quitarlo por la via normal; quedaban servicio/archivos huerfanos.

El sintoma 3 (no figura como programa instalado + huerfanos) es la firma de un **rollback del MSI**.

### Causa raiz del rollback

En `installer/Product.wxs`, el servicio se controlaba con:

```xml
<ServiceControl ... Start="install" Stop="both" Remove="uninstall" Wait="yes" />
```

sobre un `<ServiceInstall ... Vital="yes" />`. Con `Start="install"` + `Wait="yes"` el MSI ARRANCA el
servicio durante la instalacion y ESPERA a que confirme el arranque dentro del timeout del gestor de
servicios. Si el servicio no lo confirma a tiempo (p.ej. tardanza del arranque, un throw en el arranque
del worker, o un entorno lento), y como la accion es sobre un componente Vital, el MSI considera la
instalacion fallida y **revierte todo**: deshace el registro en "Programas y caracteristicas" y, segun
el momento del fallo, deja el servicio y/o archivos a medio quitar. El operador se queda con un agente
"fantasma" imposible de desinstalar por la via normal.

Ademas, aunque el arranque hubiera sido fuera del instalador, el worker tenia dos puntos que podian
tumbar el servicio en el arranque (y con el comportamiento por defecto `StopHost`, dejarlo "detenido"):
`_ipc.Start()` (abrir el pipe local) estaba fuera del try/catch del arranque, y una excepcion no
controlada en `ExecuteAsync` detenia el host.

## Decision

Agente **1.9.5**. Tres frentes:

### 1. Quitar la trampa de rollback (Product.wxs)

Se elimina `Start="install"` del `<ServiceControl>` (queda `Stop="both" Remove="uninstall" Wait="yes"`).
El arranque del servicio ya NO bloquea ni puede revertir la instalacion. El servicio sigue siendo
`Start="auto"` (arranca solo en el proximo reinicio) y, ademas, el instalador lo arranca de inmediato
con la accion de verificacion (ver punto 2), que **ignora el fallo**. El resultado: la instalacion
SIEMPRE completa y queda registrada en "Programas y caracteristicas", pase lo que pase con el arranque
del servicio; si algo queda mal, se REPORTA en vez de reventar el MSI.

### 2. Verificar la instalacion + avisar al usuario (ADR del titulo: "que valide que todo quede instalado")

Nueva herramienta `Ecorex.Agent.Tool.exe` (proyecto `apps/agent/Ecorex.Agent.Tool`), que viaja dentro
del MSI. El instalador la invoca en dos pasos:

- **`EcorexVerifyInstall`** (CA deferida, NO impersonada = SYSTEM, `Return="ignore"`, tras
  `EcorexConfigureIdentity`): corre `Ecorex.Agent.Tool.exe verify --start --quiet`. Arranca el servicio
  y verifica archivos + servicio registrado + servicio en ejecucion, y deja el veredicto en
  `%ProgramData%\Ecorex\Agent\install-verify.log`. Al ser SYSTEM puede arrancar el servicio; al ser
  `Return="ignore"` un fallo del servicio NO revierte la instalacion.
- **`EcorexShowResult`** (CA inmediata, IMPERSONADA = sesion del usuario, `Return="ignore"`, tras
  `InstallFinalize`, solo si `UILevel >= 4`): corre `Ecorex.Agent.Tool.exe show-result`, que lee el log
  y muestra el veredicto en un cuadro de mensaje en la sesion del usuario (una CA deferida corre en la
  sesion 0, donde el cuadro seria invisible; por eso se separa en dos). En instalacion silenciosa no
  muestra nada.

La identidad (config.dat) NO cuenta como fallo de instalacion: en una instalacion limpia se configura
despues por la colmena. `verify` la reporta como informativa.

### 3. Blindaje del arranque del servicio (fail-soft)

- `Program.cs`: `BackgroundServiceExceptionBehavior.Ignore`, para que una excepcion no controlada del
  worker NO detenga el servicio (el valor por defecto `StopHost` lo detendria y `verify` lo veria
  "caido" aunque la instalacion este bien).
- `AgentWorker.cs`: `_ipc.Start()` (pipe local con la colmena) va dentro de try/catch; si el pipe no
  abre, el servicio sigue atendiendo el hub y solo la colmena local no reflejara estado hasta reiniciar.
- La conexion al hub sin identidad ya era fail-soft (el worker reintenta cada 30s).

### 4. Herramienta de purga total (la peticion "desinstalar todo rastro del agente")

La MISMA `Ecorex.Agent.Tool.exe`, en modo `uninstall` (o doble clic), purga TODO rastro aunque el
agente ya no figure como programa instalado:

- Detiene y elimina el servicio `EcorexAgent`; mata los procesos `Ecorex.Agent.Gui` / `Ecorex.Agent.Service`.
- Desinstala por MSI cualquier producto "Agente Colmena" que SI este registrado (ProductCode via ARP).
- Borra la carpeta de instalacion, el atajo del Menu Inicio, la llave Run (autostart), las claves
  `HKLM/HKCU\Software\Ecorex\AgenteColmena`, el origen del Visor de eventos, las entradas ARP huerfanas
  y el diag log.
- Por defecto borra tambien la boveda/identidad (`%ProgramData%\Ecorex\Agent`); `--keep-identity` la
  conserva.
- Reporta que quedo y, si algo persiste (p.ej. servicio marcado para borrado pendiente de reinicio), lo
  dice.

Se distribuye tambien como **exe suelto self-contained** (un archivo, sin runtime previo) para poder
copiarlo a un servidor ya roto y correrlo con doble clic.

### Diseno de la herramienta

- Proyecto `apps/agent/Ecorex.Agent.Tool`, net10.0-windows, sin referencias a otros proyectos a
  proposito (debe funcionar SOLA en un equipo roto): usa `sc.exe`/`msiexec` para servicio/MSI,
  `Microsoft.Win32.Registry` para el registro, `System.IO` para archivos y P/Invoke a `user32`
  (MessageBox) para el cuadro de resultado.
- Las rutas/nombres (servicio, carpetas, claves, UpgradeCode, origen de eventos) son el CONTRATO del
  instalador (fijados tambien en Product.wxs) para que no dependan del agente instalado.
- Manifiesto `asInvoker` + AUTO-elevacion (UAC) solo para `uninstall`/`verify`: un doble clic pide UAC
  una vez, y `show-result` (que solo lee un log) corre sin elevacion desde la CA impersonada del
  instalador. La CA deferida del instalador ya corre como SYSTEM, asi que ahi no hay UAC.
- `build-installer.ps1` publica la herramienta self-contained (sin single-file) en el MISMO staging que
  el agente: sus DLL de runtime se funden con los del agente y solo son nuevos su `.dll`,
  `.runtimeconfig.json` y el apphost; el MSI crece ~0.3 MB (sigue en 59 MB).

## Opciones consideradas

### A: Dejar el MSI como estaba y solo documentar "reinicie e intente"
Descartada: el fantasma ya estaba creado y no habia como desinstalar; no resuelve la reincidencia.

### B: `Start="install"` con `Return="ignore"` en el ServiceControl
WiX no ofrece "ignorar el fallo" en `ServiceControl` (no hay atributo de retorno). La unica forma de que
el arranque no pueda revertir es NO arrancar en el ServiceControl y arrancar por una CA con
`Return="ignore"`. Elegida.

### C: Verificacion que ABORTE la instalacion si el servicio no arranca (`Return="check"`)
Descartada: reintroduce exactamente el rollback que causo el fantasma. Se prefiere instalar siempre y
REPORTAR, y dar la herramienta de purga para rehacer en limpio.

## Consecuencias

- Instalar el agente ya no puede dejar un fantasma: la instalacion completa y queda desinstalable.
- El usuario ve, al terminar, si el servicio quedo corriendo; si no, hay un log y la herramienta de purga.
- Hay por fin una desinstalacion total garantizada, util para los equipos ya rotos por 1.9.4.
- El MSI ahora invoca un exe propio en una CA: si se firma el MSI (paso posterior), firmar tambien el exe.
- Pendiente: desplegar 1.9.5 a los equipos (requiere el OK del usuario); en los equipos rotos por 1.9.4,
  correr primero la herramienta de purga suelta y luego instalar 1.9.5.

## Seguimiento 2026-11-05: la identidad NO se guardaba aunque la colmena corriera elevada

Al instalar y correr la colmena **como administrador** seguia saliendo en el registro:

```
Rechazado: Guardar la configuracion exige administrador: usa 'Guardar' en la colmena y confirma el aviso de administrador (UAC).
Configuracion guardada.
```

Las dos lineas juntas eran un **falso exito**: la identidad NO quedaba guardada y el servicio nunca
conectaba. Causa: `HiveViewModel.SaveConfig`, en la rama "ya elevado", persistia **por el pipe**
(`PipeHiveConnection.TestConnectionAsync` -> `SetConfig`, que es admin-gated por
`AgentIpcServer.RequireAdmin`). El Servicio ve la conexion del pipe como NO-admin aunque la colmena
este elevada (el nivel del token de impersonacion del pipe), asi que RECHAZA el `SetConfig`; pero
`SaveConfig` ya habia logueado "Configuracion guardada." de forma optimista. Es EXACTAMENTE el bug que
ADR-0148 arreglo para las CAPACIDADES, pero la ruta de la IDENTIDAD se habia quedado atada al pipe.

Fix (parte de 1.9.5, extiende ADR-0148/0050):
- `SaveConfig` elevado: escribe la boveda **directo** (`_store.Save`, el proceso admin tiene acceso por
  el ACL SYSTEM+Administradores) y NO usa el pipe para persistir. El Servicio recoge la escritura por su
  `FileSystemWatcher` sobre `config.dat` y reconecta en ~1-2s. Si la escritura directa fallara (ACL
  atipico), cae al camino de UAC (`--save-config`).
- `TestConnectionAsync` ("Probar conexion" / AutoConnect): la prueba activa por pipe solo corre en demo
  (`_pipe is null`). En modo real ya no se manda `SetConfig` por el pipe (persistio directo + watcher),
  eliminando el "Rechazado" tambien en el arranque y al probar.

Resultado: guardar la identidad en la colmena (elevada o no) persiste de verdad; desaparece el
"Rechazado" y el servicio conecta.

## Seguimiento 2026-11-05 (2): no conectaba desde Windows Server 2012 (TLS) -> agente 1.9.6

Ya con 1.9.5 instalado limpio en el servidor de produccion (identidad guardada, sin "Rechazado"), el
agente quedaba Offline con `The SSL connection could not be established, see inner exception.` en bucle.

Diagnostico (script `Diagnostico-AgenteColmena.ps1`, corrido en el servidor):
- SO = **Windows Server 2012 Standard (6.2.9200)**, que **NO soporta TLS 1.3** (TLS 1.3 llego en Server 2022).
- El **token** (`POST /api/agente/token`) forzado a **TLS 1.2** da **200** (tenant SOLDARCO) -> red, DNS,
  cert y reloj OK; la cadena Let's Encrypt valida (raiz ISRG X1 presente).
- El handshake con **protocolo por defecto** (que ofrece TLS 1.3) FALLA, como usuario Y como LocalSystem:
  `No se pudo realizar una llamada a SSPI... la funcion solicitada no se admite` (SEC_E_UNSUPPORTED_FUNCTION).

Causa: el agente es .NET 10, cuyo handshake por defecto ofrece TLS 1.3; el SChannel de Server 2012 no lo
soporta y rechaza la llamada. (Server 2012 esta ademas por debajo del SO minimo de .NET 10 y en
fin de soporte.)

Fix (agente **1.9.6**, `RealHiveConnection`) - **TLS adaptativo por maquina**, para NO limitar a los
servidores modernos mientras se desbloquea el viejo (el usuario pidio explicitamente que el agente no
"quede limitado" en otros servidores):
- **Deteccion de TLS 1.3**: `Tls13Supported = OSVersion.Build >= 19041`. SChannel habilita TLS 1.3 desde
  Windows 10 2004 / Server 2022 (build 19041+); Server 2012 (9200), 2016 (14393) y 2019 (17763) quedan por
  debajo. Un solo corte separa correctamente clientes y servidores. .NET (Core) devuelve el build real.
- **Protocolos segun el SO** (`AgentSslProtocols`): moderno -> `Tls12 | Tls13` (SChannel elige 1.3);
  viejo -> `Tls12`. Se aplica en el `HttpClient` del token y en el handler HTTP del hub
  (`HttpMessageHandlerFactory`). Pedir 1.3 donde no existe es lo que dispara el SEC_E_UNSUPPORTED_FUNCTION,
  por eso en el viejo se omite el flag; en el moderno se habilita y NO se degrada.
- **Transportes segun el SO**: moderno -> `WebSockets | ServerSentEvents | LongPolling` (prefiere y usa
  WebSocket sobre TLS 1.3, el canal principal; los HTTP solo son respaldo). Viejo -> `ServerSentEvents |
  LongPolling` (se OMITE el WebSocket, que ofreceria 1.3 y fallaria; va directo a HTTP con TLS 1.2, sin el
  intento fallido previo). Un MISMO binario sirve para ambos.
- **Aplana el error TLS** (`FlattenError`): ahora el log muestra la causa real (SSPI/cert/DNS) en vez de
  "see inner exception.".

Por que NO limita a los modernos: en Server 2016+/Win10+ el canal real sigue siendo WebSocket sobre TLS 1.3
(los transportes HTTP de respaldo nunca se activan si el WS conecta) y las llamadas HTTP de control
(token/negotiate) usan 1.2+1.3. El unico "menos" es intrinseco del Server 2012: no puede hacer WebSocket ni
TLS 1.3, asi que ahi el agente usa SSE/LongPolling sobre TLS 1.2 -funcionalmente identico para el trafico
del agente (ordenes puntuales), solo mayor latencia que el WS-.

Recomendacion al usuario: 1.9.6 desbloquea el Server 2012 sin tocar a los modernos, pero ese SO esta en fin
de soporte y por debajo del minimo de .NET 10; conviene mover el agente a Windows Server 2016+ / Windows 10+
cuando se pueda.

## Seguimiento 2026-11-05 (3): el MSI de misma version NO actualizaba Core.dll -> agente 1.9.7

Tras instalar "1.9.6" en el Server 2012, el agente SEGUIA Offline con el mismo error, y el log mostraba
`The SSL connection could not be established, see inner exception.` **pelado** (sin la cadena ` -> ` que
agrega `FlattenError`). Eso PROBO que el `Core.dll` en ejecucion era un build VIEJO sin `FlattenError`: el
MSI 1.9.6 se instalo sobre un 1.9.6 anterior y **Windows Installer no sobrescribe un archivo cuando la
version entrante es igual a la instalada** (regla de file-versioning), asi que `Service.exe` figuraba 1.9.6
pero `Core.dll` -donde vive el fix de TLS- se quedo en el viejo.

Fix (agente **1.9.7**):
- **Bump a version estrictamente mayor** (1.9.7): obliga al MSI a reemplazar TODOS los binarios, eliminando
  la ambiguedad de "misma version no sobrescribe". Regla a futuro: cada entrega al cliente sube el numero.
- **Diagnostico de TLS/arranque en el log** (`RealHiveConnection.TlsDiagnostics`, lo escribe `AgentWorker`
  al arrancar): `Core vX.Y.Z; SO build ...; TLS13=...; protocolos=...`. Deja constancia EN LA MAQUINA de
  que build corre y que decidio, para distinguir binario viejo de incompatibilidad real del SO sin depurar
  a ciegas.
- Mantiene el TLS adaptativo del 1.9.6 (ahora SI en ejecucion al forzarse la sobrescritura).

Que esperar en el log del 1.9.7 en el Server 2012: `...TLS13=False; protocolos=Tls12` y, si algo aun
fallara, el error TLS YA aplanado con su causa interna real.

## Seguimiento 2026-11-05 (4): [RETRACTADO, ver (6)] ".NET 10 no soporta Windows Server 2012"

> RETRACTADO el mismo dia: la conclusion de abajo era INCORRECTA. El usuario aporto el dato clave -un
> agente anterior (.NET 10 tambien) SI conectaba desde ese Server 2012-, y la causa real resulto ser un
> root de Let's Encrypt faltante tras una renovacion del certificado (seguimiento 6). Se deja el texto
> como registro del razonamiento equivocado. El TLS adaptativo de 1.9.7 sigue siendo correcto.

Con 1.9.7 instalado en el Server 2012, el log de arranque CONFIRMA que el binario correcto corre y decidio
bien: `Diagnostico TLS/arranque: Core v1.9.7; SO build 6.2.9200.0; TLS13=False; protocolos=Tls12`. La
identidad tambien quedo guardada sin "Rechazado" (`Completa=True`) -> el instalador robusto, el fix de
guardar identidad y el TLS adaptativo quedan VALIDADOS. Pero el handshake SIGUE fallando:
`No se pudo pedir el token ...: The SSL connection could not be established, see inner exception.`

Diagnostico definitivo: el SO SI puede TLS 1.2 (el diagnostico por .NET Framework/PowerShell obtuvo
respuesta del servidor), pero el agente es **.NET 10**, y **.NET 10 NO soporta Windows Server 2012** (el
soporte de Win7/8.1/Server 2012/2012 R2 se elimino en .NET 9; el minimo es Server 2016). El runtime
arranca, pero su capa TLS usa APIs de SChannel que Server 2012 no tiene -> `SEC_E_UNSUPPORTED_FUNCTION`
aunque se fije solo 1.2. **No es corregible desde el agente**: fijar protocolos no basta porque el fallo
esta en como el runtime .NET 10 inicializa SChannel en ese SO.

Camino (no es cambio de codigo del agente):
1. Correr la Colmena en otra maquina Win10/11 o Server 2016+ con internet (y acceso a la LAN si lo
   necesita). El agente SALE hacia app2; no tiene que estar en ese servidor salvo que aloje un recurso
   local que el agente deba alcanzar. Suele ser el desbloqueo mas rapido.
2. Subir el SO del servidor a Server 2016+.
3. (Ultimo recurso) build aparte sobre un runtime viejo; fork grande y este es 2012 "a secas" (ni .NET 8
   lo garantiza). No recomendado.

Pendiente: decision del usuario (maquina alterna vs. upgrade de SO). Todo lo corregible en el agente esta
hecho y validado en el log.

## Seguimiento 2026-11-05 (5): en Win11 "instala pero no conecta" = DOS fallos de entorno, NO del codigo

Reportado: el agente 1.9.7 instala en una PC Windows 11 pero no conecta. Depurado EN esa maquina (la
sesion corre ahi) se descarto el codigo de forma exhaustiva: el MISMO binario conecta.

Matriz probada (todas contra app2, token POST + GET):
- Handler por defecto y handler explicito (Tls12|Tls13), como usuario, como SYSTEM (tarea programada) y
  como SERVICIO real (sc.exe): TODAS dan TLS OK (401). El handler TLS NO es el problema.
- El Service.exe self-contained real, en consola y como servicio temporal (sc.exe): CONECTA (Online).

Causa 1 (definitiva): **el instalador no arrancaba el servicio en instalacion INTERACTIVA.** El unico
arranque era la CA `EcorexVerifyInstall` (`verify --start`), anclada `After="EcorexConfigureIdentity"`,
y esa accion de identidad SOLO corre en instalacion desatendida (con `CLIENTID`). En doble-clic quedaba
el servicio `Stopped` (exit 1077 "nunca se intento arrancar"); por eso "no conecta": ni corria. Arranque
manual -> queda Running. FIX instalador: `ServiceControl Start="install" Wait="no"` (arranca al instalar,
sin esperar -> sin la trampa de rollback de 1.9.5-, sin depender de la CA de identidad).

Causa 2 (muy probable): **cache de cadena de certificados FRIO en el contexto del servicio** (LocalSystem
sin perfil). Las primeras ~24 min de intentos del servicio fallaban con "The SSL connection could not be
established" (sin inner) -construccion de cadena de Let's Encrypt / fetch del intermedio AIA-, mientras el
MISMO token por TLS desde el usuario/SYSTEM/consola si validaba. Tras varias corridas de diagnostico en
sesion de usuario (que bajaron y cachearon la cadena a nivel de maquina), el servicio empezo a validar y
quedo Online y ESTABLE. Explica por que 1.9.5 "conectaba" antes (cache tibio entonces) y la version del
agente es IRRELEVANTE a este sintoma. Nota operativa: si reaparece en un equipo nuevo, abrir
https://app2.bitcode.com.co una vez en un navegador de ese equipo calienta la cadena. (Opcional a futuro:
endurecer el handler con RevocationMode=NoCheck para acortar stalls; no se hizo por ahora.)

Conclusion: el TLS adaptativo (1.9.7) es correcto y NO limita. El bloqueo del Server 2012 (seguimiento 4)
es un asunto aparte y real (.NET 10 no soporta ese SO). La PC de validacion quedo Online.

## Seguimiento 2026-11-05 (6): CAUSA REAL del Server 2012 - certificado de app2 renovado a la cadena ECDSA / falta ISRG Root X2

El usuario rechazo (con razon) el veredicto (4): un agente anterior -tambien .NET 10- SI conectaba desde
ese mismo Server 2012, y dejo de hacerlo "cuando actualizamos el agente". Se reviso que NO cambio nada
relevante: el runtime empaquetado es el MISMO en todos los MSI 1.9.0..1.9.8 (unico pack en cache: 10.0.11)
y el codigo del token en 1.9.5 era `new HttpClient()` puro, identico al del agente que funcionaba (el unico
commit del canal desde entonces, ADR-0148, no toca HTTP/TLS). Mismo codigo + mismo runtime + mismo servidor
=> cambio el SERVIDOR/entorno, y la actualizacion del agente fue coincidencia.

La pista que se habia descartado era el `[FALLO] FALTA ISRG Root X2` del diagnostico. Se inspecciono la
cadena que sirve app2.bitcode.com.co HOY:

- Leaf `CN=app2.bitcode.com.co`, **sha384ECDSA**, **emitido 2026-11-04 03:14** (renovado el dia que empezo
  el problema), vence 2027-02-02.
- Cadena NUEVA de Let's Encrypt: app2 -> **YE1** -> **Root YE** (ISRG) -> **ISRG Root X2** -> (cruzado por)
  ISRG Root X1.

Antes del 2026-11-04 el certificado usaba la cadena RSA clasica (-> ISRG Root X1), que el Server 2012 SI
tiene; por eso el agente viejo validaba. La renovacion lo paso a la cadena ECDSA bajo **ISRG Root X2**, que
al Server 2012 le FALTA (SO viejo sin ese root). .NET 10 (SocketsHttpHandler) exige validar la cadena y
falla con "The SSL connection could not be established"; .NET Framework (Invoke-WebRequest de la prueba 6)
la resolvia por el cruce X2->X1 o por la auto-actualizacion de roots, por eso la prueba 6 "pasaba" y
confundio el diagnostico. Esto ademas explica por que el .NET Framework con defaults (SSL3/TLS1.0) fallaba
con SEC_E_UNSUPPORTED_FUNCTION: protocolos legacy deshabilitados en el servidor, NO ausencia de TLS 1.3.

FIX (lado servidor, sin tocar el agente): importar **ISRG Root X2** al almacen de la MAQUINA
(`Cert:\LocalMachine\Root`). Se exporto desde el almacen confiable de la PC de desarrollo (no de internet) y
se verifico la huella contra la oficial: SHA256 `69729B8E15A86EFC177A57AFB7171DFC64ADD28C2FCA8CF1507E34453CCB1470`,
SHA1 `BDB1B93CD5978D45C6261455F8DB95C75AD153AF`. Entregado como `ISRG-Root-X2.cer`. Comando en el servidor
(consola admin): `certutil -addstore -f Root C:\Maestro\ISRG-Root-X2.cer` y reiniciar `EcorexAgent`.
Pendiente de confirmacion del usuario. Si tras esto el agente responde "handshake rechazado (401)", es el
secreto rotado: reingresar el actual desde /agentes-colmena en la colmena.

Leccion: ante "dejo de conectar sin tocar nada" revisar PRIMERO la fecha de emision del certificado del
servidor y los roots del cliente; una renovacion de Let's Encrypt puede cambiar la cadena (RSA->ECDSA).

## Verificacion

- `build-installer.ps1 -Version 1.9.7` produce el MSI (59 MB, 0 errores); las 3 exe + Core.dll = 1.9.7.0.
- `build-installer.ps1 -Version 1.9.8` produce el MSI (59 MB, 0 errores); 3 exe + Core.dll = 1.9.8.0;
  tabla `ServiceControl` Event=163 (bit Start=install presente; 1.9.7 tenia 162).
- `build-installer.ps1 -Version 1.9.5` produce el MSI (59 MB, 0 errores).
- Tablas del MSI: CAs `EcorexVerifyInstall` + `EcorexShowResult` presentes; `Ecorex.Agent.Tool.exe`
  (+ dll/deps/runtimeconfig) empaquetado; `ServiceControl.Event = 162` (Stop-install + Stop/Delete-
  uninstall, SIN el bit Start=install).
- La herramienta suelta: `--help` y `show-result` corren sin admin; `uninstall`/`verify` auto-elevan.
- Pendiente de prueba en el equipo del usuario: instalar 1.9.5 limpio y confirmar el cuadro de resultado
  "INSTALACION OK" + el servicio corriendo; y purgar un equipo roto por 1.9.4.
