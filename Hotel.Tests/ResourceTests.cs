using System;
using System.Linq;
using Hotel.Application;
using Hotel.ReferenceData;
namespace Hotel.Tests
{
    internal static partial class Program
    {
        private static void ResourceRules()
        {
            Check(HotelReferenceData.RoomType("family").MaximumGuests == 5,"Family capacity is hotel reference data.");
            Check(HotelReferenceData.RoomType("Double").MaximumGuests == null,"Unspecified occupancy is not invented.");
            Expect<ArgumentException>(()=>HotelReferenceData.RoomType("Unknown"));
            var beds = HotelReferenceData.Rooms().Beds;
            Check(beds.Single(b=>b.Type=="Queen").LengthCm==190 && beds.Where(b=>b.Type!="Queen").All(b=>b.LengthCm==200),"Hotel bed lengths are consistent.");
            var november=CancellationRules.ForStay(new DateTime(2026,11,18),3000);
            Check(november.CheckInAt==new DateTimeOffset(2026,11,18,15,0,0,TimeSpan.FromHours(1)),"Hotel check-in is 15:00 Berlin.");
            Check(november.FreeCancellationUntil==new DateTimeOffset(2026,11,16,15,0,0,TimeSpan.FromHours(1)) && november.LateCancellationFee==3000,"Three-night stay has one room-night cancellation fee.");
            var spring=CancellationRules.ForStay(new DateTime(2027,3,28),123.45m);
            Check(spring.FreeCancellationUntil.Hour==14 && spring.FreeCancellationUntil.Offset==TimeSpan.FromHours(1),"Spring DST uses elapsed hours, not calendar-day subtraction.");
            var fall=CancellationRules.ForStay(new DateTime(2026,10,25),123.45m);
            Check(fall.FreeCancellationUntil.Hour==16 && fall.FreeCancellationUntil.Offset==TimeSpan.FromHours(2),"Autumn DST uses elapsed hours.");
            Check((spring.CheckInAt-spring.FreeCancellationUntil).TotalHours==48 && (fall.CheckInAt-fall.FreeCancellationUntil).TotalHours==48,"Both DST boundaries preserve exactly 48 hours.");
        }
    }
}
