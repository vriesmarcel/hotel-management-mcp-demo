using System.ComponentModel;
using System.Globalization;
using Hotel.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Hotel.Mcp.Host;

internal static class ToolBoundary
{
    public static T Execute<T>(Func<T> action)
    {
        try { return action(); }
        catch (HotelValidationException e) { throw new McpException(e.Message); }
        catch (HotelConflictException e) { throw new McpException(e.Message); }
        catch (HotelNotFoundException e) { throw new McpException(e.Message); }
        catch (HotelInfrastructureException) { throw new McpException("Hotel data is unavailable. Check the local database and migrations."); }
    }
    public static DateTime Date(string text)
    {
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            throw new HotelValidationException("Dates must use yyyy-MM-dd, without a time or timezone.");
        return value;
    }
    // Never return addresses, phone numbers or national IDs in general tool results.
    public static object Summary(Booking b) => new { reservationId = b.BId, roomId = b.RId, checkIn = b.CheckIn.ToString("yyyy-MM-dd"), checkOut = b.CheckOut.ToString("yyyy-MM-dd"), status = b.Status, total = b.Total, paid = b.Advance, remaining = b.Remaining, cancellation = b.Cancellation };
}
