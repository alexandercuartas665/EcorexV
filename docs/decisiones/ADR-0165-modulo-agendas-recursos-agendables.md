# ADR-0165: Modulo de Agendas generico (recursos agendables, agendas base y agenda del recurso)

**Estado:** Aceptado (olas 1 y 2 en v0.16.313; olas 3 y 4 en v0.16.314; ola 5 en v0.16.315; ola 6 propuesta)
**Fecha:** 2026-11-12
**Relacionado:** proyecto hermano Visal (modulos "Agendas base", "Agendas de profesionales", "Calendario",
"Novedades de profesionales"); ADR-0106 (calendario operativo del tenant); CLAUDE.md seccion 7 (se elimino del
backbone la agenda del dominio belleza).

## Contexto

Muchos clientes de ECOREX gestionan agendas: necesitan separar turnos de atencion y que un agente los programe.
El proyecto hermano Visal ya resolvio el caso para medicos con un proceso en dos pasos: una agenda BASE
(plantilla semanal independiente) que se IMPORTA (copia) a cada profesional, mas novedades (vacaciones,
incapacidad) y un calendario de festivos.

ECOREX no es solo para medicos: la agenda puede ser de una persona, una sala, una maquina, un vehiculo o una
linea de atencion. Y el backbone del que se clono ECOREX traia una agenda del dominio belleza (citas de salon,
asesores, no-show) que se ELIMINO a proposito (CLAUDE.md seccion 7). Hoy no queda codigo de ese dominio; la unica
entidad "Cita" del repo es la agenda del Gestor de Clientes (000740), una funcion viva del CRM que no se toca.

## Decision

1. **Modulo nuevo y generico**, construido desde cero copiando el PROCESO de Visal (no su codigo): agenda base ->
   importar al recurso -> editar la copia -> novedades -> disponibilidad.
2. **"Profesional" pasa a "recurso agendable"**, con TIPOS definidos por cada tenant (`AgendaTipoRecurso`: nombre,
   icono, color y a que se vincula). El sistema sugiere Persona, Grupo, Espacio, Equipo, Vehiculo y Punto de
   atencion. Un tipo "Persona" se vincula a un usuario del tenant; el resto son recursos libres.
3. **Cupo en paralelo por franja**: cada franja tiene dia, inicio, fin, bloque (minutos) y cupo (turnos simultaneos
   por bloque). Cupos de la franja = bloques completos x cupo. Sirve para salas con aforo o lineas con varios
   asesores.
4. **Vigencias**: la agenda propia del recurso es una lista de `AgendaRecursoVigencia` (desde/hasta). Para una fecha
   manda la de inicio mas reciente que la cubre, asi un "horario de temporada" se monta encima del normal sin
   borrarlo. La plantilla origen queda solo como traza: borrarla no toca las copias.
5. **Disponibilidad** = franjas de la vigencia que aplica - dia cerrado por el CALENDARIO OPERATIVO del tenant
   (reutiliza `TenantOperatingDays` y su precarga de festivos de Colombia, ADR-0106) - bloques tocados por las
   novedades del recurso (dia completo o tramo horario). Calculo puro en `AgendaCalculo` (testeable sin BD).
6. **Menu y permisos**: seccion "Agendas" con "Recursos agendables" (`agendas/recursos`, 001683) y "Agendas base"
   (`agendas/base`, 001684); el Route es el ModuleKey de la matriz de roles. Se siembra con
   `ECOREX_MENU_AGENDAS=true` (Standard + Demo) o `ECOREX_MENU_AGENDAS_TENANT=<nombre>` (un solo tenant).
7. Reglas del proyecto: tenant-scoped por filtro global, borrado logico de los agregados (tipo, recurso, plantilla,
   vigencia, novedad), franjas reemplazadas en bloque dentro de una transaccion, concurrencia optimista
   (`Version`) en recurso y plantilla, fechas en la zona horaria del tenant. Migracion aditiva dual `AddAgendas`.

## Opciones consideradas

| Opcion | Pro | Contra |
|---|---|---|
| A. Copiar el modulo de Visal tal cual (profesional) | Rapido | Solo sirve a personas; no cubre salas, maquinas ni lineas |
| B. Reusar la agenda del Gestor de Clientes (Cita) | Ya existe | Es una agenda de eventos del CRM, sin plantillas ni cupos |
| **C. Modulo generico de recursos (elegida)** | Cubre cualquier entidad; el proceso de Visal se conserva | Mas piezas que configurar (tipos de recurso) |

## Olas

1. (hecha) Tipos de recurso, recursos agendables, agendas base.
2. (hecha) Agenda del recurso (importar, vigencias, editar), novedades, disponibilidad semanal con el calendario.
3. (hecha, v0.16.314) Servicios agendables: duracion, anticipacion minima (minutos) y maxima (dias), "el agente puede
   reservar" y REQUISITOS: cada requisito se cumple con UNO de sus recursos elegibles libre todo el turno; varios
   requisitos = varios recursos a la vez (p.ej. Linea + Inspector). Los elegibles se guardan como JSON de ids,
   validados contra el tenant al guardar y filtrados por el filtro global al calcular.
