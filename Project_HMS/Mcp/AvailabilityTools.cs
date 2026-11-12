using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Project_HMS.Mcp
{
    // Use the original DataAccess directly: refactoring is not required for MCP.
    // Later demos can move rules and SQL into services without changing the tool contract.
    [McpServerToolType]
    public sealed class AvailabilityTools
    {
        [McpServerTool(Name = "GetAvailableRooms", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
        [Description("Find available hotel rooms for one to 365 nights. Dates are hotel-local yyyy-MM-dd. Check-in is inclusive and check-out is exclusive. Returns room IDs, categories and nightly rates. Does not hold or book a room.")]
        public AvailabilityResult GetAvailableRooms(
            [Description("Inclusive check-in date, yyyy-MM-dd, with no time or timezone.")] string checkIn,
            [Description("Exclusive check-out date, yyyy-MM-dd, after check-in.")] string checkOut)
        {
            var arrival = ParseDate(checkIn); // Set your live-demo breakpoint here.
            var departure = ParseDate(checkOut);
            var nights = (departure - arrival).Days;
            if (nights < 1 || nights > 365)
                throw new McpException("Check-out must be after check-in. Maximum stay is 365 nights.");

            const string sql = @"
SELECT r.RId,r.Category,r.RoomCost
FROM dbo.Room r
WHERE r.IsBooked IN (N'Yes',N'No') AND r.RoomCost >= 0
  AND NOT EXISTS (
    SELECT 1 FROM dbo.Booking b WHERE b.RId = r.RId
      AND (b.CheckOut <= b.CheckIn
           OR (b.CheckIn < @checkOut AND b.CheckOut > @checkIn)))
  AND (r.IsBooked = N'No' OR EXISTS (
    SELECT 1 FROM dbo.Booking active
    WHERE active.BId = r.BId AND active.RId = r.RId
      AND active.CheckOut > active.CheckIn))
ORDER BY r.RId;";
            try
            {
                var data = new DataAccess(); // Original connection, scoped to this request.
                using (var connection = data.Sqlcon)
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                using (var command = new SqlCommand(sql, connection, transaction))
                {
                    command.CommandTimeout = 15;
                    command.Parameters.Add("@checkIn", SqlDbType.Date).Value = arrival;
                    command.Parameters.Add("@checkOut", SqlDbType.Date).Value = departure;
                    var rooms = new List<AvailableRoom>();
                    using (var reader = command.ExecuteReader())
                        while (reader.Read()) rooms.Add(new AvailableRoom
                        {
                            RoomId = reader.GetInt32(0), Category = reader.GetString(1),
                            NightlyRate = Convert.ToDecimal(reader.GetDouble(2))
                        });
                    transaction.Commit();
                    return new AvailabilityResult
                    {
                        CheckIn = checkIn, CheckOut = checkOut, Nights = nights, Rooms = rooms,
                        RateNote = "Nightly room rates only. The legacy database does not record currency. Availability does not hold or reserve a room."
                    };
                }
            }
            catch (SqlException) { throw new McpException("Availability could not be read. Check the local database configuration and schema."); }
        }

        private static DateTime ParseDate(string value)
        {
            if (value == null || value.Length != 10 || !DateTime.TryParseExact(value, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
                throw new McpException("Dates must use yyyy-MM-dd without a time or timezone.");
            return result;
        }
    }

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
        public List<AvailableRoom> Rooms { get; set; }
        public string RateNote { get; set; }
    }
}
