using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Ecorex.SuperAdmin.Agents;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Tests de la HEURISTICA PURA del Cargador de contactos (000740): extraccion de telefonos/correos del
/// sitio, filtro de correos basura + prioridad del dominio propio, dedup, armado de web_contacts, badge por
/// la metrica de Maps y saneo de URLs. Son funciones internas estaticas sin BD (InternalsVisibleTo), que es
/// justo lo que se rompe en silencio (p.ej. se colo un botmanager_support@radware.com).
/// </summary>
public class ContactSearchHeuristicsTests
{
    // ---- Telefonos ----

    [Fact]
    public void ExtractPhones_ReconoceFormatosColombianos()
    {
        var html = @"
            Fijo nacional: +57 601 3905099
            Fijo con parentesis: (601) 3905099
            Fijo plano: 601 3905099
            Celular con espacios: 315 360 0000
            Celular plano: 3153600000
            <a href=""tel:+576013905099"">llamar</a>";
        var phones = ContactSearchRunner.ExtractPhones(html).ToList();

        // Todas las variantes del fijo (con/sin parentesis, con/sin +57, tel:) normalizan al mismo numero.
        Assert.Contains("6013905099", phones);
        // Celular con y sin espacios.
        Assert.Contains("3153600000", phones);
    }

    [Fact]
    public void ExtractPhones_Dedup_VariantesDelMismoNumeroSonUna()
    {
        var html = "(601) 3905099 | 6013905099 | +57 601 3905099";
        var phones = ContactSearchRunner.DistinctKeep(ContactSearchRunner.ExtractPhones(html));
        Assert.Single(phones, p => p == "6013905099");
    }

    [Theory]
    [InlineData("900123456")]       // NIT (9 digitos)
    [InlineData("2024")]            // anio
    [InlineData("1234567890123")]   // corrida larga (ID)
    public void NormalizePhone_RechazaLoQueNoEsTelefono(string raw)
    {
        Assert.Null(ContactSearchRunner.NormalizePhone(raw));
    }

    [Theory]
    [InlineData("3153600000", "3153600000")]
    [InlineData("+57 315 360 0000", "3153600000")]
    [InlineData("(601) 3905099", "6013905099")]
    [InlineData("6013905099", "6013905099")]
    public void NormalizePhone_DejaFormaNacional(string raw, string expected)
    {
        Assert.Equal(expected, ContactSearchRunner.NormalizePhone(raw));
    }

    // ---- Correos: filtro de basura ----

    [Theory]
    [InlineData("notificacionescdc@clinicadelcountry.com", true)]
    [InlineData("botmanager_support@radware.com", false)]   // anti-bot (infra de terceros)
    [InlineData("noreply@clinicadelcountry.com", false)]    // buzon automatico
    [InlineData("info@cloudflare.com", false)]              // CDN
    [InlineData("hi@sentry.io", false)]                     // observabilidad
    [InlineData("logo@2x.png", false)]                      // falso positivo (imagen)
    public void IsPlausibleEmail_AceptaEmpresa_RechazaBasura(string email, bool esperado)
    {
        Assert.Equal(esperado, ContactSearchRunner.IsPlausibleEmail(email));
    }

    [Fact]
    public void ExtractAllEmails_QuitaMailto_YDescartaBasura()
    {
        var html = @"<a href=""mailto:notificacionescdc@clinicadelcountry.com"">correo</a>
                     <img src=""logo@2x.png""> soporte botmanager_support@radware.com";
        var emails = ContactSearchRunner.ExtractAllEmails(html).ToList();

        Assert.Contains("notificacionescdc@clinicadelcountry.com", emails); // sin el prefijo mailto:
        Assert.DoesNotContain("botmanager_support@radware.com", emails);
        Assert.DoesNotContain(emails, e => e.Contains("2x.png"));
    }

    [Fact]
    public void RankEmailsByOwnDomain_PrioridadDominioPropio()
    {
        var emails = new List<string> { "ventas@gmail.com", "info@clinicadelcountry.com" };
        var ranked = ContactSearchRunner.RankEmailsByOwnDomain(emails, "https://www.clinicadelcountry.com/contacto");

        Assert.Equal("info@clinicadelcountry.com", ranked[0]); // dominio del sitio primero
        Assert.Equal("ventas@gmail.com", ranked[1]);           // ajeno al final
    }

    // ---- DistinctKeep ----

