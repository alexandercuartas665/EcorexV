# ADR-0203: MCP ver_conector, edicion parcial de guardar_conector y tope de tiempo de la importacion REST

- Estado: Aceptado
- Fecha: 2026-11-23
- Modulo: Contenedor de datos (000730) en el servidor MCP personal de tableros
- Relacionados: ADR-0194 (MCP crea contenedores de datos, "conectores sin secreto"), ADR-0201 (rellenar_columna),
  ADR-0058/0059 (importador REST server-direct)

## Contexto

La sesion ECOREX.mcp administra por MCP el contenedor "SECOP II - Procesos" (tenant BITCODE). Su importacion diaria
de las 5 am falla desde el 22-nov con "Tiempo de espera agotado al llamar al API": el conector "SECOP II - Recientes"
(Socrata p6dx-8zbt) pide paginas sin filtro ajustado que tardan ~32 s, y cada peticion tiene un tope de 30 s. El
arreglo (endpoint con `$order=fecha_de_publicacion_del DESC&$limit=5000`, 1.6 s) ya esta validado contra el API
publico, pero el MCP no podia aplicarlo con seguridad:

1. `listar_conectores` solo da nombre, tipo, destino y activo. No habia forma de LEER el endpoint, la paginacion ni
   el mapeo de un conector.
2. `guardar_conector` en edicion REEMPLAZABA todo: omitir `mapeo_json` o `destino` los borraba, y los headers
   estaticos y la config del intercambio de token (que el MCP no recibe) se perdian siempre. Cambiar solo el
   endpoint dejaba el conector sin mapeo ni tabla destino.
3. Un conector mal configurado puede colgar la corrida horas: 30 s por pagina y hasta 1000 paginas por defecto.

## Decision

1. **Herramienta `ver_conector(contenedor, conector)`** (solo lectura, permiso View del modulo `contenedor-datos`,
   tenant-scoped por el filtro global). Devuelve tipo, tabla destino (nombre e id), activo, si tiene credenciales y:
   - api: endpoint, metodo, auth, NOMBRES de los headers (sin valores) y si hay intercambio de token configurado;
   - bd: motor, host, puerto, base, usuario y query;
   - siempre: `mapeo` (JSON parseado) y `mapeo_json` (texto, para reenviarlo tal cual).
   **Nunca expone secretos**: las credenciales siguen cifradas y solo se informa si existen; de los headers solo van
   los nombres; `ConnectorSecretMask` enmascara (`***`) el VALOR de parametros del endpoint con nombre sensible
   (token, secret, password, apikey, signature, credential y los que terminan en key/pwd/pass/sig/auth/session) y la
   userinfo `usuario:clave@`. Los parametros normales (`$order`, `$limit`, filtros) quedan legibles.
2. **`guardar_conector` edita solo lo enviado**. Con `conector` presente, cada campo omitido conserva su valor
   actual (nombre, tipo, endpoint, metodo, auth, datos de BD, query, mapeo, tabla destino, activo), y los headers y
   el intercambio de token se conservan siempre. `nombre` y `tipo` pasan a ser obligatorios solo al crear. Un
   endpoint que trae `***` (copiado de `ver_conector`) se rechaza para no romper la fuente.
3. **Tope de 20 minutos por corrida** en el importador REST server-direct (`ApiImportService.RunBudget`): se
   comprueba antes de pedir cada pagina siguiente; al pasarlo se detiene, conserva lo ya importado (como el tope de
   50.000 filas) y deja el motivo en el detalle de la corrida.

## Opciones consideradas

- **Ampliar `listar_conectores` con toda la config**: mezcla un listado barato con un detalle pesado (mapeos de
  cientos de campos). Descartada: listar sigue siendo liviano y el detalle va por conector.
- **Devolver los headers completos** (el contrato dice que no son secretos): en la practica hay APIs con la clave en
  un header estatico. Descartada: solo nombres; el valor se ve y se edita en la pantalla.
- **Tope de tiempo configurable por conector**: mas flexible, pero requiere UI y migracion. Se deja fijo; si una
  fuente legitima necesita mas, se revisa.

## Consecuencias

- 157 herramientas en el MCP de tableros (24 de contenedores). Sin migracion ni configuracion.
- El MCP ya puede corregir un conector API sin riesgo de borrar su mapeo: `ver_conector` -> `guardar_conector` con
  solo el campo a cambiar.
- Cambio de comportamiento (intencional): `guardar_conector` en edicion ya no limpia los campos omitidos. Para
  vaciar uno, se envia explicitamente vacio.
- Pruebas: `ConnectorSecretMaskTests` (unitarias) y
  `McpTablerosTests.VerConector_SinSecretos_YEditarSoloEndpoint_ConservaMapeoDestinoYHeaders` (dual).
