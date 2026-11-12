using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Hotel.Contracts;

namespace Hotel.Application
{
    public sealed class ReservationsService : IReservations
    {
        private readonly IHotelStore store;
        public ReservationsService(IHotelStore store) { this.store = store; }
        public AvailabilityResult GetAvailableRooms(DateTime checkIn, DateTime checkOut)
        {
            Workflow.DateRange(checkIn, checkOut);
            return store.Read(s => new AvailabilityResult
            {
                CheckIn = checkIn.ToString("yyyy-MM-dd"),
                CheckOut = checkOut.ToString("yyyy-MM-dd"),
                Nights = (checkOut - checkIn).Days,
                Rooms = s.GetRooms("").Where(r => Workflow.Data(s).IsAvailable(r.RId, checkIn, checkOut)).Select(r => new AvailableRoom { RoomId = r.RId, Category = r.Category, NightlyRate = r.RoomCost }).ToList(),
                RateNote = "Nightly room rates only; currency is not recorded. Availability is not a hold."
            });
        }
        public Quote CalculateQuote(QuoteRequest request, decimal advance = 0)
        {
            if (request == null) throw new HotelValidationException("Stay details are required.");
            Workflow.DateRange(request.CheckIn, request.CheckOut); Workflow.Money(advance);
            return store.Transact(s =>
            {
                if (!Workflow.Data(s).IsAvailable(request.RoomId, request.CheckIn, request.CheckOut)) throw new HotelConflictException("Room is not available for these dates.");
                var quote = HotelServices.QuoteFor(s, request);
                Workflow.Money(quote.Total);
                if (advance > quote.Total) throw new HotelValidationException("Advance exceeds the total.");
                quote.QuoteId = Guid.NewGuid().ToString("D"); quote.Stay = request; quote.ExpiresUtc = DateTime.UtcNow.AddMinutes(15); quote.Remaining = quote.Total - advance;
                quote.Cancellation = CancellationRules.ForStay(request.CheckIn, s.GetRoom(request.RoomId).RoomCost);
                Workflow.Data(s).SaveQuote(quote); return quote;
            });
        }
        public decimal CalculateRemaining(QuoteRequest request, decimal advance) => CalculateQuote(request, advance).Remaining;
        public Booking GetReservation(int id) => store.Read(s => Workflow.Booking(s, id));
        public Booking CreateReservation(ReservationRequest request)
        {
            if (request == null) throw new HotelValidationException("Reservation details are required.");
            HotelServices.ValidateGuest(request.Guest); Workflow.Money(request.Advance);
            if (!Guid.TryParse(request.QuoteId, out _)) throw new HotelValidationException("Calculate a quote before confirming the reservation.");
            string key = Workflow.Key("reservation", request.RequestId, request.Confirmed);
            string hash = Workflow.Hash(request.QuoteId, request.Guest.Name, request.Guest.Address, request.Guest.Phone, request.Guest.NationalId, request.Advance, request.Stay?.RoomId, request.Stay?.FoodPackageId, request.Stay?.ServicePackageId, request.Stay?.CheckIn.Ticks, request.Stay?.CheckOut.Ticks);
            return store.Transact(s =>
            {
                var db = Workflow.Data(s); var prior = Workflow.Replay(db, key, hash);
                if (prior != null) return Workflow.Booking(s, prior.BookingId);
                var quote = db.GetQuote(request.QuoteId) ?? throw new HotelNotFoundException("Quote was not found.");
                if (quote.Cancellation == null) throw new HotelConflictException("This quote predates cancellation terms; calculate and confirm a new quote.");
                if (quote.ExpiresUtc < DateTime.UtcNow) throw new HotelConflictException("Quote expired; calculate and confirm a new quote.");
                var stay = quote.Stay;
                if (request.Stay != null && (request.Stay.RoomId != stay.RoomId || request.Stay.CheckIn != stay.CheckIn || request.Stay.CheckOut != stay.CheckOut || request.Stay.FoodPackageId != stay.FoodPackageId || request.Stay.ServicePackageId != stay.ServicePackageId))
                    throw new HotelConflictException("Stay details differ from the confirmed quote.");
                if (!db.IsAvailable(stay.RoomId, stay.CheckIn, stay.CheckOut)) throw new HotelConflictException("Room was booked by another request.");
                if (HotelServices.QuoteFor(s, stay).Total != quote.Total) throw new HotelConflictException("Pricing changed; calculate and confirm a new quote.");
                if (request.Advance > quote.Total) throw new HotelValidationException("Advance exceeds the total.");
                var b = s.InsertBooking(new Booking { RId = stay.RoomId, CheckIn = stay.CheckIn, CheckOut = stay.CheckOut, FId = stay.FoodPackageId, SId = stay.ServicePackageId, CName = request.Guest.Name, CAdd = request.Guest.Address, CPhone = request.Guest.Phone, CNID = request.Guest.NationalId, Total = quote.Total, Advance = request.Advance, Remaining = quote.Total - request.Advance });
                db.AcceptCancellation(quote.QuoteId, b.BId); b.Cancellation = quote.Cancellation;
                db.SetStatus(b.BId, "Reserved"); db.SaveOperation(key, new OperationRecord { PayloadHash = hash, BookingId = b.BId, Amount = request.Advance }); b.Status = "Reserved"; return b;
            });
        }
    }
}
