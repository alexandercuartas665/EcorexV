# ADR-0086: Modulo movil de tablero (consulta + cambio de estado + escaneo de codigo de barras)

- Estado: Aceptado (Opcion A para tareas SIN flujo + Opcion B/C para tareas CON flujo, ver abajo). Se itera lo demas.
- Fecha: 2026-09-02
- Deciden: Alexander (producto) + sesion de codigo

> DECISION (2026-09-02): Opcion A elegida por el usuario para actividades planas. El escaneo muestra una
> TARJETA DE CONFIRMACION y, al confirmar, mueve la actividad a la siguiente columna del tablero
> (MoveTaskAsync). Ahi correran mas adelante las reglas del avance.
>
> DECISION (2026-09-02, iteracion): para actividades GOBERNADAS POR UN FLUJO, el escaneo NO avanza columna:
> abre EN UN MODAL el PUNTO donde esta el proceso (el formulario del paso PENDIENTE, "solo ese punto") para
> que el operario CONCRETE la accion desde el celular. Si el paso lleva a una COMPUERTA EXCLUSIVA, el propio
> formulario ofrece las rutas (ApprovalOptions) y al enviar el flujo avanza (mueve la tarjeta a la columna
> del siguiente nodo). Reusa la MISMA maquinaria del detalle de tarea (GetTaskStepFormsAsync + paso PENDING +
> DynamicFormRenderer), sin duplicar reglas de flujo. (v0.15.144)

## Contexto

Se necesita una vista **pensada para celular** (no una APK: un modulo web mas del sistema, Blazor Server)
para operar un tablero de actividades en piso/campo: ver las actividades por estado, **cambiar el estado con
toques grandes**, y sobre todo un **lector de codigo de barras** que haga **saltar la actividad de un paso al
siguiente**. El operario escanea el codigo de la orden y la actividad avanza sin navegar menus.

Restricciones: reusar el nucleo existente (tableros de actividades, ADR-0020/0038), multi-tenant intacto, sin
dependencias externas nuevas si se puede, y funcionar en el navegador del telefono (Android Chrome) sobre
HTTPS (prod) / localhost (dev), que son contextos seguros para la camara.

## Decision

Un modulo Blazor nuevo `/movil/tablero` (policy `TenantMember`), **mobile-first** (una columna, targets
grandes, barra inferior con el boton de escaneo), que **reusa `IActivityBoardService`** (sin modelo de datos
nuevo):

- **Consulta**: `ListBoardsAsync` (selector) + `GetBoardDetailAsync` (columnas=estados con sus cards).
- **Cambiar estado**: tocar una card abre una hoja inferior con los estados como botones grandes ->
  `MoveTaskAsync(taskId, columnaDestino, ...)` (columna del tablero = estado).
- **Escaneo**: boton "Escanear codigo" -> camara con la **API nativa `BarcodeDetector`** (getUserMedia,
  camara trasera) via un JS chico (`wwwroot/js/movil-scan.js`); al detectar el primer codigo invoca de vuelta
  al componente. Semantica: el codigo trae el **numero de la actividad** (T00058); se localiza en el tablero y
  se **avanza al SIGUIENTE estado** (columna por SortOrder+1). Si no hay soporte de camara, cae a **entrada
  manual** del numero.

## Opciones consideradas

### Escaner: API nativa BarcodeDetector vs libreria JS (ZXing/Quagga)
| Dimension | BarcodeDetector nativo | Libreria JS (ZXing/Quagga) |
|-----------|------------------------|----------------------------|
| Complejidad | Baja (sin bundle) | Media (bundle + init) |
| Peso/CSP | Cero JS externo | ~200KB+; hosting/CSP |
| Cobertura | Android Chrome si; iOS Safari NO | Universal |
| Precision | Buena (nativa) | Buena |

Elegido **BarcodeDetector nativo** con fallback manual: cero dependencias, y el caso de uso primario es
Android en piso. Si mas adelante se requiere iOS/escritorio, se agrega ZXing como segunda estrategia detras
de la misma interfaz JS (`ecorexScan.start`).

### Datos: reusar tableros de actividades vs endpoint movil nuevo
Reusar `IActivityBoardService` (mismo aislamiento por tenant, mismas reglas de mover/cerrar). Un endpoint
movil nuevo seria duplicar reglas. Elegido **reusar**.

### Accion del escaneo: avanzar-al-siguiente vs abrir-selector
Se eligio **avanzar al siguiente estado** (lo pedido: "saltar de un paso a otro"), con toast del cambio. El
selector manual queda disponible tocando la card (cubre correcciones y saltos no lineales).

## Consecuencias

- Mas facil: operar en campo con una mano; el escaneo avanza sin navegar; cero infraestructura nueva.
- Mas dificil / a vigilar: iOS Safari no trae BarcodeDetector (fallback manual); el "siguiente estado" asume
  columnas ordenadas linealmente (no ramas de flujo); mover a una columna final con motivos de cierre
  configurados puede requerir el motivo (hoy se muestra el error del servicio y se itera).

## Pendientes / a definir con el usuario (iteracion)

1. Que trae exactamente el codigo (numero de actividad T#, id, o un codigo de la OT). Hoy: numero.
2. [RESUELTO v0.15.144] "Paso" = columna (tareas planas) O **nodo del flujo** (tareas con flujo: se abre el
   paso PENDIENTE en el modal y se concreta ahi, incl. compuerta exclusiva).
3. Motivo de cierre al pasar a una columna final (pedirlo en la hoja movil). Solo aplica a tareas planas.
4. Ubicacion en el menu (nodo por tenant), alcance de permiso (operarios), y si filtra por asignado/mias.
5. [RESUELTO v0.15.143] Confirmacion antes de avanzar por escaneo (tarjeta de confirmacion en tareas planas).
6. Paso de flujo SIN formulario (nodo Task puro / compuerta sin form previo): hoy cae al avance por columna;
   pendiente exponer boton "Completar paso" / rutas via IWorkflowInboxService si aparece el caso.
7. Reglas de negocio en el avance (tarjeta de confirmacion): hook dejado en ConfirmAdvanceAsync.

## Action Items

1. [x] Primera vista `/movil/tablero`: selector + estados con cards + hoja de cambio de estado + boton y hoja
       de escaneo (camara nativa + fallback manual). Verificado el render en dev (mobile 375x812).
2. [ ] Recoger ajustes del usuario sobre los pendientes 1-5.
3. [ ] Nodo de menu del modulo por tenant + permiso.
4. [ ] (si aplica) Escaneo que avanza el FLUJO (no solo la columna).
