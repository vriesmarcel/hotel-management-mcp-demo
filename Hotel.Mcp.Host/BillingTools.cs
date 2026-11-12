using System.ComponentModel;
using System.Globalization;
using Hotel.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Hotel.Mcp.Host;

internal sealed class BillingTools(IBilling billing)
{
    [McpServerTool(Name = "GetBill", ReadOnly = true, OpenWorld = false), Description("Read the total, recorded payments and remaining balance for a reservation.")]
    public object GetBill(int reservationId) => ToolBoundary.Execute(() => ToolBoundary.Summary(billing.GetBill(reservationId)));
    [McpServerTool(Name = "RecordPayment", Destructive = false, Idempotent = true, OpenWorld = false), Description("Record a confirmed DEMO payment in SQL only. No payment provider or money transfer. Amount must be positive and no greater than the balance. Requires explicit confirmation and a UUID requestId for safe retries.")]
    public object RecordPayment(int reservationId, decimal amount, string requestId, bool confirmed) => ToolBoundary.Execute(() => ToolBoundary.Summary(billing.RecordPayment(reservationId, amount, requestId, confirmed)));
    [McpServerTool(Name = "CompleteCheckout", Destructive = false, Idempotent = true, OpenWorld = false), Description("Settle a checked-in stay and release its room. paidNow must cover the remaining balance; returns change. SQL demo payment only. If already settled, use paidNow=0. Requires explicit confirmation and a UUID requestId for retries.")]
    public object CompleteCheckout(int reservationId, decimal paidNow, string requestId, bool confirmed) => ToolBoundary.Execute<object>(() =>
    {
        var receipt = billing.CompleteCheckout(reservationId, paidNow, requestId, confirmed);
        return new { reservation = ToolBoundary.Summary(receipt.Booking), receipt.PaidNow, receipt.Change, paymentMode = "SQL demo record only" };
    });
}
