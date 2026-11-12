using System;
using System.Data;
using Microsoft.Data.SqlClient;
using Hotel.Application;
using Hotel.Contracts;
namespace Hotel.Sql
{
    public sealed partial class SqlHotelStore
    {
        private sealed partial class Session
        {
            public bool IsAvailable(int roomId, DateTime arrival, DateTime departure)
            {
                using (var command = Command(@"SELECT COUNT(*) FROM dbo.Room r WHERE r.RId=@id AND r.RoomCost>=0 AND r.IsBooked IN(N'Yes',N'No')
AND NOT EXISTS(SELECT 1 FROM dbo.Booking b WHERE b.RId=r.RId AND NOT EXISTS(SELECT 1 FROM dbo.HotelBookingLifecycle l WHERE l.BId=b.BId AND l.Status=N'CheckedOut') AND (b.CheckOut<=b.CheckIn OR (b.CheckIn<@end AND b.CheckOut>@start)))
AND (r.IsBooked=N'No' OR EXISTS(SELECT 1 FROM dbo.Booking a WHERE a.BId=r.BId AND a.RId=r.RId AND a.CheckOut>a.CheckIn));", Id("@id", roomId), Date("@start", arrival), Date("@end", departure)))
                    return (int)command.ExecuteScalar() == 1;
            }
            public string GetStatus(int bookingId)
            {
                using (var command = Command("SELECT COALESCE((SELECT Status FROM dbo.HotelBookingLifecycle WHERE BId=@id),CASE WHEN EXISTS(SELECT 1 FROM dbo.Room WHERE BId=@id AND IsBooked=N'Yes') THEN N'CheckedIn' ELSE N'Reserved' END);", Id("@id", bookingId)))
                    return (string)command.ExecuteScalar();
            }
            public void SetStatus(int bookingId, string status) => Write(@"UPDATE dbo.HotelBookingLifecycle SET Status=@status WHERE BId=@id;
IF @@ROWCOUNT=0 INSERT dbo.HotelBookingLifecycle(BId,Status) VALUES(@id,@status);", Id("@id", bookingId), Text("@status", status, 20));
            private static SqlParameter Decimal(string name, decimal value) => new SqlParameter(name, SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = value };
            public void AddPayment(int bookingId, decimal amount) => WriteOne("UPDATE dbo.Booking SET Advance=CONVERT(decimal(18,2),Advance)+@amount,Remaining=CONVERT(decimal(18,2),Remaining)-@amount WHERE BId=@id AND CONVERT(decimal(18,2),Remaining)>=@amount;", Id("@id", bookingId), Decimal("@amount", amount));
            public void SaveQuote(Quote q)
            {
                WriteOne(@"INSERT dbo.HotelQuotes(QuoteId,RId,FId,SId,CheckIn,CheckOut,Total,ExpiresUtc)
VALUES(@quote,@room,@food,@service,@start,@end,@total,@expires);", new SqlParameter("@quote", SqlDbType.UniqueIdentifier) { Value = Guid.Parse(q.QuoteId) }, Id("@room", q.Stay.RoomId), Id("@food", q.Stay.FoodPackageId), Id("@service", q.Stay.ServicePackageId), Date("@start", q.Stay.CheckIn), Date("@end", q.Stay.CheckOut), Decimal("@total", q.Total), new SqlParameter("@expires", SqlDbType.DateTime2) { Value = q.ExpiresUtc });
                var c = q.Cancellation;
                WriteOne(@"INSERT dbo.HotelCancellationTerms(QuoteId,PolicyUri,PolicyVersion,HotelTimeZone,CheckInAt,FreeCancellationUntil,LateCancellationFee)
VALUES(@quote,@uri,@version,@zone,@checkin,@deadline,@fee);", new SqlParameter("@quote", SqlDbType.UniqueIdentifier) { Value = Guid.Parse(q.QuoteId) },
                    Text("@uri",c.PolicyUri,200), Text("@version",c.PolicyVersion,30), Text("@zone",c.HotelTimeZone,80),
                    new SqlParameter("@checkin",SqlDbType.DateTimeOffset) { Value=c.CheckInAt }, new SqlParameter("@deadline",SqlDbType.DateTimeOffset) { Value=c.FreeCancellationUntil }, Decimal("@fee",c.LateCancellationFee));
            }
            public Quote GetQuote(string id)
            {
                var quote = Single("SELECT QuoteId,RId,FId,SId,CheckIn,CheckOut,Total,ExpiresUtc FROM dbo.HotelQuotes WHERE QuoteId=@id;", r => new Quote
            {
                QuoteId = r.GetGuid(0).ToString("D"),
                Stay = new QuoteRequest { RoomId = r.GetInt32(1), FoodPackageId = r.GetInt32(2), ServicePackageId = r.GetInt32(3), CheckIn = r.GetDateTime(4), CheckOut = r.GetDateTime(5) },
                Days = (r.GetDateTime(5) - r.GetDateTime(4)).Days,
                Total = r.GetDecimal(6),
                ExpiresUtc = DateTime.SpecifyKind(r.GetDateTime(7), DateTimeKind.Utc)
            }, new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = Guid.Parse(id) });
                if (quote != null) quote.Cancellation = ReadCancellation("QuoteId=@id", new SqlParameter("@id",SqlDbType.UniqueIdentifier) { Value=Guid.Parse(id) });
                return quote;
            }
            private CancellationTerms ReadCancellation(string predicate, SqlParameter parameter) => Single("SELECT PolicyUri,PolicyVersion,HotelTimeZone,CheckInAt,FreeCancellationUntil,LateCancellationFee FROM dbo.HotelCancellationTerms WHERE " + predicate,
                r => new CancellationTerms { PolicyUri=r.GetString(0),PolicyVersion=r.GetString(1),HotelTimeZone=r.GetString(2),CheckInAt=r.GetDateTimeOffset(3),FreeCancellationUntil=r.GetDateTimeOffset(4),LateCancellationFee=r.GetDecimal(5) }, parameter);
            public CancellationTerms GetCancellation(int bookingId) => ReadCancellation("BId=@id",Id("@id",bookingId));
            public void AcceptCancellation(string quoteId,int bookingId) => WriteOne("UPDATE dbo.HotelCancellationTerms SET BId=@booking WHERE QuoteId=@quote AND BId IS NULL;", Id("@booking",bookingId), new SqlParameter("@quote",SqlDbType.UniqueIdentifier) { Value=Guid.Parse(quoteId) });
            public OperationRecord GetOperation(string id) => Single("SELECT PayloadHash,BId,Amount FROM dbo.HotelOperations WHERE OperationId=@id;", r => new OperationRecord { PayloadHash = r.GetString(0), BookingId = r.GetInt32(1), Amount = r.GetDecimal(2) }, Text("@id", id, 100));
            public void SaveOperation(string id, OperationRecord record) => WriteOne("INSERT dbo.HotelOperations(OperationId,PayloadHash,BId,Amount) VALUES(@id,@hash,@booking,@amount);", Text("@id", id, 100), Text("@hash", record.PayloadHash, 64), Id("@booking", record.BookingId), Decimal("@amount", record.Amount));
        }
    }
}
