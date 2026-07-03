namespace Ecorex.Domain.Enums;

/// <summary>Estado de un pago de suscripcion, alineado con estados Wompi mas revision interna.</summary>
public enum PaymentStatus
{
    Pending,
    Approved,
    Declined,
    Voided,
    Error,
    NeedsReview
}
