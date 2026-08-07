# ADR-0063: Identidad del agente reconfigurable por MSI + blindaje del secreto vacio

- Estado: Aceptada
- Fecha: 2026-08-06
- Contexto tecnico: Agente Conector On-Prem "Colmena" (MSI WiX + GUI WPF + Servicio).
- Relacionada: ADR-0049 (instalador MSI), ADR-0050 (identidad por UAC y propiedades MSI), ADR-0039 (boveda machine-scope).

## Contexto

La identidad del agente (ClientId + hub + secreto) vive en un vault machine-scope
`%ProgramData%\Ecorex\Agent\config.dat` (DPAPI de MAQUINA, ACL SYSTEM+Admins). Cambiarla EXIGE
elevacion. Se detectaron dos fallas en el despliegue real (SOLDARCO):

1. **El MSI solo aplicaba la identidad en instalacion NUEVA.** La custom action deferida
   `EcorexConfigureIdentity` tenia la condicion `CLIENTID AND HUBURL AND NOT Installed`. Reinstalar
   con propiedades sobre un agente YA instalado (reinstall/modify) NO re-aplicaba la identidad: el
   vault viejo se quedaba y, tras rotar el secreto en el servidor, el handshake fallaba para siempre
   con `401 {"error":"Firma invalida."}`.

2. **Un SECRET vacio corrompia el vault.** Con `SECRET` vacio, el comando formateado del MSI
   terminaba en un argumento vacio `""`. MSI corrompe ese ultimo argumento vacio y lo reemplaza por
   basura (`CURRENTDIRECTORY="C:\WINDOWS\system32"`), que el exe escribia como si fuera el secreto.
   Resultado: el secreto valido se destruia y el handshake pasaba a `Firma invalida` de forma
   permanente. (Confirmado en la CustomActionData del log verboso del MSI.)

## Decision

1. **CA de identidad reconfigurable.** Se cambia la condicion a solo `CLIENTID AND HUBURL` (cubre
   install, reinstall y modify). El guard sigue vivo: sin esas props la condicion es falsa y el vault
   existente no se toca. La CA sigue deferida + no-impersonada (SYSTEM) y la escritura es idempotente.

2. **Centinela `__KEEP__` para el secreto vacio.** Un `SetProperty` fuerza `SECRET=__KEEP__` cuando
   no vino SECRET (`CLIENTID AND HUBURL AND NOT SECRET`), ANTES de armar el comando. Asi el 4o
   argumento SIEMPRE es no vacio y no se corrompe. El exe (`AgentIdentity.Merge`, probado) interpreta
   `__KEEP__` (o vacio/omitido) como "conserva el secreto actual"; solo un secreto no vacio y distinto
   del centinela rota la credencial. Para re-fijar ClientId/hub sin exponer el secreto: OMITIR SECRET.

3. **Verbo de diagnostico `--show-identity` / `--whoami`.** Imprime la identidad ACTIVA del vault
   (ClientId + hub + si hay secreto, NUNCA el valor). La GUI es WinExe: se engancha a la consola del
   proceso padre para que la salida sea visible desde una terminal; si no hay, muestra un dialogo.

Nota: la GUI ya elevaba de verdad (Verb=runas) y ya no miente en el log (ADR-0050, commit aa88527);
este ADR no cambia ese flujo.

## Consecuencias

- Re-fijar la identidad por MSI ahora funciona sobre un agente ya instalado (misma version incluida).
  Validado en esta maquina: config.dat se reescribe en cada reinstall y `--show-identity` confirma.
- Distribuir la identidad SIN secreto (conservando el actual) es seguro: OMITIR SECRET. Pasar
  `SECRET=` vacio explicito tambien es seguro ahora (el centinela lo neutraliza).
- Riesgo despreciable: un secreto real igual al literal `__KEEP__` se conservaria en vez de rotarse.
- El secreto se escribe elevado (CA SYSTEM) o por UAC desde la GUI; nunca por un proceso no-admin.
