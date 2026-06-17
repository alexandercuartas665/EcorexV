# ADR-0009: Motor de agenda por intervalos (anti-solapamiento) y modo de agenda por asesor

**Fecha:** 2026-06-17
**Estado:** Aceptado
**Corresponde a:** Capa 2 (Nucleo Operativo) - Modulos 2.2/2.3; regla de oro del CLAUDE.md (jamas overbooking)

## Contexto

Los servicios del salon tienen duracion variable, y ademas esa duracion depende del **largo
del cabello** (ServicePriceTier: precio + duracion por largo corto/medio/largo/muy largo). El
motor original era una **grilla de cupos fijos**: `ShiftTemplate.SlotMinutes` (30 por defecto)
generaba slots y la defensa anti-overbooking era un indice `UNIQUE(tenant, recurso, fecha, start_time)`.

Ese candado solo impide dos citas con la **misma hora de inicio**. En el momento en que la
duracion varia, deja pasar solapamientos: un servicio de 45 min a las 3:00 (ocupa 3:00-3:45) y
otra cita a las 3:30 tienen distinto `start_time`, asi que el UNIQUE las acepta -> **overbooking
por cruce de horario**. La defensa era insuficiente para el modelo real de atencion.

Ademas, el negocio necesita que coexistan dos formas de atender: unos asesores trabajan **por
turnos fijos** y otros **por la duracion del servicio** (continuo).

## Decision

1. **Una sola defensa de BD, por SOLAPAMIENTO de intervalos.** Se reemplaza el `UNIQUE(start_time)`
   por un *exclusion constraint* GiST (`ck_appointments_no_overlap`): para citas activas del mismo
   `(tenant_id, resource_id, appointment_date)`, ningun par puede cruzar su intervalo
   `[inicio, inicio + duracion + buffer)`. Requiere la extension `btree_gist` (mezcla igualdad `=`
   en columnas escalares con solapamiento `&&` de rango en el mismo indice). El rango es
   **medio-abierto `[)`**: dos citas pegadas (3:00-3:30 y 3:30-4:00) NO chocan, pero 3:00-3:45 y
   3:30-4:00 SI. El constraint **subsume** al viejo UNIQUE (dos citas a la misma hora siempre se cruzan).
   Se crea por SQL crudo en la migracion porque EF Core no modela EXCLUDE.

2. **El intervalo se computa en SQL** desde `start_time`, `duration_minutes` y `buffer_minutes`
   (no se guarda `end_time` redundante; `duration_minutes` ya da el fin real para pintar el bloque).
   El filtro parcial `status NOT IN ('Cancelled','Rescheduled')` se mantiene: esas citas liberan el cupo.

3. **Buffer por asesor.** `Resource.BufferMinutes` (margen de limpieza/preparacion) se hace
   **snapshot** en `Appointment.BufferMinutes` al reservar y entra en el intervalo del anti-solapamiento.
   Snapshot (no join al recurso) porque el constraint no puede referenciar otra tabla y porque las
   citas ya reservadas deben conservar su margen aunque el asesor cambie su configuracion.

4. **Modo de agenda por asesor.** `Resource.SchedulingMode { SlotGrid, Duration }`. La defensa de BD
   es IDENTICA en ambos modos; lo unico que cambia es **como se ofrecen los horarios**: `SlotGrid`
   ofrece inicios cada `SlotMinutes` (clasico); `Duration` ofrece el proximo hueco donde quepa la
   duracion completa del servicio (continuo). Default `SlotGrid` (compatibilidad: el comportamiento
   previo se conserva para los asesores existentes).

5. **Largo obligatorio para servicios con tarifas por largo.** Un servicio con `PriceTiers` no se
   puede reservar (ni por IA ni por recepcion) hasta conocer el largo (foto clasificada o seleccion
   manual). Con el largo conocido la reserva usa la **duracion y precio del tier**, no los base.
   (Implementacion en la fase F2.)

6. **Traduccion de la violacion.** El servicio de reserva captura `DbUpdateException` con SqlState
   `23505` (unique_violation) **o** `23P01` (exclusion_violation) y devuelve un mensaje amable
   ("ese horario acaba de ocuparse o se cruza con otra cita").

## Fases

- **F1 (este commit):** columnas `buffer_minutes` (appointments, resources) y `scheduling_mode`
  (resources); exclusion constraint + `btree_gist`; snapshot de buffer al reservar; captura de 23P01;
  test de concurrencia que prueba el no-solapamiento, la adyacencia y el buffer (hito critico).
- **F2:** disponibilidad consciente de duracion segun `SchedulingMode`; reserva con duracion/precio
  del tier por largo; gate de largo obligatorio (IA + recepcion); UI de modo/buffer/largo.
- **F3:** vista Dia con bloques proporcionales a la duracion.

## Consecuencias

- El overbooking por solapamiento es **imposible a nivel de BD** para todos los asesores desde F1,
  sin importar el modo. El test de concurrencia lo prueba.
- Cambio de comportamiento intencional: reservas que antes se aceptaban por tener distinto
  `start_time` pero cruzaban duracion ahora se rechazan (era el bug). El mensaje amable lo cubre.
- **Riesgo de migracion:** crear el constraint FALLA si la BD ya tiene citas activas solapadas. Se
  verifico que la BD dev no las tiene. **Antes de aplicar en produccion/Railway hay que confirmar que
  no existan solapamientos previos** (o limpiarlos), o la migracion abortara.
- Brecha temporal de UX entre F1 y F2: la vista Dia (grilla fija) puede ofrecer un cupo que luego el
  constraint rechace; el mensaje amable lo maneja hasta que F2/F3 hagan la disponibilidad consciente
  de la duracion.
- `btree_gist` queda instalada (la migracion Down no la desinstala por si otros objetos la usan).
