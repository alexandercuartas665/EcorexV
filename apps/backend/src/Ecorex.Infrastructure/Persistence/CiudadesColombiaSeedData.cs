namespace Ecorex.Infrastructure.Persistence;

/// <summary>
/// Semilla del catalogo GLOBAL de ciudades: municipios de Colombia (departamento + municipio).
/// Los tenants de ECOREX son colombianos, asi que el selector de ciudad se restringe a este listado.
/// Convencion ASCII del proyecto: nombres sin tildes ni enie (ej. "Medellin", "Narino", "Choco").
///
/// TODO(catalogo-completo): esta lista cubre las 32 capitales departamentales + Bogota D.C. y los
/// municipios mas poblados/conocidos de cada departamento (~230 filas). El catalogo DANE completo
/// son ~1103 municipios. Para completarlo, cargar el CSV oficial de la Division Politico-
/// Administrativa (DIVIPOLA) del DANE y reemplazar/extender este arreglo (o migrar a un .csv
/// embebido). La siembra es idempotente por (Departamento, Municipio), asi que ampliar la lista y
/// re-sembrar solo agrega los faltantes (ver DatabaseSeeder.EnsureCiudadesAsync).
/// </summary>
public static class CiudadesColombiaSeedData
{
    /// <summary>Pares (Departamento, Municipio). Sin codigo DANE por ahora (columna opcional).</summary>
    public static IReadOnlyList<(string Departamento, string Municipio)> Municipios { get; } = new (string, string)[]
    {
        // ----- Bogota, Distrito Capital -----
        ("Bogota D.C.", "Bogota"),

        // ----- Antioquia -----
        ("Antioquia", "Medellin"),
        ("Antioquia", "Bello"),
        ("Antioquia", "Itagui"),
        ("Antioquia", "Envigado"),
        ("Antioquia", "Sabaneta"),
        ("Antioquia", "La Estrella"),
        ("Antioquia", "Caldas"),
        ("Antioquia", "Copacabana"),
        ("Antioquia", "Girardota"),
        ("Antioquia", "Barbosa"),
        ("Antioquia", "Rionegro"),
        ("Antioquia", "Marinilla"),
        ("Antioquia", "La Ceja"),
        ("Antioquia", "Apartado"),
        ("Antioquia", "Turbo"),
        ("Antioquia", "Carepa"),
        ("Antioquia", "Chigorodo"),
        ("Antioquia", "Caucasia"),
        ("Antioquia", "Yarumal"),
        ("Antioquia", "Santa Fe de Antioquia"),
        ("Antioquia", "Puerto Berrio"),
        ("Antioquia", "Segovia"),
        ("Antioquia", "Andes"),

        // ----- Atlantico -----
        ("Atlantico", "Barranquilla"),
        ("Atlantico", "Soledad"),
        ("Atlantico", "Malambo"),
        ("Atlantico", "Sabanalarga"),
        ("Atlantico", "Puerto Colombia"),
        ("Atlantico", "Galapa"),
        ("Atlantico", "Baranoa"),
        ("Atlantico", "Sabanagrande"),
        ("Atlantico", "Santo Tomas"),

        // ----- Bolivar -----
        ("Bolivar", "Cartagena"),
        ("Bolivar", "Magangue"),
        ("Bolivar", "Turbaco"),
        ("Bolivar", "Arjona"),
        ("Bolivar", "El Carmen de Bolivar"),
        ("Bolivar", "Mompos"),
        ("Bolivar", "Maria la Baja"),
        ("Bolivar", "San Juan Nepomuceno"),

        // ----- Boyaca -----
        ("Boyaca", "Tunja"),
        ("Boyaca", "Duitama"),
        ("Boyaca", "Sogamoso"),
        ("Boyaca", "Chiquinquira"),
        ("Boyaca", "Paipa"),
        ("Boyaca", "Villa de Leyva"),
        ("Boyaca", "Puerto Boyaca"),
        ("Boyaca", "Moniquira"),
        ("Boyaca", "Nobsa"),

        // ----- Caldas -----
        ("Caldas", "Manizales"),
        ("Caldas", "La Dorada"),
        ("Caldas", "Chinchina"),
        ("Caldas", "Villamaria"),
        ("Caldas", "Riosucio"),
        ("Caldas", "Anserma"),
        ("Caldas", "Supia"),

        // ----- Caqueta -----
        ("Caqueta", "Florencia"),
        ("Caqueta", "San Vicente del Caguan"),
        ("Caqueta", "Puerto Rico"),
        ("Caqueta", "El Doncello"),

        // ----- Cauca -----
        ("Cauca", "Popayan"),
        ("Cauca", "Santander de Quilichao"),
        ("Cauca", "Puerto Tejada"),
        ("Cauca", "Patia"),
        ("Cauca", "Piendamo"),
        ("Cauca", "Guapi"),

        // ----- Cesar -----
        ("Cesar", "Valledupar"),
        ("Cesar", "Aguachica"),
        ("Cesar", "Agustin Codazzi"),
        ("Cesar", "Bosconia"),
        ("Cesar", "La Jagua de Ibirico"),
        ("Cesar", "El Copey"),

        // ----- Cordoba -----
        ("Cordoba", "Monteria"),
        ("Cordoba", "Cerete"),
        ("Cordoba", "Lorica"),
        ("Cordoba", "Sahagun"),
        ("Cordoba", "Planeta Rica"),
        ("Cordoba", "Montelibano"),
        ("Cordoba", "Tierralta"),
        ("Cordoba", "Cienaga de Oro"),

        // ----- Cundinamarca -----
        ("Cundinamarca", "Soacha"),
        ("Cundinamarca", "Facatativa"),
        ("Cundinamarca", "Zipaquira"),
        ("Cundinamarca", "Chia"),
        ("Cundinamarca", "Mosquera"),
        ("Cundinamarca", "Madrid"),
        ("Cundinamarca", "Funza"),
        ("Cundinamarca", "Fusagasuga"),
        ("Cundinamarca", "Girardot"),
        ("Cundinamarca", "Cajica"),
        ("Cundinamarca", "Cota"),
        ("Cundinamarca", "La Calera"),
        ("Cundinamarca", "Sibate"),
        ("Cundinamarca", "Tocancipa"),
        ("Cundinamarca", "Zipacon"),
        ("Cundinamarca", "Ubate"),
        ("Cundinamarca", "Villeta"),
        ("Cundinamarca", "Caqueza"),

        // ----- Choco -----
        ("Choco", "Quibdo"),
        ("Choco", "Istmina"),
        ("Choco", "Tado"),
        ("Choco", "Condoto"),
        ("Choco", "Bahia Solano"),

        // ----- Huila -----
        ("Huila", "Neiva"),
        ("Huila", "Pitalito"),
        ("Huila", "Garzon"),
        ("Huila", "La Plata"),
        ("Huila", "Campoalegre"),
        ("Huila", "Gigante"),

        // ----- La Guajira -----
        ("La Guajira", "Riohacha"),
        ("La Guajira", "Maicao"),
        ("La Guajira", "Uribia"),
        ("La Guajira", "Fonseca"),
        ("La Guajira", "San Juan del Cesar"),
        ("La Guajira", "Villanueva"),
        ("La Guajira", "Manaure"),

        // ----- Magdalena -----
        ("Magdalena", "Santa Marta"),
        ("Magdalena", "Cienaga"),
        ("Magdalena", "Fundacion"),
        ("Magdalena", "El Banco"),
        ("Magdalena", "Plato"),
        ("Magdalena", "Zona Bananera"),

        // ----- Meta -----
        ("Meta", "Villavicencio"),
        ("Meta", "Acacias"),
        ("Meta", "Granada"),
        ("Meta", "Puerto Lopez"),
        ("Meta", "San Martin"),
        ("Meta", "Cumaral"),
        ("Meta", "Puerto Gaitan"),

        // ----- Narino -----
        ("Narino", "Pasto"),
        ("Narino", "Tumaco"),
        ("Narino", "Ipiales"),
        ("Narino", "Tuquerres"),
        ("Narino", "La Union"),
        ("Narino", "Samaniego"),
        ("Narino", "Sandona"),

        // ----- Norte de Santander -----
        ("Norte de Santander", "Cucuta"),
        ("Norte de Santander", "Ocana"),
        ("Norte de Santander", "Villa del Rosario"),
        ("Norte de Santander", "Los Patios"),
        ("Norte de Santander", "Pamplona"),
        ("Norte de Santander", "Tibu"),

        // ----- Putumayo -----
        ("Putumayo", "Mocoa"),
        ("Putumayo", "Puerto Asis"),
        ("Putumayo", "Orito"),
        ("Putumayo", "Valle del Guamuez"),
        ("Putumayo", "Sibundoy"),

        // ----- Quindio -----
        ("Quindio", "Armenia"),
        ("Quindio", "Calarca"),
        ("Quindio", "La Tebaida"),
        ("Quindio", "Montenegro"),
        ("Quindio", "Quimbaya"),
        ("Quindio", "Circasia"),

        // ----- Risaralda -----
        ("Risaralda", "Pereira"),
        ("Risaralda", "Dosquebradas"),
        ("Risaralda", "Santa Rosa de Cabal"),
        ("Risaralda", "La Virginia"),
        ("Risaralda", "Marsella"),
        ("Risaralda", "Quinchia"),

        // ----- Santander -----
        ("Santander", "Bucaramanga"),
        ("Santander", "Floridablanca"),
        ("Santander", "Giron"),
        ("Santander", "Piedecuesta"),
        ("Santander", "Barrancabermeja"),
        ("Santander", "San Gil"),
        ("Santander", "Socorro"),
        ("Santander", "Barbosa"),
        ("Santander", "Malaga"),

        // ----- Sucre -----
        ("Sucre", "Sincelejo"),
        ("Sucre", "Corozal"),
        ("Sucre", "Sampues"),
        ("Sucre", "San Marcos"),
        ("Sucre", "San Onofre"),
        ("Sucre", "Tolu"),

        // ----- Tolima -----
        ("Tolima", "Ibague"),
        ("Tolima", "Espinal"),
        ("Tolima", "Melgar"),
        ("Tolima", "Honda"),
        ("Tolima", "Chaparral"),
        ("Tolima", "Libano"),
        ("Tolima", "Mariquita"),
        ("Tolima", "Flandes"),

        // ----- Valle del Cauca -----
        ("Valle del Cauca", "Cali"),
        ("Valle del Cauca", "Palmira"),
        ("Valle del Cauca", "Buenaventura"),
        ("Valle del Cauca", "Tulua"),
        ("Valle del Cauca", "Cartago"),
        ("Valle del Cauca", "Buga"),
        ("Valle del Cauca", "Jamundi"),
        ("Valle del Cauca", "Yumbo"),
        ("Valle del Cauca", "Candelaria"),
        ("Valle del Cauca", "Florida"),
        ("Valle del Cauca", "Zarzal"),
        ("Valle del Cauca", "Sevilla"),
        ("Valle del Cauca", "Caicedonia"),

        // ----- Arauca -----
        ("Arauca", "Arauca"),
        ("Arauca", "Saravena"),
        ("Arauca", "Tame"),
        ("Arauca", "Arauquita"),
        ("Arauca", "Fortul"),

        // ----- Casanare -----
        ("Casanare", "Yopal"),
        ("Casanare", "Aguazul"),
        ("Casanare", "Villanueva"),
        ("Casanare", "Tauramena"),
        ("Casanare", "Paz de Ariporo"),

        // ----- Putumayo ya listado arriba -----

        // ----- Amazonas -----
        ("Amazonas", "Leticia"),
        ("Amazonas", "Puerto Narino"),

        // ----- Guainia -----
        ("Guainia", "Inirida"),

        // ----- Guaviare -----
        ("Guaviare", "San Jose del Guaviare"),
        ("Guaviare", "Calamar"),
        ("Guaviare", "El Retorno"),

        // ----- Vaupes -----
        ("Vaupes", "Mitu"),

        // ----- Vichada -----
        ("Vichada", "Puerto Carreno"),
        ("Vichada", "La Primavera"),

        // ----- San Andres y Providencia -----
        ("San Andres y Providencia", "San Andres"),
        ("San Andres y Providencia", "Providencia"),
    };
}
