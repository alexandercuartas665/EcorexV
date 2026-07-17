# ADR-0040: La credencial de la fuente VIAJA por el canal (opcion a); el TLS estricto pasa a bloqueante

- Estado: aceptada
- Fecha: 2026-07-16
- Revierte a: la eleccion de la **opcion (b)** del doc 01 s7 del capitulo "Agente Conector On-Prem"
  ("credencial gestionada por el agente"), que era lo construido hasta la Ola C.
- Relacionada con: ADR-0039 (empaque: el Servicio es dueno de la boveda), doc 02 (protocolo),
  doc 04 s4-s5 (cliente), doc 05 Ola 6 (endurecimiento).

## Contexto

El doc 01 s7 planteaba dos politicas para la credencial de la base de datos del CLIENTE (la fuente de
la LAN que el Gateway consulta):

- **(a)** el servidor la manda en el `FetchRequest`;
- **(b)** vive SOLO en el agente y el servidor manda un `secretRef`. El propio doc la llamaba **"mas
  segura"**, y fue la que se implemento: `GatewaySourceStore` guarda la cadena de conexion en la
  boveda del agente y **nunca sale**.

Al construir el boton "Actualizar datos" del modulo web (2026-07-16), el usuario pidio poder
**configurar la conexion a la base desde la web**: host, puerto, base, usuario y credencial, que es
justo lo que `DataConnector` ya guarda cifrado con `ISecretProtector`. Con la opcion (b) esos campos
del conector quedaban muertos para el camino del agente: la web preguntaba una credencial que nadie
usaba, y la de verdad habia que configurarla a mano en cada colmena.

Se le presentaron las tres opciones (a / b / mixta con `secretRef`) con su consecuencia. **Eligio (a)**,
a sabiendas de que la contrasena viaja.

## Decisiones

### 1. El servidor manda la credencial de la fuente

`ConnectorSpec` gana `Secret`: el servidor descifra `DataConnector.CredentialsEncrypted` y lo incluye
en el `FetchRequest`. El agente arma la cadena de conexion con lo que le mandan
(`GatewayExecutor.BuildConnectionString`). Toda la administracion queda en la web, que era el objetivo.

### 2. La opcion (b) NO se retira: es el respaldo

Si el `ConnectorSpec` llega **sin** `Secret`, el agente cae a su cadena LOCAL (`GatewaySourceStore`),
que es como funcionaba la Ola C. Asi:

- un agente ya configurado a mano sigue trabajando sin tocar nada;
- un cliente que NO quiera que su contrasena salga de su red puede seguir con (b), conector por
  conector, simplemente no guardando la credencial en la web.

La eleccion, entonces, es **por conector** y no global. Es la razon de conservar las dos rutas en vez
de borrar la vieja.

### 3. **El TLS estricto pasa de "pendiente" a BLOQUEANTE**

Es la consecuencia directa, y la parte importante de esta ADR:

- Con (b) el canal transportaba ordenes y filas. Feo si iba en claro, pero acotado.
- Con (a) el canal transporta **la contrasena de la base de datos del cliente**.

Hoy el agente **acepta `http://` sin protestar** (doc 05 Ola 6: "TLS estricto" seguia pendiente; la
validacion de certificados de .NET si esta activa y nadie la desactiva, pero no se exige el esquema).
Eso deja de ser aceptable: **antes de que esto toque un cliente real, el hub debe ser `https`/`wss` y
el agente debe rechazar lo que no lo sea**. El usuario lo confirma (2026-07-16): *"la contrasena sigue
pasando por el tunel... luego el servidor no va a trabajar como HTTP sino como HTTPS"*.

Mientras tanto, en dev, se acepta `http://localhost` a proposito.

## Consecuencias

- Se cierra el hueco de administracion: el operador configura la fuente en la web y el agente no
  necesita configuracion manual por equipo.
- **La superficie crece**: quien pueda leer el trafico del canal, o quien comprometa el servidor, ve
  la credencial de la BD del cliente. Con (b) el servidor comprometido podia pedir consultas (acotadas
  por `QueryGuard` a solo-SELECT) pero **no** obtener la contrasena.
- `QueryGuard` (solo-SELECT) sigue siendo la defensa que impide escribir en la base del cliente, venga
  la credencial de donde venga.
- El doc 01 s7 y el doc 04 s4 quedan desactualizados en este punto: describen (b) como lo elegido.

## Deudas / pendientes de implementacion

- [ ] **TLS estricto (BLOQUEANTE para produccion)**: exigir `https`/`wss` en la URL del hub y rechazar
      lo demas, salvo `localhost` en Development. Prueba con certificado invalido (doc 05 Ola 6).
- [ ] `TrustServerCertificate=True` esta fijo al armar la cadena de SQL Server (las fuentes on-prem
      suelen tener certificado autofirmado). Aplica a la BD de la LAN, no al canal; deberia ser
      configurable por conector.
- [ ] Reflejar el cambio en los docs 01 s7, 02 s5 y 04 s4-s5 del vault.
