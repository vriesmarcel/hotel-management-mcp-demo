using Hotel.Contracts;
namespace Hotel.Application
{
    public interface IWorkflowSession
    {
        bool IsAvailable(int roomId, System.DateTime arrival, System.DateTime departure);
        string GetStatus(int bookingId);
        void SetStatus(int bookingId, string status);
        void SaveQuote(Quote quote);
        Quote GetQuote(string quoteId);
        OperationRecord GetOperation(string operationId);
        void SaveOperation(string operationId, OperationRecord operation);
        void AddPayment(int bookingId, decimal amount);
    }
    public sealed class OperationRecord
    {
        public string PayloadHash { get; set; }
        public int BookingId { get; set; }
        public decimal Amount { get; set; }
    }
}
