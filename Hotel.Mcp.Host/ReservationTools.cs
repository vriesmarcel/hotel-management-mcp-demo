using System.ComponentModel;
using System.Globalization;
using Hotel.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Hotel.Mcp.Host;

internal sealed class ReservationTools(IReservations reservations, IPackageService packages)
{
    [McpServerTool(Name = "GetAvailableRooms", ReadOnly = true, OpenWorld = false), Description("Find rooms available for the entire date window. Check-out is exclusive; dates use yyyy-MM-dd. Returns room IDs, categories, nightly rates, room information and cancellation policy. Read the linked resources when supported. Availability is not a hold.")]
    public AvailabilityResult GetAvailableRooms(string checkIn, string checkOut) =>
        ToolBoundary.Execute(() => reservations.GetAvailableRooms(ToolBoundary.Date(checkIn), ToolBoundary.Date(checkOut)));

    [McpServerTool(Name = "GetPackages", ReadOnly = true, OpenWorld = false), Description("List food packages (charged nightly) and service packages (charged once). ID 0 means no package.")]
    public object GetPackages() => ToolBoundary.Execute<object>(() => new { food = packages.GetFoodPackages(), services = packages.GetServicePackages() });

    [McpServerTool(Name = "CalculateQuote", Destructive = false, OpenWorld = false), Description("Create a server-priced quote valid for 15 minutes. Does not hold inventory. Includes the exact cancellation deadline and one-room-night late fee, excluding packages. Show all quoted terms and obtain confirmation before booking. Currency is not recorded in this legacy database.")]
    public Quote CalculateQuote(int roomId, string checkIn, string checkOut, int foodPackageId = 0, int servicePackageId = 0, decimal advance = 0) =>
        ToolBoundary.Execute(() => reservations.CalculateQuote(new QuoteRequest { RoomId = roomId, CheckIn = ToolBoundary.Date(checkIn), CheckOut = ToolBoundary.Date(checkOut), FoodPackageId = foodPackageId, ServicePackageId = servicePackageId }, advance));

    [McpServerTool(Name = "CreateReservation", Destructive = false, Idempotent = true, OpenWorld = false), Description("Reserve the quoted room after the guest explicitly confirms stay details, total, advance and cancellation deadline/fee. The accepted policy version and terms are stored with the reservation. Requires the quoteId and a new UUID requestId; reuse that ID only for identical retries. Use synthetic guest details for the demo. This records an advance in SQL, without moving money. Check-in is a separate context.")]
    public object CreateReservation(string quoteId, string guestName, string address, string phone, string nationalId, decimal advance, string requestId, bool confirmed) =>
        ToolBoundary.Execute(() => ToolBoundary.Summary(reservations.CreateReservation(new ReservationRequest { QuoteId = quoteId, Guest = new GuestDetails { Name = guestName, Address = address, Phone = phone, NationalId = nationalId }, Advance = advance, RequestId = requestId, Confirmed = confirmed })));

    [McpServerTool(Name = "GetReservation", ReadOnly = true, OpenWorld = false), Description("Retrieve a reservation's stay, status and financial summary by reservation ID.")]
    public object GetReservation(int reservationId) => ToolBoundary.Execute(() => ToolBoundary.Summary(reservations.GetReservation(reservationId)));
}
