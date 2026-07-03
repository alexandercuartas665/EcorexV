// Drag-and-drop del tablero Pipeline.
// El DnD nativo de HTML5 no funciona en Blazor Server porque dragover.preventDefault()
// debe ser sincrono en el cliente y Blazor enruta los eventos al servidor de forma
// asincrona (para cuando vuelve el preventDefault, el navegador ya descarto la zona de
// soltado). Aqui hacemos el preventDefault del lado cliente y devolvemos el movimiento
// a .NET via invokeMethodAsync. Delegacion a nivel document: sobrevive a los re-render
// del tablero (Blazor reemplaza los nodos, pero los listeners de document persisten).
window.ecorexPipeline = (function () {
  let dotnet = null;
  let draggedId = null;
  let wired = false;

  function colOf(target) {
    return target && target.closest ? target.closest('.kb-col[data-stage-id]') : null;
  }

  function wire() {
    if (wired) { return; }
    wired = true;

    document.addEventListener('dragstart', function (e) {
      const card = e.target.closest ? e.target.closest('.kb-card[data-lead-id]') : null;
      if (!card) { return; }
      draggedId = card.getAttribute('data-lead-id');
      if (e.dataTransfer) {
        e.dataTransfer.effectAllowed = 'move';
        try { e.dataTransfer.setData('text/plain', draggedId); } catch (_) { }
      }
      card.classList.add('dragging');
    });

    document.addEventListener('dragend', function (e) {
      const card = e.target.closest ? e.target.closest('.kb-card') : null;
      if (card) { card.classList.remove('dragging'); }
      document.querySelectorAll('.kb-col.drop-hover').forEach(function (c) { c.classList.remove('drop-hover'); });
    });

    document.addEventListener('dragover', function (e) {
      const col = colOf(e.target);
      if (!col) { return; }
      e.preventDefault(); // sincrono: habilita el drop
      if (e.dataTransfer) { e.dataTransfer.dropEffect = 'move'; }
    });

    document.addEventListener('dragenter', function (e) {
      const col = colOf(e.target);
      if (col) { col.classList.add('drop-hover'); }
    });

    document.addEventListener('dragleave', function (e) {
      const col = colOf(e.target);
      if (col && !col.contains(e.relatedTarget)) { col.classList.remove('drop-hover'); }
    });

    document.addEventListener('drop', function (e) {
      const col = colOf(e.target);
      if (!col) { return; }
      e.preventDefault();
      col.classList.remove('drop-hover');
      const stageId = col.getAttribute('data-stage-id');
      const leadId = draggedId || (e.dataTransfer ? e.dataTransfer.getData('text/plain') : null);
      draggedId = null;
      if (leadId && stageId && dotnet) {
        dotnet.invokeMethodAsync('MoveLeadFromJs', leadId, stageId);
      }
    });

    // ===== Arrastrar-y-soltar archivos del SO sobre la conversacion del chat =====
    // Delegacion a nivel document (sobrevive a los re-render de Blazor del panel de chat).
    function chatZone(target) {
      return target && target.closest ? target.closest('#pl-chat-drop') : null;
    }
    function dragHasFiles(dt) {
      return !!dt && dt.types && Array.prototype.indexOf.call(dt.types, 'Files') >= 0;
    }

    document.addEventListener('dragenter', function (e) {
      const z = chatZone(e.target);
      if (z && dragHasFiles(e.dataTransfer)) { e.preventDefault(); z.classList.add('pl-chat-dragover'); }
    });
    document.addEventListener('dragover', function (e) {
      const z = chatZone(e.target);
      if (z && dragHasFiles(e.dataTransfer)) {
        e.preventDefault();
        try { e.dataTransfer.dropEffect = 'copy'; } catch (_) { }
        z.classList.add('pl-chat-dragover');
      }
    });
    document.addEventListener('dragleave', function (e) {
      const z = chatZone(e.target);
      if (z && !z.contains(e.relatedTarget)) { z.classList.remove('pl-chat-dragover'); }
    });
    document.addEventListener('drop', function (e) {
      const z = chatZone(e.target);
      if (!z) { return; }
      e.preventDefault();
      z.classList.remove('pl-chat-dragover');
      const files = e.dataTransfer && e.dataTransfer.files ? Array.prototype.slice.call(e.dataTransfer.files) : [];
      files.forEach(function (f) {
        const reader = new FileReader();
        reader.onload = function () {
          const res = reader.result || '';
          const b64 = res.indexOf(',') >= 0 ? res.split(',')[1] : '';
          if (b64 && dotnet) { dotnet.invokeMethodAsync('OnChatFileDropped', f.name, f.type || '', b64); }
        };
        reader.readAsDataURL(f);
      });
    });
  }

  return {
    init: function (ref) { dotnet = ref; wire(); },
    // Compatibilidad: el wiring del chat ahora es a nivel document en wire(); no hace falta init por elemento.
    initChatDrop: function () { }
  };
})();

// Desplaza el cuerpo del chat al ultimo mensaje (auto-scroll al recibir/enviar).
window.ecorexScrollChat = function () {
  const body = document.querySelector('.pl-chat-body');
  if (body) { body.scrollTop = body.scrollHeight; }
};
