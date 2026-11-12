using System.Reflection;
using Hotel.Contracts;
using ModelContextProtocol.Server;
using ModelContextProtocol.Extensions.Apps;

namespace Hotel.Mcp.Host;

internal sealed class ContextCatalog
{
    public string Name { get; }
    public string Instructions { get; }
    public McpServerPrimitiveCollection<McpServerTool> Tools { get; } = new();
    public McpServerResourceCollection Resources { get; } = new();
    public McpServerPrimitiveCollection<McpServerPrompt> Prompts { get; } = new();

    public ContextCatalog(string name, string instructions, object tools)
    {
        Name = name; Instructions = instructions;
        foreach (var method in tools.GetType().GetMethods())
            if (method.GetCustomAttribute<McpServerToolAttribute>() != null)
                Tools.Add(McpServerTool.Create(method, tools));
        // These catalogs are route-specific, so apply the Apps attributes explicitly.
        McpApps.ApplyAppUiAttributes(Tools);
    }
}

internal sealed class ContextCatalogs
{
    private readonly Dictionary<string, ContextCatalog> contexts;
    public ContextCatalogs(IReservations reservations, IStays stays, IBilling billing, IPackageService packages)
    {
        var reservation = new ContextCatalog("hotel-reservations",
            "Availability, quotes and reservations only. Read the reservation policy. Quote before booking. Show the guest the exact stay, total and advance and obtain explicit confirmation before CreateReservation. Reuse requestId only when retrying identical details. No check-in or checkout tools exist on this endpoint.",
            new ReservationTools(reservations, packages));
        reservation.Resources.Add(McpServerResource.Create((Func<string>)ReservationGuidance.Policy,
            new McpServerResourceCreateOptions { Name = "ReservationPolicy", UriTemplate = "hotel://reservation-policy", MimeType = "text/plain" }));
        reservation.Resources.Add(ReservationApp.CreateResource());
        reservation.Prompts.Add(McpServerPrompt.Create((Func<string, string>)ReservationGuidance.PlanReservation,
            new McpServerPromptCreateOptions { Name = "PlanReservation", Description = "Plan a hotel stay using availability, packages and a confirmed quote." }));
        contexts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["/mcp/reservations"] = reservation,
            ["/mcp/stays"] = new("hotel-stays", "Arrival and check-in only. Confirm the reservation ID before CheckIn. Use hotel-local dates. Reuse requestId only for identical retries.", new StayTools(stays)),
            ["/mcp/billing"] = new("hotel-billing", "Bills, demo payment records and checkout only. Payments are SQL records; no money moves. Show the balance and obtain explicit confirmation before recording payment or checkout. Reuse requestId for identical retries.", new BillingTools(billing))
        };
    }
    public ContextCatalog ForPath(string path) => contexts[path.TrimEnd('/')];
}
