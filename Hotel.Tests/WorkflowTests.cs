using System;
using System.Linq;
using System.Threading.Tasks;
using Hotel.Application;
using Hotel.Contracts;
using Hotel.Sql;

namespace Hotel.Tests
{
    internal static partial class Program
    {
        private static void WorkflowTests()
        {
            var store = new SqlHotelStore(ConnectionString);
            var hotel = new HotelServices(store);
            var reservations = new ReservationsService(store);
            var stays = new StaysService(store);
            var billing = new BillingService(store);
            var today = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
            var room = hotel.SaveRoom(null,"O'Brien suite","No",100.25m);
            var food = hotel.SaveFoodPackage(null,"Daily food",20.50m);
            var service = hotel.SaveServicePackage(null,"Once service",5.75m);
            Check(room.RId > 100 && food.FId > 14 && service.SId > 25,"Sequences start above existing IDs.");
            Check(hotel.GetRooms("O'").Single().RId==room.RId,"Search is parameterized.");
            Check(hotel.GetRooms("%").Count==0,"Search wildcards are literal.");
            Check(hotel.Authenticate("test-manager",TestPassword).Role=="Manager","Existing desktop authentication works.");
            Check(hotel.Authenticate("' OR 1=1 --",TestPassword)==null,"Login is parameterized.");
            var stay = new QuoteRequest { RoomId=room.RId,FoodPackageId=food.FId,ServicePackageId=service.SId,CheckIn=today,CheckOut=today.AddDays(3) };
            var quote = reservations.CalculateQuote(stay,18);
            Check(quote.Total==368 && quote.Remaining==350 && quote.ExpiresUtc>DateTime.UtcNow,"Persisted quote includes correct pricing and expiry.");
            Check(quote.Cancellation.LateCancellationFee==100.25m,"Cancellation fee excludes nightly food and once-only services.");
            var persistedQuote=store.Read(s=>((IWorkflowSession)s).GetQuote(quote.QuoteId));
            Check(persistedQuote.Cancellation.FreeCancellationUntil==quote.Cancellation.FreeCancellationUntil && persistedQuote.Cancellation.PolicyVersion=="v1","Quoted cancellation terms round-trip through SQL.");
            var request = new ReservationRequest { QuoteId=quote.QuoteId,Guest=Guest(),Advance=18,RequestId=Guid.NewGuid().ToString(),Confirmed=false };
            Expect<HotelValidationException>(()=>reservations.CreateReservation(request));
            request.Confirmed=true;
            var booking = reservations.CreateReservation(request);
            Check(booking.Status=="Reserved" && hotel.GetRooms().Single(r=>r.RId==room.RId).IsBooked=="No","Booking does not immediately occupy a room.");
            Check(reservations.CreateReservation(request).BId==booking.BId,"Booking retry returns the same ID.");
            var savedTerms=reservations.GetReservation(booking.BId).Cancellation;
            Check(savedTerms.PolicyUri==quote.Cancellation.PolicyUri && savedTerms.LateCancellationFee==100.25m,"Reservation stores accepted policy version and fee.");
            Check((int)Scalar("SELECT COUNT(*) FROM dbo.HotelCancellationTerms WHERE BId="+booking.BId)==1,"Idempotent booking has one accepted policy snapshot.");
            request.Advance=19;
            Expect<HotelConflictException>(()=>reservations.CreateReservation(request));
            request.Advance=18;
            Check(!reservations.GetAvailableRooms(today,today.AddDays(3)).Rooms.Any(r=>r.RoomId==room.RId),"Overlapping reservation excludes room.");
            Check(reservations.GetAvailableRooms(today.AddDays(3),today.AddDays(4)).Rooms.Any(r=>r.RoomId==room.RId),"Exclusive checkout allows adjacent stay.");
            Check(stays.GetArrivals(today).Any(b=>b.BId==booking.BId),"Reserved stay appears in arrivals.");
            Expect<HotelConflictException>(()=>billing.CompleteCheckout(booking.BId,350,Guid.NewGuid().ToString(),true));
            var checkinId=Guid.NewGuid().ToString();
            Check(stays.CheckIn(booking.BId,checkinId,true).Status=="CheckedIn","Check-in changes lifecycle.");
            Check(stays.CheckIn(booking.BId,checkinId,true).BId==booking.BId,"Check-in retry succeeds.");
            Check(hotel.GetRooms().Single(r=>r.RId==room.RId).BId==booking.BId,"Check-in links active occupancy.");
            var paymentId=Guid.NewGuid().ToString();
            Check(billing.RecordPayment(booking.BId,50,paymentId,true).Remaining==300,"Payment reduces balance.");
            Check(billing.RecordPayment(booking.BId,50,paymentId,true).Remaining==300,"Payment retry does not charge twice.");
            Expect<HotelConflictException>(()=>billing.RecordPayment(booking.BId,51,paymentId,true));
            Expect<HotelConflictException>(()=>billing.RecordPayment(booking.BId,301,Guid.NewGuid().ToString(),true));
            Expect<HotelValidationException>(()=>billing.RecordPayment(booking.BId,1.001m,Guid.NewGuid().ToString(),true));
            Expect<HotelValidationException>(()=>billing.CompleteCheckout(booking.BId,299,Guid.NewGuid().ToString(),true));
            Execute("CREATE TRIGGER dbo.TestRoomFailure ON dbo.Room AFTER UPDATE AS THROW 51001,'Synthetic failure',1;");
            try
            {
                Expect<HotelInfrastructureException>(()=>billing.CompleteCheckout(booking.BId,300,Guid.NewGuid().ToString(),true));
                Check(billing.GetBill(booking.BId).Remaining==300 && billing.GetBill(booking.BId).Status=="CheckedIn","Failed room release rolls back payment and lifecycle.");
            }
            finally { Execute("DROP TRIGGER dbo.TestRoomFailure;"); }
            var checkoutId=Guid.NewGuid().ToString();
            var receipt=billing.CompleteCheckout(booking.BId,350,checkoutId,true);
            Check(receipt.Change==50 && receipt.Booking.Remaining==0 && receipt.Booking.Status=="CheckedOut","Checkout settles balance and returns change.");
            Check(billing.CompleteCheckout(booking.BId,350,checkoutId,true).Change==50,"Checkout retry preserves original receipt change.");
            Check(hotel.GetRooms().Single(r=>r.RId==room.RId).BId==null,"Checkout clears active room reference.");
            Expect<HotelConflictException>(()=>billing.RecordPayment(booking.BId,1,Guid.NewGuid().ToString(),true));

            var raceRoom=hotel.SaveRoom(null,"Race room","No",90);
            var raceStay=new QuoteRequest { RoomId=raceRoom.RId,CheckIn=today,CheckOut=today.AddDays(2) };
            var q1=reservations.CalculateQuote(raceStay); var q2=reservations.CalculateQuote(raceStay);
            var requests=new[] { q1,q2 }.Select(q=>new ReservationRequest { QuoteId=q.QuoteId,Guest=Guest(),Confirmed=true,RequestId=Guid.NewGuid().ToString() }).ToArray();
            var tasks=requests.Select(r=>Task.Run(()=> { try { return reservations.CreateReservation(r).BId; } catch(HotelConflictException) { return -1; } })).ToArray();
            Task.WaitAll(tasks);
            Check(tasks.Count(t=>t.Result>0)==1,"Concurrent overlapping bookings have exactly one winner.");
            var retryRoom=hotel.SaveRoom(null,"Retry room","No",50);
            raceStay.RoomId=retryRoom.RId;
            var rq=reservations.CalculateQuote(raceStay);
            var rr=new ReservationRequest { QuoteId=rq.QuoteId,Guest=Guest(),Confirmed=true,RequestId=Guid.NewGuid().ToString() };
            var retries=Enumerable.Range(0,4).Select(_=>Task.Run(()=>reservations.CreateReservation(rr).BId)).ToArray();
            Task.WaitAll(retries);
            Check(retries.Select(t=>t.Result).Distinct().Count()==1,"Concurrent identical retries create one reservation.");
            var expiryRoom=hotel.SaveRoom(null,"Expiry room","No",40); raceStay.RoomId=expiryRoom.RId;
            var expired=reservations.CalculateQuote(raceStay);
            Execute("UPDATE dbo.HotelQuotes SET ExpiresUtc=DATEADD(minute,-1,SYSUTCDATETIME());");
            Expect<HotelConflictException>(()=>reservations.CreateReservation(new ReservationRequest { QuoteId=expired.QuoteId,Guest=Guest(),Confirmed=true,RequestId=Guid.NewGuid().ToString() }));
            var oldQuote=reservations.CalculateQuote(raceStay);
            Execute("DELETE FROM dbo.HotelCancellationTerms WHERE QuoteId='"+oldQuote.QuoteId+"';");
            Expect<HotelConflictException>(()=>reservations.CreateReservation(new ReservationRequest { QuoteId=oldQuote.QuoteId,Guest=Guest(),Confirmed=true,RequestId=Guid.NewGuid().ToString() }));
            var changed=reservations.CalculateQuote(raceStay);
            hotel.SaveRoom(expiryRoom.RId,expiryRoom.Category,"No",41);
            Expect<HotelConflictException>(()=>reservations.CreateReservation(new ReservationRequest { QuoteId=changed.QuoteId,Guest=Guest(),Confirmed=true,RequestId=Guid.NewGuid().ToString() }));
            var future=reservations.CalculateQuote(new QuoteRequest { RoomId=expiryRoom.RId,CheckIn=today.AddDays(5),CheckOut=today.AddDays(6) });
            var fb=reservations.CreateReservation(new ReservationRequest { QuoteId=future.QuoteId,Guest=Guest(),Confirmed=true,RequestId=Guid.NewGuid().ToString() });
            Expect<HotelValidationException>(()=>stays.CheckIn(fb.BId,Guid.NewGuid().ToString(),true));
            Expect<HotelValidationException>(()=>reservations.GetAvailableRooms(today,today));
            Expect<HotelValidationException>(()=>reservations.GetAvailableRooms(today,today.AddDays(366)));
            Expect<InvalidOperationException>(()=>store.Read(s=>s.InsertRoom("Bad write","No",1)));
            EmptyTableTests(hotel);
        }
    }
}
