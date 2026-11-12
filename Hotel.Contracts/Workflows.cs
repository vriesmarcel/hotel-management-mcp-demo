using System;
using System.Collections.Generic;
namespace Hotel.Contracts
{
    public interface IReservations : IReservationService
    {
        AvailabilityResult GetAvailableRooms(DateTime checkIn, DateTime checkOut);
        Booking GetReservation(int bookingId);
    }
    public interface IStays
    {
        IReadOnlyList<Booking> GetArrivals(DateTime date);
        Booking CheckIn(int bookingId, string requestId, bool confirmed);
    }
    public interface IBilling : IBillingService
    {
        Booking GetBill(int bookingId);
        Booking RecordPayment(int bookingId, decimal amount, string requestId, bool confirmed);
        CheckoutReceipt CompleteCheckout(int bookingId, decimal paidNow, string requestId, bool confirmed);
    }
}
