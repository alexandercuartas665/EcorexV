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

### 3. El transporte cifrado lo da el despliegue; el "TLS estricto" es un guardrail del cliente

> **Corregido 2026-07-17.** La version original de esta seccion decia que el TLS estricto "pasa a
> BLOQUEANTE" y que "la contrasena viaja en claro cada N minutos". Eso mezclaba dos cosas distintas y
> exageraba el riesgo en produccion. El usuario lo senalo: si el sistema se despliega detras de HTTPS,
> el canal ya va cifrado. Tiene razon. La redaccion de abajo es la correcta.

Hay que separar dos cosas:

- **Cifrado del canal (lo importante):** lo resuelve el DESPLIEGUE. Si el hub de produccion se sirve
  por HTTPS, el agente conecta por `wss://` y la credencial va cifrada en el tramo que cruza internet.
  El grueso de la preocupacion (que un tercero lea la contrasena en transito) queda cubierto por
  desplegar detras de HTTPS, sin tocar el agente. La validacion de certificados de .NET ademas esta
  activa y nadie la desactiva, asi que **un certificado invalido YA se rechaza**.
- **"TLS estricto" (el guardrail que falta):** que el AGENTE se **niegue** a usar una URL que no sea
  TLS. Hoy el agente acepta `http://` sin protestar. "Produccion es HTTPS" es un hecho sobre el
  SERVIDOR, no una garantia que imponga el CLIENTE.

Por tanto el valor real del "estricto" queda acotado a dos escenarios de borde, no al caso normal:

- **Error de configuracion:** alguien instala un agente con `http://` (un typo, copiar de dev, un proxy
  interno que corta el TLS antes de tiempo). Conecta feliz y la clave viaja en claro dentro de esa red,
  sin que nada avise.
- **Downgrade:** un atacante en la ruta que logre influir en la URL fuerza un canal en claro; la
  defensa es que el cliente diga "no".

Es **defensa en profundidad barata** (un tirante sobre el cinturon que ya es el HTTPS del despliegue),
no una proteccion de carga ni un bloqueante de release. Se implementa cuando convenga; el orden
sensato es: hub `https`/`wss` en produccion + el agente configurado con la URL `https` (que cubre el
caso real) y, como endurecimiento, que el agente **rechace** esquemas no-TLS salvo `localhost` en dev.

Mientras tanto, en dev, se acepta `http://localhost` a proposito.

## Consecuencias

- Se cierra el hueco de administracion: el operador configura la fuente en la web y el agente no
  necesita configuracion manual por equipo.
- **La superficie crece, pero acotada por el transporte**: quien pueda leer el trafico del canal EN
  CLARO ve la credencial. Con el hub por HTTPS/WSS eso no es leible en transito; el riesgo residual es
  un agente mal configurado a `http://` (ver punto 3). Aparte, quien **comprometa el servidor** ve la
  credencial descifrada; contra eso el TLS no ayuda (es el server el que descifra), y con (b) el
  servidor comprometido podia pedir consultas -acotadas por `QueryGuard` a solo-SELECT- pero **no**
  obtener la contrasena. Ese sigue siendo el trade-off de elegir (a).
- `QueryGuard` (solo-SELECT) sigue siendo la defensa que impide escribir en la base del cliente, venga
  la credencial de donde venga.
- El doc 01 s7 y el doc 04 s4 quedan desactualizados en este punto: describen (b) como lo elegido.

## Deudas / pendientes de implementacion

- [ ] **TLS estricto (guardrail del cliente, NO bloqueante si prod es HTTPS)**: que el agente exija
      `https`/`wss` en la URL del hub y rechace lo demas, salvo `localhost` en Development. El cifrado
      del canal ya lo da el despliegue detras de HTTPS; esto es defensa en profundidad contra
      configuracion erronea/downgrade. Incluye la prueba con certificado invalido (doc 05 Ola 6).
- [ ] `TrustServerCertificate=True` esta fijo al armar la cadena de SQL Server (las fuentes on-prem
      suelen tener certificado autofirmado). Aplica a la BD de la LAN, no al canal; deberia ser
      configurable por conector.
- [ ] Reflejar el cambio en los docs 01 s7, 02 s5 y 04 s4-s5 del vault.
