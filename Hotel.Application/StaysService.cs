using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Hotel.Contracts;

namespace Hotel.Application
{
    public sealed class StaysService : IStays
    {
        private readonly IHotelStore store;
        public StaysService(IHotelStore store) { this.store = store; }
        public IReadOnlyList<Booking> GetArrivals(DateTime date) => store.Read(s => s.GetBookings().Where(b => b.CheckIn == date.Date).Select(b => Workflow.Booking(s, b.BId)).Where(b => b.Status == "Reserved").ToList());
        public Booking CheckIn(int id, string requestId, bool confirmed)
        {
            var key = Workflow.Key("checkin", requestId, confirmed); var hash = Workflow.Hash(id);
            return store.Transact(s =>
            {
                var db = Workflow.Data(s); if (Workflow.Replay(db, key, hash) != null) return Workflow.Booking(s, id);
                var b = Workflow.Booking(s, id);
                if (b.Status != "Reserved") throw new HotelConflictException("Only a reserved stay can be checked in.");
                if (DateTime.Today < b.CheckIn || DateTime.Today >= b.CheckOut) throw new HotelValidationException("Check-in is allowed only during the booked stay dates.");
                var room = s.GetRoom(b.RId);
                if (room == null || room.IsBooked != "No") throw new HotelConflictException("Room is currently occupied or unavailable.");
                room.IsBooked = "Yes"; room.BId = id; s.UpdateRoom(room); db.SetStatus(id, "CheckedIn");
                db.SaveOperation(key, new OperationRecord { PayloadHash = hash, BookingId = id }); b.Status = "CheckedIn"; return b;
            });
        }
    }
}
