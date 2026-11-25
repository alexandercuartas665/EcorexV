---
tipo: trampas
tema: flujos
---

# F09 - Trampas conocidas de flujos

Volver a [[00 - LEEME PRIMERO]].

1. **Dos salidas en una tarea = paralelo.** Un nodo que no es compuerta sigue TODAS sus salidas. Si querias una
   decision, mete una compuerta. `ver_flujo` lo avisa ("EN PARALELO").
2. **Compuerta automatica sin nombres en sus salidas.** El paso anterior no muestra opciones y el flujo sigue
   siempre por la ruta por defecto. Pon `nombre_salida` a cada salida. Aviso: "SIN NOMBRE".
3. **Condicion que no coincide con el nombre.** `approval == 'Aprobado'` NO coincide con la opcion "Aprobada".
   Copia el nombre exacto de la salida (mayusculas no importan, las letras si).
4. **Condiciones con otra sintaxis.** Solo `approval == 'X'` o `approval != 'X'`. `monto > 1000` o
   `form.campo == 'x'` nunca se cumplen (fail-closed): el flujo se iria siempre por la ruta por defecto.
5. **Mas de una salida sin condicion en una compuerta automatica.** Solo una puede ser la ruta por defecto; el
   motor toma la primera que encuentra. Aviso: "se resuelve SOLA".
6. **Decision humana sin encargado.** Una compuerta con `decision_humana_nodo` y origen `politica` sin cargo no le
   llega a nadie. Asigna cargo u otro origen.
7. **Insertar un nodo entre dos que no estan conectados.** `agregar_nodo` con `desde_nodo_id`/`hasta_nodo_id`
   conecta igual, pero si no habia conexion directa no quita nada: revisa que no queden flechas sobrantes.
8. **Usar ids viejos despues de `nueva_version_flujo`.** La version borrador tiene ids NUEVOS. Usar los viejos da
   "solo en borrador" o cambia la version publicada (los cambios en vivo si aplican sobre ella).
9. **Publicar con `forzar` sin preguntar.** Los avisos son flujos rotos. Muestraselos al usuario.
10. **Enganchar un flujo sin publicar.** El concepto queda apuntando pero las tareas no arrancan nada hasta
    publicar. La respuesta lo avisa.
11. **Concepto de cierre en una columna que no es de cierre.** Primero `tablero_destino_nodo` con una columna de
    cierre; el concepto debe ser uno de los del tablero.
12. **Enlaces de decision hacia algo que no es salida de compuerta.** `salida_nodo_id` debe ser el DESTINO de una
    salida de una compuerta; si no, se rechaza.
13. **Variables de enlace que la plantilla de WhatsApp no tiene.** El cliente recibe el mensaje sin el enlace.
    Revisa las variables de la plantilla HSM antes.
14. **Probar avisos con clientes reales.** Cada prueba envia correos/WhatsApp de verdad. Usa una tarea de prueba
    con el contacto del propio usuario y avisale antes.
15. **Formulario obligatorio por salida en una tarea.** Solo aplica a compuertas; en una tarea usa
    `poner_formulario_a_nodo` con `obligatorio: true`.
