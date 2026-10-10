namespace Ecorex.Domain.Enums;

/// <summary>De donde viene un origen de requerimientos (que alimenta las necesidades de compra).</summary>
public enum RequerimientoFuenteTipo
{
    /// <summary>Alta a mano dentro del modulo.</summary>
    Manual = 0,
    /// <summary>Un formulario del gestor de formularios entrega las necesidades.</summary>
    Formulario = 1,
    /// <summary>Un Contenedor de Datos alimenta las necesidades.</summary>
    Contenedor = 2
}

/// <summary>Dimension del item sobre la que una regla de la matriz asigna proveedor. El orden va de lo
/// MAS especifico (Referencia) a lo mas general (Categoria): gana la de menor valor cuando hay empate.</summary>
public enum RequerimientoDimension
{
    Referencia = 0,
    Marca = 1,
    Subgrupo = 2,
    Grupo = 3,
    Categoria = 4
}

/// <summary>Estado de un requerimiento (y de sus registros hijos) a lo largo del ciclo de compra.</summary>
public enum RequerimientoEstado
{
    /// <summary>Recien llegado de una fuente (o alta manual), sin procesar.</summary>
    Recibido = 0,
    /// <summary>Consolidado/agrupado con otros pedidos del mismo producto.</summary>
    Agrupado = 1,
    /// <summary>Dividido en registros hijos (granularidad) y programado en el tiempo.</summary>
    Planificado = 2,
    /// <summary>Con proveedor asignado, listo para ejecutar.</summary>
    ConProveedor = 3,
    /// <summary>Compra ejecutada: genero una actividad (tarea).</summary>
    Ejecutado = 4,
    /// <summary>Cerrado.</summary>
    Cerrado = 5
}
