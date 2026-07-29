# Agente Conector On-Prem (colmena)

App de escritorio Windows del Agente Conector On-Prem de ECOREX. Modelo **colmena**:
un orquestador local mantiene una conexion SignalR saliente e instancia **sub-agentes efimeros**
por capacidad (Gateway de datos, Archivos, Navegador). La GUI es un **panal de hexagonos** que
monitorea + configura; la ejecucion la hace el orquestador.

Stack (D7, doc 06): **.NET 10 + C#, Windows-first**. WPF para la GUI, Worker Service para el
orquestador, SignalR saliente, Playwright/WebView2 para el navegador (olas siguientes).

## Estructura

```
apps/agent/
  Ecorex.Agent.Gui/           # WPF (net10.0-windows) - la colmena. OLA A (esta).
  libs/Ecorex.Contracts.Agent/# contratos compartidos (net10.0). Sin internals del backend.
```

El agente referencia SOLO `libs/Ecorex.Contracts.Agent`; NUNCA el backend web (apps/backend).

## Olas

- **Ola A (esta): cascara visual "colmena".** Ventana sin borde translucida, panal de hexagonos
  (HexTile con estados Vacio/Lleno/Atendiendo/Error), hexagono Configuracion siempre lleno
  (ClientId + URL del hub + estado + "Probar conexion" stub), sub-agentes vacios, y un modo DEMO
  (mock) para ver el llenado. SIN SignalR real ni ejecucion de sub-agentes.
- **Ola B**: cliente SignalR real + handshake HMAC (ClientId + secreto) -> datos reales en la colmena.
- **Olas C+**: ejecucion real de sub-agentes (Gateway BD, Archivos, Navegador WebView2/MCP),
  allow-list de seguridad, auditoria, instalador/servicio Windows.

## Fuera de alcance de la Ola A (solo hooks/interfaces)
SignalR real, ejecucion de sub-agentes, seguridad (allow-list). Se dejan placeholders.

## Instalador MSI (WiX) - despliegue en la estacion del cliente

El agente se empaqueta como un **MSI self-contained (win-x64)**: la maquina cliente NO necesita
instalar ningun runtime .NET. Ver ADR-0049. El proyecto vive en `apps/agent/installer/`.

### Que instala

- Binarios en `%ProgramFiles%\Ecorex\Agente Colmena` (self-contained: runtime .NET incluido).
- **Servicio de Windows** `EcorexAgent` (LocalSystem, arranque automatico). Se arranca al instalar
  y se detiene/elimina al desinstalar. Es el orquestador de fondo (canal SignalR + sub-agentes).
- **GUI de bandeja**: acceso directo en el Menu Inicio y AUTOSTART (llave Run de la maquina) que la
  arranca minimizada en la bandeja al iniciar sesion (opcion `--tray`).
- Carpeta de config compartida `%ProgramData%\Ecorex\Agent` (donde servicio y GUI leen la MISMA
  identidad cifrada `config.dat`). El instalador la crea/preserva; NO trae ClientId, URL ni secreto.

### Construir el MSI

Requiere el SDK de .NET 10 (el SDK de WiX se restaura solo desde NuGet; no hace falta instalar la
herramienta global `wix`). Desde `apps/agent/installer`:

```powershell
.\build-installer.ps1                 # version por defecto 1.0.0
.\build-installer.ps1 -Version 1.1.0  # otra version
```

El script (idempotente) publica ambos proyectos self-contained, compila el MSI y deja el archivo en:

```
apps/agent/installer/dist/Ecorex-AgenteColmena-<version>.msi   (~58 MB)
```

### Instalar / desinstalar

El MSI es **perMachine** (requiere elevacion):

```powershell
msiexec /i Ecorex-AgenteColmena-1.1.0.msi          # instalar (UI de progreso)
msiexec /i Ecorex-AgenteColmena-1.1.0.msi /qn      # instalar silencioso
msiexec /x Ecorex-AgenteColmena-1.1.0.msi          # desinstalar
```

### Configurar la identidad (ADR-0050)

Dos caminos, sin abrir consolas elevadas a mano:

1. **Desde la colmena (recomendado):** hexagono Configuracion -> ClientId + URL del hub + secreto ->
   Guardar (o Probar). Si la colmena no corre elevada (lo normal, se auto-lanza a la bandeja sin
   elevar), aparece **UN prompt de UAC**; al confirmarlo la identidad se guarda y el servicio reconecta
   solo (vigila `config.dat`). No hay que reiniciar el servicio.

2. **Install desatendido (varios servidores):** pasar la identidad como propiedades del MSI:

   ```powershell
   msiexec /i Ecorex-AgenteColmena-1.1.0.msi /qn `
     CLIENTID=cli_xxx HUBURL=https://app2.bitcode.com.co SECRET=xxx
   ```

   Queda configurado al instalar (una custom action deferida escribe la boveda como SYSTEM). `SECRET`
   no aparece en el log del MSI; aun asi, un secreto en linea de comandos es visible en el arbol de
   procesos mientras corre msiexec, asi que conviene distribuirlo por un canal seguro.

La config sobrevive a un UPGRADE del MSI (el instalador no toca `config.dat`).

### Notas

- Publicacion en CARPETA (no single-file): mas robusto para WPF/WebView2, y el MSI ya comprime todo.
- El **MSI no se firma** (no hay certificado). La firma de codigo (signtool + cert Authenticode) es un
  paso posterior de release; sin ella Windows muestra "editor desconocido". Ver ADR-0049.
