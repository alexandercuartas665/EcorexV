using Ecorex.Application.Forms.Builder;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// PRUEBA DORADA de la mitigacion del "flaky" de vision: detecta cuando el agente PIDE el archivo (aunque ya
// venga adjunto) para reenviarlo una vez, y NO dispara en respuestas normales.
public class AsksForAttachmentTests
{
    [Theory]
    // El caso real que vimos con Gemini flash.
    [InlineData("Necesito que me adjuntes la ficha de inspeccion para poder armar el formulario. Puede ser un archivo Excel, PDF o una imagen.")]
    [InlineData("No veo ningun archivo adjunto. Sube el PDF por favor.")]
    [InlineData("Comparteme el documento para continuar.")]
    [InlineData("Para armarlo necesito que me adjuntes la imagen.")]
    public void Detecta_cuando_pide_el_archivo(string text)
        => Assert.True(FormBuilderChatService.AsksForAttachment(text));

    [Theory]
    // Respuestas normales: leyo el archivo, o construye, o cierra -> NO debe disparar.
    [InlineData("He analizado la imagen de la ficha de inspeccion. Voy a crear las secciones.")]
    [InlineData("El formulario esta listo. Puedes probarlo en la vista previa.")]
    [InlineData("Voy a agregar un campo de firma al final.")]
    [InlineData("")]
    [InlineData(null)]
    public void No_dispara_en_respuestas_normales(string? text)
        => Assert.False(FormBuilderChatService.AsksForAttachment(text));
}
