namespace Ecorex.Domain.Enums;

/// <summary>Tipo de un campo configurable del salon (define el input y el formato del valor).</summary>
public enum SalonFieldType
{
    Text,       // Texto corto
    TextArea,   // Texto largo
    Number,     // Numero
    Currency,   // Monto en pesos
    Select,     // Lista de opciones (Options, una por linea)
    Date,       // Fecha
    Time,       // Hora HH:mm
    Phone,      // Telefono
    Boolean,    // Si / No (checkbox)
    Separator   // Linea divisoria (no captura valor)
}
