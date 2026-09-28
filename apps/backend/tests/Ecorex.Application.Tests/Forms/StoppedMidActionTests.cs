using Ecorex.Application.Forms.Builder;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// PRUEBA DORADA de la heuristica anti "narra pero no emite tools": cuando el agente anuncia una accion futura
// sin emitir llamadas, el servicio lo empuja UNA vez. Aqui se fija que empuja en las narraciones reales que vimos
// y que NO empuja cuando pregunta o cuando ya termino.
public class StoppedMidActionTests
{
    [Theory]
    [InlineData("Ahora si, voy a agregar el campo cliente, la tabla de items y el boton de imprimir.")]
    [InlineData("Entendido. Vamos a proceder con la construccion. Aqui te presento las llamadas a las herramientas:")]
    [InlineData("Primero creare el contenedor de datos. A continuacion, agregare las secciones y sus campos.")]
    public void Empuja_cuando_narra_una_accion_futura(string text)
        => Assert.True(FormBuilderChatService.StoppedMidAction(text));

    [Theory]
    [InlineData("He creado la tabla \"Items\". Puedes probar el borrador en la Vista previa.")]
    [InlineData("El formulario esta listo. Si necesitas alguna modificacion, hazmelo saber.")]
    [InlineData("Esas gestiones (Cotizacion, PQR...) son botones que crean otro registro, o solo etiquetas?")]
    [InlineData("Voy a crear la seccion 'Datos del cliente'. Confirmas los campos nombre, nit, telefono?")]
    [InlineData("")]
    [InlineData(null)]
    public void No_empuja_cuando_pregunta_o_ya_termino(string? text)
        => Assert.False(FormBuilderChatService.StoppedMidAction(text));
}
