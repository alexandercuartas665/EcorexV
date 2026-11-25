---
tipo: trampas
tema: formularios
---

# FO10 - Trampas conocidas de formularios

Volver a [[00 - LEEME PRIMERO]].

1. **NOTIFICAR no envia nada.** Solo deja constancia en la tarea. Para avisar de verdad usa los avisos del paso del
   flujo (cerebro de flujos) o un correo del modulo correspondiente.
2. **Regla creada pero no vinculada.** `crear_regla` sola no corre: vinculala (`regla_en_campo`, `agregar_regla_nodo`)
   o usa `regla_al_enviar`.
3. **Regla en desarrollo o documento inactivo.** No corre fuera de la prueba manual. `ver_regla` muestra
   `estado` y `documento_activo`.
4. **Parametros inventados.** Los nombres exactos (camelCase, en ingles: `message`, `titleTemplate`) salen de
   `listar_verbos_regla`. Un nombre desconocido se rechaza.
5. **`CREAR_TAREA` en formulario anonimo sin `assigneeUserId`.** No hay usuario que la reciba: indica el correo
   del responsable.
6. **Tareas duplicadas al reenviar.** Usa `marcaCreadaField` (campo oculto) en `CREAR_TAREA`.
7. **Botones que no hacen nada.** El boton de regla es tipo `Button` (no `ActionButton`, que es otro control sin
   reglas). `COMPLETAR_PASO_FLUJO` solo actua dentro de una tarea con flujo en curso, y en compuertas necesita
   `targetNodeBpmnId`.
8. **"Requerir" que nunca se quita.** `condicion_entre_campos` con efecto requerir no se revierte solo; agrega la
   regla inversa (opcional).
9. **Registro editable + cierre automatico.** Se contradicen: el editable nunca se cierra.
10. **Cambiar `field_code` de un campo con datos.** Rompe registros, reglas, calculos, plantillas y reportes.
11. **Codigo de campo de otro formulario en un parametro.** Los parametros `codigo_de_campo` se validan contra el
    formulario de la regla: usa los codigos de `get_form` de ESE formulario.
12. **Eliminar una regla que ya corrio.** No se puede (el historial es inmutable): `estado_regla` inactiva.
13. **Probar con datos reales.** `create_record`, envios y enlaces publicos crean registros y tareas de verdad:
    avisa al usuario y marca los datos de prueba.
