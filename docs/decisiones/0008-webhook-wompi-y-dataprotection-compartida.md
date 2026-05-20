# ADR-0008: Webhook de Wompi idempotente y Data Protection compartida entre apps

**Fecha:** 2026-05-20
**Estado:** Aceptado
**Corresponde a:** Fase 3 de pagos (Super Admin SaaS sec.8/15.4). Complementa ADR-0007.

## Contexto

La confirmacion de pagos de suscripcion llega por **webhook de Wompi** (seccion 15.4): Wompi
firma cada evento y puede reenviarlo. Hay que validar la firma, evitar procesar duplicados y
aplicar el cambio al pago/suscripcion.

Al construirlo aparecio un problema de fondo: la consola **SuperAdmin** cifra los secretos de
Wompi con ASP.NET Data Protection, pero el **Api** (donde vive el webhook) usa por defecto un
**key ring distinto** y no podia descifrarlos ("The payload was invalid"). Api, SuperAdmin y los
futuros Workers necesitan descifrar los mismos secretos.

## Decision

1. **Data Protection compartida en la base de datos.** `AddInfrastructure` configura
   `AddDataProtection().SetApplicationName("CubotTravels").PersistKeysToDbContext<CubotTravelsDbContext>()`.
   Las llaves viven en la tabla `data_protection_keys` (Postgres compartido) con un nombre de
   aplicacion comun, de modo que cualquier app del sistema cifra/descifra los mismos secretos.
   Paquete: `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`.

2. **Webhook idempotente** `POST /webhooks/wompi` (anonimo respecto al JWT; la confianza es la
   firma del evento). El proveedor reenvia eventos: la idempotencia se garantiza por
   `provider_event_id = transaction.id + ":" + timestamp` con indice unico en `wompi_webhook_events`.

3. **Validacion de firma:** `checksum = SHA256( concat(valores de signature.properties bajo "data")
   + timestamp + events_secret )`, comparado en forma insensible a mayusculas. El `events_secret`
   se descifra con la llave compartida.

4. **Aplicacion del evento:** se localiza el `TenantPayment` por `provider_reference`; se mapea el
   estado Wompi (APPROVED/DECLINED/VOIDED/ERROR/PENDING) a `PaymentStatus`; si es aprobado se marca
   `ConfirmedAt`, se **renueva** la suscripcion (extiende el periodo) y se **reactiva** el tenant si
   estaba suspendido/moroso. Si no hay pago con esa referencia, el evento queda en estado
   `NoMatchingPayment` (cola de conciliacion). Todo el evento se guarda crudo para auditoria.

5. **Descifrado resiliente:** al rotar el key ring, los secretos cifrados con la llave anterior
   dejan de poder descifrarse; el codigo lo tolera (muestra "(re-ingresar)" y exige volver a
   guardarlos) en lugar de fallar.

## Pruebas en desarrollo

Wompi no alcanza `localhost`. Para probar sin exposicion publica se envia un evento **auto-firmado**
con el `events_secret` real (curl). Para eventos reales del sandbox se usa un **tunel**
(cloudflared/ngrok) que el operador corre y registra como URL de webhook en el panel de Wompi
(la herramienta de tunel no se instala desde el repo por la regla de no tocar el SO).

## Diferido

- Generacion del checkout/link de pago firmado con el secret de integridad (Fase 3 parte 2).
- Workers de vencimiento/gracia (revisar cortes, marcar mora, suspender) (seccion 15.3).
- Reporte de conciliacion Wompi vs registros internos (seccion 9).

## Consecuencias

- Los secretos cifrados son legibles por todas las apps (requisito para Api/SuperAdmin/Workers).
- Rotar el key ring obliga a re-ingresar secretos una vez (evento conocido y tolerado).
- El recaudo de suscripciones queda confirmable de forma segura (firma) e idempotente (sin pagos
  ni renovaciones duplicadas).