4. (hecha, v0.16.314) Turnos (`AgendaTurno` + `AgendaTurnoRecurso`): separar sobre los huecos libres, reprogramar,
   confirmar, atendido, no asistio y cancelar (con motivo; libera el cupo). Fecha y horas locales + instante UTC.
   Historial legible en el turno.
   - Reglas del hueco: empieza en el inicio de un bloque de la franja de CADA recurso asignado (si los bloques
     difieren, solo valen los inicios comunes), termina dentro de la franja y ocupa todos los bloques que toca; en
     cada bloque caben tantos turnos como el cupo. Se prefiere el recurso menos ocupado del dia.
   - Festivos/dias no operativos: el calendario de la ENTIDAD (TenantOperatingDays, configuracion de la entidad);
     el modulo no tiene calendario propio.
   - No entregar el mismo cupo dos veces: reservar/reprogramar tocan la fila de cada recurso asignado dentro de la
     transaccion; su Version (token de concurrencia) hace fallar a la segunda reserva simultanea ("ese horario se
     acaba de ocupar").
   - Novedad sobre turnos ya tomados: se guarda, AVISA cuantos turnos quedaron dentro y la lista de Turnos los marca
     "Choca con: ..." (tambien si el calendario cierra el dia despues) para reprogramarlos. No se mueven solos.
   - Un recurso o servicio con turnos proximos no se puede quitar.
5. (hecha, v0.16.315) Integracion con el resto de ECOREX:
   - TAREA por turno: el servicio elige un CONCEPTO (opcional); cada turno crea una tarea de ese concepto via
     ITaskItemService (hereda tablero, columna, flujo y formulario), con inicio/vencimiento = horas del turno,
     contacto del Directorio, conversacion de origen y asignada al usuario del primer recurso tipo persona. Se
     crea DESPUES de confirmar el turno y es best-effort (un fallo queda en el historial, no deshace la reserva).
     Reprogramar actualiza las fechas y comenta; cancelar comenta y ARCHIVA la tarea (la maquina de estados no
     deja cerrar una tarea pendiente, y archivar no la marca como trabajo hecho).
   - Directorio: el turno se liga a un contacto (persona o empresa) con el selector del Directorio; sin telefono
     se toma el de la ficha. El agente lo liga solo por los ultimos 10 digitos del telefono de la conversacion.
   - Agente (grupo de herramientas "agendas", encendido por defecto, se apaga por agente): servicios_agendables,
     consultar_disponibilidad, reservar_turno, turnos_del_cliente, reprogramar_turno y cancelar_turno. Solo
     servicios con "el agente puede reservarlo" y recursos con "el agente puede agendar"; el telefono sale de la
     conversacion (no del modelo); solo toca turnos de ESE cliente; no duplica la misma reserva; reservar/
     reprogramar/cancelar cuentan como cierre (red de seguridad ADR-0154) y marcan la tarea como tocada para la
     alerta de cierre (ADR-0163). Sin huecos en la fecha pedida devuelve 'siguientes' para que no invente horarios.
   - Vista Calendario de la semana en Turnos (dias x horas, color por estado, marca de conflicto).
5. (propuesta) Herramientas del agente: consultar_disponibilidad, reservar_turno, turnos_del_cliente, reprogramar,
   cancelar; recordatorio y "no asistio". `AgendaRecurso.AgenteHabilitado` ya existe para gobernarlo.
6. (propuesta) Reserva publica por enlace.

## Consecuencias

- Cada tenant puede montar agendas de cualquier cosa sin codigo, con el mismo proceso que ya usa Visal.
- La disponibilidad descuenta los turnos tomados (ola 4); la ficha del recurso muestra "N tomados".
- Pendiente: reserva publica por enlace (ola 6), aplicar el cambio de una agenda base a sus copias, franjas que
  cruzan la medianoche, festivos con excepcion por recurso, recordatorio automatico antes del turno.

## Verificacion

- `AgendaCalculoTests` (unitarias): cupos, validacion de franjas, vigencia que aplica, novedades parciales, festivo.
- `AgendasTests` matriz dual PG + SQL Server 8/8: importar como copia independiente, semana con festivo y novedad,
  validaciones, concurrencia optimista, duplicar, tipo en uso, AISLAMIENTO cross-tenant.
- E2E en dev (5234, AGROMETALICAS): tipos sugeridos -> agenda base -> recurso Persona -> importar -> novedad parcial
  -> disponibilidad (viernes 8 de 16 cupos por la capacitacion de la tarde).
- Olas 3 y 4: `AgendaTurnosCalculoTests` (unitarias) y `AgendaTurnosTests` dual PG + SQL Server (separar hasta agotar,
  reprogramar, cancelar, festivo, anticipacion, novedad sobre turno tomado, aislamiento). Simulacion por Chrome con un
  consultorio medico y un CDA (linea con 2 cupos + 2 inspectores).
- Ola 5: `AgendaTurnosTests` dual (tarea por turno ligada al Directorio y al dia con reprogramar/cancelar; agente
  consulta/reserva/reprograma/cancela solo lo de su cliente, nombre tolerante, 'siguientes'). E2E con SARA por el
  emulador de canal: la primera corrida destapo que el modelo usaba 'fecha' (no 'desde'), escribia el servicio
  distinto y, ante vacio, INVENTO horarios; corregido (alias, coincidencia tolerante, 'siguientes', regla en la
  descripcion). Segunda corrida: ofrecio horas reales y reservo el turno con su tarea T00101 ligada al contacto.
