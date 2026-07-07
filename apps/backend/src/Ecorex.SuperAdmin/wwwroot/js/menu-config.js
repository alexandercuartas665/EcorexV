// Helper del Administrador de Menu (Ola 2): descarga un texto (JSON de la vista exportada)
// como archivo, sin depender de librerias. Se invoca desde ConfiguracionMenu.razor via JS interop.
window.ecorexMenuConfig = {
  downloadText: function (filename, text, mime) {
    try {
      var blob = new Blob([text], { type: mime || "application/json;charset=utf-8" });
      var url = window.URL.createObjectURL(blob);
      var a = document.createElement("a");
      a.href = url;
      a.download = filename || "vista-menu.json";
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      window.URL.revokeObjectURL(url);
      return true;
    } catch (e) {
      return false;
    }
  }
};
