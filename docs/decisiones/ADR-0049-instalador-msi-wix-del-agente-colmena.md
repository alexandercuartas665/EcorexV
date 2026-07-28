# ADR-0049: Instalador MSI (WiX) self-contained del agente Colmena

- Estado: Aceptado
- Fecha: 2026-07-28
- Contexto: apps/agent (Ecorex.Agent.Service, Ecorex.Agent.Gui) + apps/agent/installer
- Relacionado: ADR-0039 (agente Colmena: servicio dueno de identidad/canal/boveda; la GUI presta
  el escritorio), ADR-0045 (modulo del cliente agente), ADR-0048 (ejecutor REST).

## Contexto

El agente Conector On-Prem "Colmena" (apps/agent) hasta ahora se corria a mano: el Worker Service
(Ecorex.Agent.Service, servicio de Windows "EcorexAgent") y la GUI de bandeja (Ecorex.Agent.Gui) se
lanzaban sueltos, y la identidad (ClientId + URL del hub + secreto) se guardaba con un comando de
consola. El README ya listaba "instalador/servicio Windows" como pendiente. Un despliegue en la
estacion de un cliente necesitaba: registrar el servicio, dejar la GUI en la bandeja arrancando con
Windows, y NO exigir que el cliente instale ningun runtime .NET.

## Decision

1. **Instalador MSI con WiX v5** (`apps/agent/installer/Ecorex.Agent.Installer.wixproj` +
   `Product.wxs`), construible con `dotnet build` porque usa `WixToolset.Sdk/5.0.2` (se restaura de
   NuGet; no hace falta la herramienta global `wix`). La extension `WixToolset.Util.wixext` aporta
   el registro del origen del Visor de eventos.

2. **Publicacion SELF-CONTAINED (win-x64), en CARPETA (no single-file).** Perfiles
   `win-x64-selfcontained.pubxml` en cada proyecto: `SelfContained=true`, `RuntimeIdentifier=win-x64`,
   sin PDB. El runtime .NET viaja dentro del paquete: la maquina cliente NO instala nada.
   - Se descarta single-file: complica WPF/WebView2 y no aporta, porque el MSI ya empaqueta y
     comprime todo en un cab embebido.
   - Servicio y GUI se publican en la MISMA carpeta staging: comparten runtime `net10.0-windows`,
     asi que los `.dll` identicos se sobreescriben sin duplicar ~130 MB. Los DOS exe se separan a
     una carpeta aparte (`dist/exe`) para declararlos como componentes propios (el servicio necesita
     su exe como KeyPath para el `ServiceInstall`) y que la cosecha `<Files>` del resto no los duplique.

3. **El MSI (perMachine, elevado):**
   - Instala en `%ProgramFiles%\Ecorex\Agente Colmena`.
   - Registra el **Servicio de Windows** `EcorexAgent` (`ServiceInstall`: LocalSystem, arranque
     automatico; `ServiceControl`: arranca al instalar, detiene al desinstalar/upgrade, se elimina
     al desinstalar). Es el nombre que espera `Program.cs`.
   - Registra el **origen del Visor de eventos** "ECOREX Agente" (que el codigo usa pero no crea).
   - Instala la **GUI de bandeja**: acceso directo en el Menu Inicio y **AUTOSTART** por la llave
     `HKLM\...\CurrentVersion\Run` que la lanza con `--tray` (nace como icono de bandeja, sin robar
     foco en el logon; arranca para cualquier usuario que inicie sesion, coherente con perMachine).
   - Crea y **PRESERVA** la carpeta de config compartida `%ProgramData%\Ecorex\Agent` (la misma que
     usa `AgentVault`), donde el servicio y la GUI leen la MISMA identidad cifrada (`config.dat`).
   - **UPGRADE**: `UpgradeCode` estable + `MajorUpgrade` que reemplaza la version previa.
   - **Desinstalacion** limpia: quita servicio, binarios y accesos directos.

4. **Autostart por llave Run (HKLM), no por acceso directo en Startup.** La llave Run es
   determinista, per-machine (coherente con el MSI perMachine) y facil de auditar/quitar. Se le pasa
   `--tray`, opcion nueva de la GUI que arranca minimizada a la bandeja sin parpadeo de ventana.

5. **Auto-lanzar la bandeja al TERMINAR la instalacion** (no solo en el proximo logon). La llave Run
   solo dispara en el inicio de sesion, asi que tras instalar el servicio corria pero NO habia icono
   en la bandeja hasta re-loguear (observado en una instalacion previa). Se agrega una custom action
   tipo 18 (`LaunchTrayGui`) que ejecuta el exe ya instalado con `--tray`, **inmediata + impersonada**
   (el icono nace en la sesion del USUARIO instalador, no en la sesion 0 del servicio),
   **`asyncNoWait`** (no espera ni puede hacer fallar la instalacion, p.ej. un install silencioso por
   SCCM sin usuario interactivo), agendada **despues de `InstallFinalize`** (el exe ya esta en disco)
   y condicionada a `NOT Installed` (solo instalacion/upgrade, nunca al desinstalar).

6. **Diagnostico de conexion en la GUI (mini-log).** "Probar conexion" solo movia el punto de estado;
   si fallaba, la colmena no mostraba nada accionable y el operador no podia diagnosticar en su equipo.
   El servicio ya producia un `LastError` detallado (rechazo de handshake con codigo HTTP, URL mala,
   token, reloj desfasado) que viajaba por el pipe hasta la GUI (`StatusDetail`) pero no se pintaba.
   Se agrega un REGISTRO con hora en el flyout de Configuracion (mas nuevo arriba) que traza cada
   intento ("Probando...", "Conectando...", "Conectado: en linea.", "Sin conexion (offline).") y el
   motivo EXACTO del fallo ("Error: ...", "Rechazado: ..."). Es solo presentacion en `HiveViewModel` +
   `MainWindow.xaml`; el contrato del canal no cambia.

## Preservacion de la config en upgrade y desinstalacion

La clave es que **el MSI no gestiona `config.dat`**: ese archivo lo escribe el cliente desde la GUI
(o por `Ecorex.Agent.Service.exe --save-config`), no el instalador. El componente de la carpeta de
config es `Permanent` y solo hace `CreateFolder`. Por eso:
- En **upgrade**, `MajorUpgrade` retira la version vieja y pone la nueva, pero nunca toca
  `%ProgramData%\Ecorex\Agent\config.dat`: la identidad del cliente sobrevive.
- En **desinstalacion**, la carpeta de config se conserva (Permanent). Se documenta que quitarla es
  un paso manual si se desea una limpieza total.

El endurecimiento del ACL de esa carpeta (SYSTEM + Administradores) lo hace la app (`AgentVault`)
la primera vez que escribe; el instalador solo garantiza que la carpeta exista.

## Consecuencias

- El paquete es grande (~58 MB comprimido; ~167 MB instalado) por ser self-contained. Aceptado: es
  el precio de no exigir runtime en el cliente.
- El **MSI no se firma** (no hay certificado). La firma de codigo (signtool con un cert de Authenticode)
  queda como paso posterior de release; sin ella Windows/SmartScreen muestra advertencia de editor
  desconocido. Documentado en `apps/agent/README.md`.
- La version del producto se pasa por parametro (`-Version`, por defecto 1.0.0). No hay `<Version>`
  central en los csproj del agente; si se quisiera, se centralizaria en un `Directory.Build.props`.
- Alternativas descartadas: MSIX (requiere firma si o si y complica el servicio de Windows clasico);
  instalador single-file .exe (peor para WPF/WebView2 y para el registro del servicio).
