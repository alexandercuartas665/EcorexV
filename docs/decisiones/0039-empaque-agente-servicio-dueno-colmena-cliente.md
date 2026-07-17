# ADR-0039: Empaque del agente - el Servicio es dueno de identidad/canal/store; la colmena WPF es su cliente

- Estado: aceptada
- Fecha: 2026-07-16
- Revisa a: D4 del capitulo "Agente Conector On-Prem" del vault (doc 00 s3: "Servicio Windows
  headless + app WPF"), decidida ANTES de la expansion a colmena multi-agente.
- Relacionada con: D7 / doc 06 (stack .NET 10 + WPF, Windows-first), doc 04 (cliente),
  doc 05 Ola 5 (empaque), doc 06 s4 (guardrails de seguridad).

## Contexto

Hoy la colmena es **un solo proceso WPF** que hace todo: sostiene el canal SignalR con la identidad
(`ClientId` + secreto), ejecuta los tres sub-agentes (Gateway, Archivos, Navegador WebView2),
hospeda el servidor MCP local y guarda TODO su estado cifrado con DPAPI en `%APPDATA%`.

La Ola 5 pide partirlo en Servicio Windows (canal 24/7) + WPF de configuracion. Al ir a hacerlo
aparecen dos hechos duros:

1. **DPAPI de usuario**: `DpapiConfigStore` cifra con scope de USUARIO (no pasa
   `CRYPTPROTECT_LOCAL_MACHINE`) y guarda bajo `%APPDATA%\Ecorex\Agent`. Un servicio corre como
   LocalSystem o cuenta de servicio: **otro perfil, otra llave**. Partido en dos procesos, el
   servicio no puede leer NADA de lo que escribio la WPF (identidad, secreto, cadena de conexion,
   allow-lists, consentimiento).
2. **Sesion 0**: WebView2 necesita escritorio/sesion interactiva; Microsoft no lo soporta en la
   sesion 0 de un servicio. El Gateway y Archivos, en cambio, son headless-friendly.

Y el escenario de despliegue confirmado por el usuario (2026-07-16) es **ambos**: hay clientes con
estacion y usuario logueado, y hay servidores 24/7 sin sesion. No se puede elegir uno solo.

## Decisiones

### 1. El Servicio es el UNICO dueno de identidad, canal y store

`Ecorex.Agent.Service` (Worker Service, `UseWindowsService`) sostiene el WebSocket saliente
autenticado y es el unico proceso que abre el store. Un solo `ClientId` = una sola conexion = una
sola identidad ante el hub (dos procesos conectando con el mismo ClientId seria ambiguo para el
registro de agentes del servidor).

### 2. El store pasa a scope de MAQUINA, en ProgramData, con ACL

Como solo lo abre el servicio: `%ProgramData%\Ecorex\Agent` con DPAPI
`CRYPTPROTECT_LOCAL_MACHINE` + ACL restringida a la cuenta del servicio y administradores. Esto es
consecuencia de la decision 1, y de paso **mejora** la postura actual: hoy el secreto del tenant
vive en el perfil del usuario que abrio la colmena.

**Cuenta del servicio: LocalSystem** (decidido por el usuario, 2026-07-16). Consecuencia asumida: con
DPAPI de maquina la llave no cuelga del usuario, asi que **el ACL del archivo es la unica puerta**;
cualquier proceso que pueda LEER el archivo puede descifrarlo. Con LocalSystem eso significa que un
administrador local puede llegar al secreto del tenant. Escalon siguiente si algun dia se quiere
least-privilege: cuenta virtual `NT SERVICE\EcorexAgent`, que acota el ACL a la propia identidad del
servicio; el codigo no cambia (solo el instalador).

> **Hallazgo (2026-07-16, verificado en maquina real)**: **el instalador DEBE crear la boveda**; no se
> puede dejar que la cree perezosamente el primer proceso que arranque. Quien crea el directorio es su
> **propietario**, y un propietario siempre puede reescribir el DACL: si un usuario sin privilegios
> abre la colmena antes de que exista la boveda, queda como dueno y podria re-otorgarse acceso, y con
> eso leer el secreto del tenant que despues escriba el servicio. `AgentVault.EnsureDir()` sigue
> creando+endureciendo como red de seguridad, pero la creacion autoritativa (owner = Administradores)
> es responsabilidad de la Ola 5d.

### 3. La colmena WPF es CLIENTE del servicio (named pipe), no un peer

La GUI deja de descifrar y de conectarse al hub. Por pipe local: lee estado real para pintar la
colmena, y edita config/allow-lists/consentimiento (**persiste el servicio**, no ella). La GUI
nunca ve el secreto maestro. Es coherente con el principio de doc 06 s3.1 ("los sub-agentes reciben
solo la orden acotada, no las credenciales maestras"): ahora tambien aplica a la propia GUI.

### 4. El Navegador vive en la sesion interactiva; el Servicio se lo DELEGA a la colmena

Una peticion `BrowserRequest` llega al servicio (que es quien tiene el canal) y se **delega por el
pipe** a la colmena, que tiene escritorio y ejecuta WebView2. Si no hay colmena conectada (servidor
sin sesion), el servicio responde `BrowserFailed` con motivo explicito ("no hay sesion interactiva"),
**nunca cuelga la peticion**. Gateway y Archivos siguen atendiendo normalmente.

### 5. Se conserva WebView2; Playwright headless queda como add-on, no como reemplazo

El doc 06 s2 recomendaba Playwright. Se mantiene WebView2 (ya construido y verificado E2E, con
prior-art propio en Doom) porque cubre el caso "estacion con usuario", que es donde el navegador
tiene sentido de negocio (ver, operar, inyectar JS). Si aparece un cliente que necesite navegador en
un servidor SIN sesion, se agrega un `IBrowserSubAgent` con Playwright headless dentro del servicio:
el seam permite ambos sin tocar el resto. **No se construye ahora** (mismo criterio que Linux en D7).

## Consecuencias

- El seam `IBrowserSubAgent` (marshalling al Dispatcher escondido en la impl WPF) desacopla
  `RealHiveConnection` y `AgentMcpServer` de WPF: ~1400 de 1850 lineas de `Services/` quedan
  portables sin tocar logica. Habilita `Ecorex.Agent.Core` (net10.0, sin WPF).
- El MCP local queda en el proceso que tenga el navegador (la colmena), porque su razon de ser son
  las tools `browser.*` para una IA local; las `file.*` se atienden igual.
- Migracion del store: **no hay migracion automatica, y se comprobo que no puede haberla** (se
  intento en la Ola 5b y se retiro el codigo). El unico que puede DESCIFRAR el `%APPDATA%` viejo es el
  usuario que lo escribio (la colmena), y ese es justamente quien ya NO puede ESCRIBIR la boveda; el
  servicio puede escribirla pero no descifrar el archivo del usuario. Quien ya tenia la colmena
  configurada reconfigura una vez (hoy solo afecta la maquina de desarrollo).
- **5b y 5c estan acoplados**: al mudar la boveda, la colmena (proceso sin elevar) deja de poder
  leerla o escribirla -que es exactamente lo que esta ADR quiere-, y por tanto **no hay forma de
  configurar el agente hasta que exista el pipe (5c)**. Entre 5b y 5c, la identidad se escribe con
  `Ecorex.Agent.Service.exe --save-config` desde una consola de administrador (lo mismo que hara el
  instalador). No es un rodeo de pruebas: es el modelo definitivo, porque el dueno del store es el
  servicio.
- El instalador debe: registrar el servicio, crear ProgramData con su ACL, y dejar la colmena con
  auto-arranque al inicio de sesion.

## Deudas / pendientes de implementacion

- Ola 5a: seam `IBrowserSubAgent` + extraer `Ecorex.Agent.Core`.
- Ola 5b: `Ecorex.Agent.Service` + store de maquina (`CRYPTPROTECT_LOCAL_MACHINE` + ACL).
- Ola 5c: IPC named pipe (estado, config, delegacion del navegador).
- Ola 5d: instalador (Inno) + aceptacion en Windows limpio.
- Politica de cuenta del servicio (LocalSystem vs cuenta dedicada) a definir en 5b/5d.
