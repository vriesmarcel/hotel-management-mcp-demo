using System;

namespace Hotel.Contracts
{
    public sealed class Room
    {
        public int RId { get; set; }
        public string Category { get; set; }
        public string IsBooked { get; set; }
        public decimal RoomCost { get; set; }
        public int? BId { get; set; }
    }

    public sealed class FoodPackage
    {
        public int FId { get; set; }
        public string FName { get; set; }
        public decimal FPackageCost { get; set; }
    }

    public sealed class ServicePackage
    {
        public int SId { get; set; }
        public string SName { get; set; }
        public decimal SPackageCost { get; set; }
    }

    public sealed class GuestDetails
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        public string NationalId { get; set; }
    }

    public sealed class Booking
    {
        public string Status { get; set; }
        public int BId { get; set; }
        public int RId { get; set; }
        public DateTime CheckIn { get; set; }
        public DateTime CheckOut { get; set; }
        public int FId { get; set; }
        public int SId { get; set; }
        public string CName { get; set; }
        public string CAdd { get; set; }
        public string CPhone { get; set; }
        public string CNID { get; set; }
        public decimal Total { get; set; }
        public decimal Advance { get; set; }
        public decimal Remaining { get; set; }
    }

    public sealed class QuoteRequest
    {
        public int RoomId { get; set; }
        public int FoodPackageId { get; set; }
        public int ServicePackageId { get; set; }
        public DateTime CheckIn { get; set; }
        public DateTime CheckOut { get; set; }
    }

    public sealed class ReservationRequest
    {
        public string QuoteId { get; set; }
        public string RequestId { get; set; }
        public bool Confirmed { get; set; }
        public QuoteRequest Stay { get; set; }
        public GuestDetails Guest { get; set; }
        public decimal Advance { get; set; }
    }

    public sealed class Quote
    {
        public string QuoteId { get; set; }
        public QuoteRequest Stay { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public int Days { get; set; }
        public decimal Total { get; set; }
        public decimal Remaining { get; set; }
    }

    public sealed class CheckoutReceipt
    {
        public Booking Booking { get; set; }
        public decimal PaidNow { get; set; }
        public decimal Change { get; set; }
    }

    public sealed class DesktopUser
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Role { get; set; }
    }

    public class HotelException : Exception
    {
        public HotelException(string message) : base(message) { }
    }

    public sealed class HotelValidationException : HotelException
    {
        public HotelValidationException(string message) : base(message) { }
    }

    public sealed class HotelConflictException : HotelException
    {
        public HotelConflictException(string message) : base(message) { }
    }

    public sealed class HotelNotFoundException : HotelException
    {
        public HotelNotFoundException(string message) : base(message) { }
    }

    public sealed class HotelInfrastructureException : HotelException
    {
        public HotelInfrastructureException(string message) : base(message) { }
    }
}
