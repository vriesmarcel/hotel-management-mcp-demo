using System;
using Hotel.Contracts;
using Hotel.ReferenceData;

namespace Hotel.Application
{
    public static class CancellationRules
    {
        public static CancellationTerms ForStay(DateTime arrival, decimal roomNightlyRate)
        {
            var policy = HotelReferenceData.Cancellation();
            TimeZoneInfo zone;
            // Windows .NET Framework uses Windows IDs; modern .NET also accepts IANA.
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(policy.HotelTimeZone); }
            catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
            var local = DateTime.SpecifyKind(arrival.Date.Add(TimeSpan.Parse(policy.CheckInTime, System.Globalization.CultureInfo.InvariantCulture)), DateTimeKind.Unspecified);
            var checkIn = new DateTimeOffset(local, zone.GetUtcOffset(local));
            // Subtract elapsed hours on the UTC timeline, then convert back for display.
            // Across DST this is not necessarily 15:00 two calendar days earlier.
            var deadline = TimeZoneInfo.ConvertTime(checkIn.ToUniversalTime().AddHours(-policy.FreeCancellationHours), zone);
            return new CancellationTerms { PolicyUri = policy.Uri, PolicyVersion = policy.Version, HotelTimeZone = policy.HotelTimeZone,
                CheckInAt = checkIn, FreeCancellationUntil = deadline, LateCancellationFee = roomNightlyRate * policy.LateFeeNights };
        }
    }
}
