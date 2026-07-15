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
SignalR real, ejecucion de sub-agentes, seguridad (allow-list), instalador. Se dejan placeholders.
