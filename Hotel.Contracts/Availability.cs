using System;
using System.Collections.Generic;

namespace Hotel.Contracts
{
    // A projection deliberately excludes guest details, booking IDs and credentials.
    public sealed class AvailableRoom
    {
        public int RoomId { get; set; }
        public string Category { get; set; }
        public decimal NightlyRate { get; set; }
    }

    public sealed class AvailabilityResult
    {
        public string CheckIn { get; set; }
        public string CheckOut { get; set; }
        public int Nights { get; set; }
        public IReadOnlyList<AvailableRoom> Rooms { get; set; }
        public string RateNote { get; set; }
    }

}
