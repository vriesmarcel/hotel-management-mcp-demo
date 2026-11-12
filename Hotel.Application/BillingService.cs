using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Hotel.Contracts;

namespace Hotel.Application
{
    public sealed class BillingService : IBilling
    {
        private readonly IHotelStore store;
        public BillingService(IHotelStore store) { this.store = store; }
        public Booking GetBill(int id) => store.Read(s => Workflow.Booking(s, id));
        public IReadOnlyList<Booking> GetBills() => store.Read(s => s.GetBookings().Select(b => Workflow.Booking(s, b.BId)).ToList());
        public decimal CalculateChange(int id, decimal paidNow)
        {
            Workflow.Money(paidNow); var bill = GetBill(id);
            if (paidNow < bill.Remaining) throw new HotelValidationException("Payment does not cover the remaining balance.");
            return paidNow - bill.Remaining;
        }
        public Booking RecordPayment(int id, decimal amount, string requestId, bool confirmed)
        {
            Workflow.Money(amount); if (amount == 0) throw new HotelValidationException("Payment must be greater than zero.");
            var key = Workflow.Key("payment", requestId, confirmed); var hash = Workflow.Hash(id, amount);
            return store.Transact(s =>
            {
                var db = Workflow.Data(s); if (Workflow.Replay(db, key, hash) != null) return Workflow.Booking(s, id);
                var b = Workflow.Booking(s, id);
                if (b.Status == "CheckedOut" || amount > b.Remaining) throw new HotelConflictException("Payment exceeds the balance or the stay is already closed.");
                db.AddPayment(id, amount); db.SaveOperation(key, new OperationRecord { PayloadHash = hash, BookingId = id, Amount = amount }); return Workflow.Booking(s, id);
            });
        }
        public CheckoutReceipt CompleteCheckout(int id, decimal paidNow) => CompleteCheckout(id, paidNow, Guid.NewGuid().ToString("D"), true);
        public CheckoutReceipt CompleteCheckout(int id, decimal paidNow, string requestId, bool confirmed)
        {
            Workflow.Money(paidNow); var key = Workflow.Key("checkout", requestId, confirmed); var hash = Workflow.Hash(id, paidNow);
            return store.Transact(s =>
            {
                var db = Workflow.Data(s); var prior = Workflow.Replay(db, key, hash);
                if (prior != null) return new CheckoutReceipt { Booking = Workflow.Booking(s, id), PaidNow = paidNow, Change = paidNow - prior.Amount };
                var b = Workflow.Booking(s, id); var room = s.GetRoom(b.RId);
                if (b.Status != "CheckedIn" || room == null || room.BId != id || room.IsBooked != "Yes") throw new HotelConflictException("Only the room's checked-in stay can check out.");
                if (paidNow < b.Remaining) throw new HotelValidationException("Settle the remaining balance before checkout.");
                decimal applied = b.Remaining; if (applied > 0) db.AddPayment(id, applied);
                db.SetStatus(id, "CheckedOut"); room.IsBooked = "No"; room.BId = null; s.UpdateRoom(room);
                db.SaveOperation(key, new OperationRecord { PayloadHash = hash, BookingId = id, Amount = applied });
                return new CheckoutReceipt { Booking = Workflow.Booking(s, id), PaidNow = paidNow, Change = paidNow - applied };
            });
        }
    }
}
