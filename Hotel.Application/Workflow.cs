using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Hotel.Contracts;

namespace Hotel.Application
{
    internal static class Workflow
    {
        public static IWorkflowSession Data(IHotelSession session) => (IWorkflowSession)session;
        public static Booking Booking(IHotelSession s, int id)
        {
            var booking = s.GetBooking(id) ?? throw new HotelNotFoundException("Reservation was not found.");
            booking.Status = Data(s).GetStatus(id);
            booking.Cancellation = Data(s).GetCancellation(id);
            return booking;
        }
        public static string Key(string context, string requestId, bool confirmed)
        {
            if (!confirmed) throw new HotelValidationException("Explicit confirmation of these details is required.");
            if (!Guid.TryParse(requestId, out var id)) throw new HotelValidationException("requestId must be a UUID. Reuse it only for retries of the same request.");
            return context + ":" + id.ToString("D");
        }
        public static string Hash(params object[] fields)
        {
            var text = string.Concat(fields.Select(x => { string value = Convert.ToString(x, CultureInfo.InvariantCulture) ?? ""; return value.Length + ":" + value; }));
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }
        public static OperationRecord Replay(IWorkflowSession s, string key, string hash)
        {
            var prior = s.GetOperation(key);
            if (prior != null && prior.PayloadHash != hash) throw new HotelConflictException("This requestId was already used with different details.");
            return prior;
        }
        public static void DateRange(DateTime start, DateTime end)
        {
            if (start.TimeOfDay != TimeSpan.Zero || end.TimeOfDay != TimeSpan.Zero || start.Kind != DateTimeKind.Unspecified || end.Kind != DateTimeKind.Unspecified || (end - start).Days < 1 || (end - start).Days > 365)
                throw new HotelValidationException("Use hotel-local dates for one to 365 nights, with exclusive check-out.");
        }
        public static void Money(decimal value)
        {
            if (value < 0 || value > 100000000m || decimal.Round(value, 2) != value) throw new HotelValidationException("Amount must be non-negative, at most 100000000, and have at most two decimal places.");
        }
    }
}
