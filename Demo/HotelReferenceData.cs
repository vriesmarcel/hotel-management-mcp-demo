using System;
using System.Collections.Generic;
using System.Linq;

// The same demo facts are compiled into the embedded app and, later, the shared library.
// These are hotel-specific facts, not universal definitions of room or bed categories.
namespace Hotel.ReferenceData
{
    public static class HotelReferenceData
    {
        public const string RoomTypesUri = "hotel://room-types";
        public const string CancellationUri = "hotel://policies/cancellation/v1";
        public static RoomInformation Rooms() => new RoomInformation
        {
            Uri = RoomTypesUri,
            RoomTypes = new[] {
                new RoomTypeInformation { Category = "Single", Uri = RoomTypesUri + "/Single" },
                new RoomTypeInformation { Category = "Double", Uri = RoomTypesUri + "/Double" },
                new RoomTypeInformation { Category = "Family", Uri = RoomTypesUri + "/Family", MaximumGuests = 5 }
            },
            Beds = new[] {
                new BedInformation { Type = "Single", LengthCm = 200 },
                new BedInformation { Type = "King", LengthCm = 200 },
                new BedInformation { Type = "Queen", LengthCm = 190 }
            },
            Note = "Demo hotel facts. Null occupancy means unspecified. Bed lengths describe bed types; the legacy room inventory does not identify each room's bed configuration. Do not infer a bed type from a room category. Package coverage per person is also unspecified."
        };
        public static RoomTypeInformation RoomType(string category) => Rooms().RoomTypes.FirstOrDefault(r => string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Unknown room category. Read hotel://room-types for the supported categories.");
        public static CancellationPolicy Cancellation() => new CancellationPolicy
        {
            Uri = CancellationUri, Version = "v1", HotelTimeZone = "Europe/Berlin", CheckInTime = "15:00",
            FreeCancellationHours = 48, LateFeeNights = 1, PackagesIncludedInFee = false,
            Description = "Free cancellation at or before 48 elapsed hours before check-in at 15:00 Europe/Berlin. After that deadline, the fee is one night's room rate, excluding food and service packages. The quote gives the exact deadline and fee. This demo describes cancellation terms; it does not execute cancellations or charge money."
        };
    }
    public sealed class RoomInformation
    {
        public string Uri { get; set; }
        public IReadOnlyList<RoomTypeInformation> RoomTypes { get; set; }
        public IReadOnlyList<BedInformation> Beds { get; set; }
        public string Note { get; set; }
    }
    public sealed class RoomTypeInformation
    {
        public string Uri { get; set; }
        public string Category { get; set; }
        public int? MaximumGuests { get; set; }
    }
    public sealed class BedInformation
    {
        public string Type { get; set; }
        public int LengthCm { get; set; }
    }
    public sealed class CancellationPolicy
    {
        public string Uri { get; set; }
        public string Version { get; set; }
        public string HotelTimeZone { get; set; }
        public string CheckInTime { get; set; }
        public int FreeCancellationHours { get; set; }
        public int LateFeeNights { get; set; }
        public bool PackagesIncludedInFee { get; set; }
        public string Description { get; set; }
    }
}