    [Fact]
    public void DistinctKeep_DedupInsensibleAMayusculas_PreservaOrden()
    {
        var items = new[] { "A@X.com", "b@y.com", "a@x.COM", "B@Y.COM" };
        var result = ContactSearchRunner.DistinctKeep(items);
        Assert.Equal(new[] { "A@X.com", "b@y.com" }, result);
    }

    // ---- MergeWebContacts ----

    [Fact]
    public void MergeWebContacts_NoPisaMaps_CreaWebContacts_YNoDuplicaAlRecorrer()
    {
        var sitio = "https://clinicadelcountry.com";
        var correos = new[] { "info@clinicadelcountry.com" };
        var telefonos = new[] { "6013905099" };
        // data_json que YA trae lo de Maps (no debe perderse al mezclar).
        var original = "{\"nombre\":\"Clinica\",\"telefono\":\"6013905099\"}";

        var merged = ContactSearchRunner.MergeWebContacts(original, sitio, correos, telefonos);
        using (var doc = JsonDocument.Parse(merged))
        {
            var root = doc.RootElement;
            Assert.Equal("Clinica", root.GetProperty("nombre").GetString());        // preserva Maps
            Assert.Equal("6013905099", root.GetProperty("telefono").GetString());
            var wc = root.GetProperty("web_contacts");
            Assert.Equal(sitio, wc.GetProperty("sitio").GetString());
            Assert.Single(wc.GetProperty("correos").EnumerateArray());
            Assert.Single(wc.GetProperty("telefonos").EnumerateArray());
        }

        // Al re-correr sobre el resultado, web_contacts se REEMPLAZA (no se duplica la lista).
        var merged2 = ContactSearchRunner.MergeWebContacts(merged, sitio, correos, telefonos);
        using (var doc2 = JsonDocument.Parse(merged2))
        {
            var wc = doc2.RootElement.GetProperty("web_contacts");
            Assert.Single(wc.GetProperty("correos").EnumerateArray());
            Assert.Single(wc.GetProperty("telefonos").EnumerateArray());
        }
    }

    // ---- ComputeBadge / ParseMetrica ----

    [Theory]
    [InlineData("4.9 (6,690 opiniones)", null, "Hot")]
    [InlineData("4.7 (12 opiniones)", null, "Calificado")]    // rating>=4.0 pero <20 resenas
    [InlineData("3.0(6)", null, "Nuevo")]
    [InlineData(null, "https://empresa.com", "Calificado")]    // sin metrica pero con sitio web
    [InlineData("Sin opiniones", null, "Nuevo")]
    public void ComputeBadge_SegunMetricaYSitio(string? metrica, string? sitio, string esperado)
    {
        Assert.Equal(esperado, ProspectoSearchRowSink.ComputeBadge(metrica, sitio));
    }

    // ---- SafeHttpUrl ----

    [Theory]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:text/html,x", null)]
    [InlineData("/ruta/relativa", null)]
    [InlineData("https://empresa.com/x", "https://empresa.com/x")]
    public void SafeHttpUrl_SoloHttpAbsoluta(string input, string? esperado)
    {
        Assert.Equal(esperado, ProspectoSearchRowSink.SafeHttpUrl(input));
    }

    // ---- TryRootUrl (fallback a la raiz del dominio ante 404/SSL de la ruta profunda) ----

    [Theory]
    // Ruta profunda -> raiz del dominio.
    [InlineData("https://empresa.com/contacto/equipo", "https://empresa.com/")]
    [InlineData("http://www.empresa.com/es/nosotros?x=1", "http://www.empresa.com/")]
    // Puerto preservado en la autoridad.
    [InlineData("https://empresa.com:8443/a/b", "https://empresa.com:8443/")]
    public void TryRootUrl_RutaProfunda_DevuelveRaiz(string input, string esperado)
    {
        Assert.Equal(esperado, ContactSearchRunner.TryRootUrl(input));
    }

    [Theory]
    // Ya es la raiz: no hay nada que reintentar -> null.
    [InlineData("https://empresa.com")]
    [InlineData("https://empresa.com/")]
    // No http(s): no se reintenta.
    [InlineData("ftp://empresa.com/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/ruta/relativa")]
    [InlineData("")]
    [InlineData(null)]
    public void TryRootUrl_SinRutaOInvalida_DevuelveNull(string? input)
    {
        Assert.Null(ContactSearchRunner.TryRootUrl(input));
    }
}
