using System.ComponentModel;
using System.Globalization;
using Hotel.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Hotel.Mcp.Host;

internal sealed class StayTools(IStays stays)
{
    [McpServerTool(Name = "GetArrivals", ReadOnly = true, OpenWorld = false), Description("List reserved arrivals for a hotel-local date (yyyy-MM-dd), with reservation IDs and rooms.")]
    public object GetArrivals(string date) => ToolBoundary.Execute<object>(() => stays.GetArrivals(ToolBoundary.Date(date)).Select(ToolBoundary.Summary).ToArray());
    [McpServerTool(Name = "CheckIn", Destructive = false, Idempotent = true, OpenWorld = false), Description("Check in a confirmed reservation during its stay dates, marking the room occupied. Requires explicit confirmation and a UUID requestId for safe retries.")]
    public object CheckIn(int reservationId, string requestId, bool confirmed) => ToolBoundary.Execute(() => ToolBoundary.Summary(stays.CheckIn(reservationId, requestId, confirmed)));
}
