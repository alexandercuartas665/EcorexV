# Agente Conector On-Prem - despliegue

Instalacion del agente en la maquina del cliente. Ola 5d; el reparto de piezas lo fija
[ADR-0039](../../docs/decisiones/0039-empaque-agente-servicio-dueno-colmena-cliente.md).

## Que se instala, y por que son dos piezas

| Pieza | Que es | Por que no es una sola |
|---|---|---|
| **Servicio** `EcorexAgent` | Worker headless, **LocalSystem**, arranque automatico. Dueno de la identidad, del canal y de la boveda. Atiende **Gateway** y **Archivos**. | Debe correr 24/7 **aunque nadie inicie sesion** (hay clientes que son servidores). |
| **Colmena** (WPF) | La cara del agente: arranca al iniciar sesion, muestra el estado real y **le presta el escritorio al Navegador**. | WebView2 exige sesion interactiva: **no vive en la sesion 0** de un servicio. |

En un servidor sin sesion, Gateway y Archivos trabajan igual y una orden de Navegador **falla con
motivo explicito** en vez de quedarse colgada.

## Pasos

```powershell
# 1. Publicar (autocontenido: no exige instalar .NET en la maquina del cliente)
.\publish.ps1

# 2. Instalar (consola de ADMINISTRADOR)
.\install.ps1 -ClientId cli_acme -HubUrl https://ecorex.midominio.com -Secret "<secreto>"

# ...o instalar sin identidad y configurar despues desde la colmena:
.\install.ps1
```

La URL puede ser la **base**: el agente le agrega `/hubs/agente` si falta.

## Requisitos

- Windows 10/11 o Windows Server (x64).
- **Runtime de WebView2** (Edge), solo para el Navegador. Viene de serie en Windows 11 y en Windows 10
  actualizado. Si falta, el Navegador falla con motivo y el resto del agente sigue trabajando.
- Salida HTTPS hacia el servidor ECOREX. **No hace falta abrir ningun puerto entrante**: el agente
  marca hacia afuera (por eso atraviesa NAT/firewall sin tocar la red del cliente).
- .NET NO es requisito: el publicado es autocontenido.

## Donde queda cada cosa

| Que | Donde |
|---|---|
| Binarios | `%ProgramFiles%\ECOREX\Agente\{service,gui}` |
| Boveda (identidad, secretos, allow-lists, consentimiento) | `%ProgramData%\Ecorex\Agent` |
| Bitacora del servicio | Visor de eventos -> Aplicacion -> origen **"ECOREX Agente"** |
| Auto-arranque de la colmena | `HKLM\...\CurrentVersion\Run` -> `EcorexColmena` |

## Seguridad (lo que hay que saber antes de aprobar el despliegue)

- La **boveda la crea el instalador** con owner = Administradores y ACL cerrada (SYSTEM +
  Administradores). No es un detalle: quien crea el directorio es su propietario y un propietario
  puede reescribir el ACL, asi que dejarsela crear al primer proceso que arranque permitiria que un
  usuario sin privilegios quedara de dueno del sitio donde vive el secreto del tenant.
- Se cifra con **DPAPI de maquina**. La llave no cuelga del usuario, asi que **el ACL del archivo es
  la unica puerta**: con el servicio como LocalSystem, un administrador local puede llegar al
  secreto. Es lo aceptado (D9); el escalon siguiente para least-privilege es una cuenta virtual
  `NT SERVICE\EcorexAgent` (cambia este instalador, no el codigo).
- **Configurar el agente exige administrador**, tambien desde la colmena: ensanchar la allow-list de
  Archivos con el servicio corriendo como SYSTEM equivaldria a abrirle a la nube el disco entero. Un
  usuario normal puede ver el estado y prestar el escritorio, nada mas.
- El **secreto nunca sale** de la boveda hacia la colmena: se escribe, no se lee.
- La credencial de la fuente local (BD de la LAN) **no viaja por el canal**: la guarda el agente.

## Diagnostico

```powershell
Get-Service EcorexAgent

# El mismo binario corre en consola y cuenta lo que hace (consola de ADMINISTRADOR: lee la boveda)
& "$env:ProgramFiles\ECOREX\Agente\service\Ecorex.Agent.Service.exe"
```

Si no conecta, el log dice **por que** (secreto cambiado, ClientId inexistente, reloj del equipo
desfasado mas de 120s, URL inalcanzable). Si la colmena aparece Offline con el servicio arriba, el
canal local es `\\.\pipe\ecorex-agent`.

## Desinstalar

```powershell
.\uninstall.ps1                 # conserva la boveda (reinstalar no obliga a reconfigurar)
.\uninstall.ps1 -RemoveVault    # borra tambien identidad y secretos
```

## Pendiente

Empaquetar todo esto en un instalador `.exe` firmado (Inno Setup / WiX) que envuelva estos mismos
pasos. Requiere la herramienta y un certificado de firma; hasta entonces, estos scripts SON el
instalador.
