using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Hotel.Application;
using Hotel.Contracts;
using Hotel.Sql;

namespace Hotel.Tests
{
    internal static class Program
    {
        private static int assertions;
        internal static string ConnectionString;
        internal static string TestPassword;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Boundaries();
                UnitRules();
                if (args.Contains("--integration"))
                {
                    ConnectionString = Environment.GetEnvironmentVariable("HMS_TEST_CONNECTION_STRING");
                    var marker = Environment.GetEnvironmentVariable("HMS_TEST_DATABASE_MARKER");
                    var builder = new SqlConnectionStringBuilder(ConnectionString ?? "");
                    Check(builder.InitialCatalog.StartsWith("Project_HMS_RefactorTest_", StringComparison.Ordinal),
                        "Integration tests require an isolated database name.");
                    Check(!string.IsNullOrWhiteSpace(marker) && Equals(Scalar(
                        "SELECT CONVERT(nvarchar(100),value) FROM sys.extended_properties WHERE class=0 AND name=N'HotelRefactoringTest';"), marker),
                        "Integration tests require the provisioner's ownership marker.");
                    Seed();
                    Integration();
                    Console.WriteLine("PASS: SQL pricing, lifecycle, rollback, concurrent IDs and conflict checks.");
                    DesktopTests.Run(new HotelServices(new SqlHotelStore(ConnectionString)));
                }
                Console.WriteLine("PASS: " + assertions + " assertions" + (args.Contains("--integration") ? " including SQL integration and WinForms workflows." : " (boundaries and application rules)."));
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL: " + exception.GetType().Name + ": " + exception.Message);
                return 1;
            }
        }

        internal static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            assertions++;
        }

        internal static void Expect<T>(Action operation) where T : Exception
        {
            try { operation(); }
            catch (T) { assertions++; return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }

        private static void Boundaries()
        {
            var contracts = typeof(IRoomService).Assembly;
            Check(!contracts.GetReferencedAssemblies().Any(a => a.Name == "System.Data" || a.Name == "System.Windows.Forms"),
                "Contracts must not reference SQL or controls.");
            Check(!typeof(HotelServices).Assembly.GetReferencedAssemblies().Any(a => a.Name == "Hotel.Sql" || a.Name == "System.Data" || a.Name == "System.Windows.Forms"),
                "Application must depend on ports, not SQL or forms.");
            foreach (var type in contracts.GetExportedTypes())
            foreach (var property in type.GetProperties())
                Check(property.PropertyType.Namespace != "System.Data", "Contract properties must be typed models.");
            foreach (var form in typeof(Project_HMS.Login).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(System.Windows.Forms.Form))))
                Check(!form.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Any(f => f.FieldType.Namespace == "Hotel.Sql" || f.FieldType.Namespace == "Hotel.Application" || f.FieldType.Namespace == "System.Data.SqlClient"),
                    "Forms must not own persistence or application implementations.");
        }

        private static void UnitRules()
        {
            var store = new QuoteStore();
            var hotel = new HotelServices(store);
            var stay = Stay(1, 1, 1);
            var quote = hotel.CalculateQuote(stay, 20m);
            Check(quote.Days == 3 && quote.Total == 365m && quote.Remaining == 345m, "Room and food are daily; service is once.");
            Check(hotel.CalculateRemaining(stay, 20m) == 345m, "Remaining is calculated centrally.");
            stay.CheckOut = stay.CheckIn;
            Expect<HotelValidationException>(() => hotel.CalculateQuote(stay));
            stay.CheckOut = stay.CheckIn.AddDays(-1);
            Expect<HotelValidationException>(() => hotel.CalculateQuote(stay));
            stay.CheckOut = stay.CheckIn.AddDays(3);
            Expect<HotelValidationException>(() => hotel.CalculateQuote(stay, -1));
            Expect<HotelValidationException>(() => hotel.CalculateQuote(stay, 366));
            Expect<HotelValidationException>(() => hotel.CreateReservation(new ReservationRequest { Stay = stay }));
            foreach (var field in typeof(GuestDetails).GetProperties())
            {
                var guest = Guest();
                field.SetValue(guest, "");
                Expect<HotelValidationException>(() => hotel.CreateReservation(new ReservationRequest { Stay = stay, Guest = guest }));
            }
            var longGuest = Guest();
            longGuest.Name = new string('x', 51);
            Expect<HotelValidationException>(() => hotel.CreateReservation(new ReservationRequest { Stay = stay, Guest = longGuest }));
            store.Session.MissingFood = true;
            Expect<HotelNotFoundException>(() => hotel.CalculateQuote(stay));
            Check(store.Transactions == 0, "Invalid guest requests must fail before persistence writes.");
            Expect<HotelValidationException>(() => hotel.GetRooms(new string('x', 51)));
            Expect<HotelInfrastructureException>(() => new SqlHotelStore("InvalidDatabaseOption=value"));
        }

        private static void Seed()
        {
            TestPassword = Guid.NewGuid().ToString("N");
            Execute("INSERT dbo.Login (UId,Username,Password,Role) VALUES (1,N'test-manager',@password,N'Manager'),(2,N'test-owner',@password,N'Owner');",
                new SqlParameter("@password", TestPassword));
        }

        private static void Integration()
        {
            var store = new SqlHotelStore(ConnectionString);
            var hotel = new HotelServices(store);
            var room = hotel.SaveRoom(null, "O'Brien suite", "No", 100.25m);
            Check(room.RId > 100, "Sequence must be initialized above existing room IDs.");
            var food = hotel.SaveFoodPackage(null, "Chef's meal", 20.5m);
            var service = hotel.SaveServicePackage(null, "Spa's package", 5.75m);
            Check(food.FId > 14 && service.SId > 25, "Package sequences must start above existing IDs.");
            Check(hotel.GetRooms("O'").Single().RId == room.RId, "Apostrophe search must be parameterized.");
            Check(hotel.GetRooms("%").Count == 0 && hotel.GetRooms("_").Count == 0 && hotel.GetRooms("[").Count == 0,
                "Search prefix wildcard characters must be literal.");
            Check(hotel.Authenticate("test-manager", "bad") == null, "Invalid credentials must fail.");
            Check(hotel.Authenticate("' OR 1=1 --", TestPassword) == null, "Login must not interpret SQL input.");
            Check(hotel.Authenticate("test-manager", TestPassword).Role == "Manager", "Login can succeed after a failed attempt.");
            Check(hotel.Authenticate("test-owner", TestPassword).Role == "Owner", "Authentication must retain the stored role.");

            var stay = Stay(room.RId, food.FId, service.SId);
            Check(hotel.CalculateQuote(stay).Total == 368m, "SQL float mappings must preserve the tested fractional quote.");
            var booking = hotel.CreateReservation(new ReservationRequest { Stay = stay, Guest = Guest(), Advance = 18m });
            Check(booking.Total == 368m && booking.Remaining == 350m, "Booking must calculate authoritative pricing.");
            Check(hotel.GetRooms().Single(r => r.RId == room.RId).BId == booking.BId, "Booking must link the room in the same transaction.");
            Check(hotel.GetBills().Single(b => b.BId == booking.BId).CNID == "ID'abc", "Guest strings must round-trip without SQL interpolation.");
            Expect<HotelConflictException>(() => hotel.CreateReservation(new ReservationRequest { Stay = stay, Guest = Guest(), Advance = 0 }));
            Expect<HotelConflictException>(() => hotel.SaveRoom(room.RId, room.Category, "No", room.RoomCost));
            Expect<HotelConflictException>(() => hotel.DeleteRoom(room.RId));
            Expect<HotelConflictException>(() => hotel.DeleteFoodPackage(food.FId));
            Expect<HotelConflictException>(() => hotel.DeleteServicePackage(service.SId));
            Expect<HotelNotFoundException>(() => hotel.SaveRoom(-9, "Missing", "No", 1));
            Check(hotel.UpdateGuest("ID'abc", new GuestDetails { Name = "Guest updated", Address = "New address", Phone = "test-phone", NationalId = "NEW-ID" }) == 1,
                "Guest updates must match the original string national ID.");
            Check(hotel.GetBills().Single(b => b.BId == booking.BId).CNID == "NEW-ID", "Edited national ID must persist.");
            Expect<HotelNotFoundException>(() => hotel.UpdateGuest("unknown", Guest()));
            Check(hotel.CalculateChange(booking.BId, 400m) == 50m, "Change uses persisted balance.");
            Expect<HotelValidationException>(() => hotel.CompleteCheckout(booking.BId, 349m));
            Check(hotel.GetRooms().Single(r => r.RId == room.RId).IsBooked == "Yes", "Underpayment must not release a room.");
            var receipt = hotel.CompleteCheckout(booking.BId, 400m);
            Check(receipt.Change == 50m && receipt.Booking.Remaining == 0m, "Checkout returns a typed receipt.");
            var released = hotel.GetRooms().Single(r => r.RId == room.RId);
            Check(released.IsBooked == "No" && released.BId == null, "Checkout clears both occupancy and active booking link.");
            Check(hotel.GetBills().Single(b => b.BId == booking.BId).Advance == 368m, "Checkout settles stored total, not UI values.");
            Expect<HotelConflictException>(() => hotel.CompleteCheckout(booking.BId, 400m));
            var rebooking = hotel.CreateReservation(new ReservationRequest { Stay = Stay(room.RId, 0, 0), Guest = Guest() });
            Expect<HotelConflictException>(() => hotel.CompleteCheckout(booking.BId, 400m));
            Check(hotel.GetRooms().Single(r => r.RId == room.RId).BId == rebooking.BId, "Historical checkout must not release a later guest.");
            hotel.CompleteCheckout(rebooking.BId, rebooking.Remaining);
            Check(hotel.CalculateQuote(Stay(room.RId, 0, 0)).Total == 300.75m, "Existing zero-package rows are explicit selectable options.");
            Expect<HotelNotFoundException>(() => hotel.CalculateQuote(Stay(room.RId, -1, 0)));

            RollbackTests(hotel, store);
            ConcurrencyTests(hotel);
            EmptyTableTests(hotel);
        }

        private static void RollbackTests(HotelServices hotel, SqlHotelStore store)
        {
            var room = hotel.SaveRoom(null, "Rollback room", "No", 10);
            int count = hotel.GetBills().Count;
            Execute("CREATE TRIGGER dbo.TestRoomFailure ON dbo.Room AFTER UPDATE AS THROW 51001,'Synthetic failure',1;");
            try
            {
                Expect<HotelInfrastructureException>(() => hotel.CreateReservation(new ReservationRequest { Stay = Stay(room.RId, 0, 0), Guest = Guest() }));
                Check(hotel.GetBills().Count == count && hotel.GetRooms().Single(r => r.RId == room.RId).BId == null,
                    "Room update failure must roll back the inserted booking.");
            }
            finally { Execute("DROP TRIGGER dbo.TestRoomFailure;"); }
            var booking = hotel.CreateReservation(new ReservationRequest { Stay = Stay(room.RId, 0, 0), Guest = Guest() });
            Execute("CREATE TRIGGER dbo.TestRoomFailure ON dbo.Room AFTER UPDATE AS THROW 51001,'Synthetic failure',1;");
            try
            {
                Expect<HotelInfrastructureException>(() => hotel.CompleteCheckout(booking.BId, booking.Remaining));
                Check(hotel.GetBills().Single(b => b.BId == booking.BId).Remaining == booking.Remaining &&
                    hotel.GetRooms().Single(r => r.RId == room.RId).IsBooked == "Yes", "Checkout room failure must roll back settlement.");
            }
            finally { Execute("DROP TRIGGER dbo.TestRoomFailure;"); }
            Expect<InvalidOperationException>(() => store.Read(s => s.InsertRoom("Read-only", "No", 1)));
            Execute("ALTER SCHEMA guest TRANSFER dbo.HotelRoomId;");
            try
            {
                Expect<HotelInfrastructureException>(() => hotel.SaveRoom(null, "Missing sequence", "No", 1));
                Check(hotel.GetRooms().Count(r => r.Category == "Missing sequence") == 0, "Missing migration must fail explicitly without partial data.");
            }
            finally { Execute("ALTER SCHEMA dbo TRANSFER guest.HotelRoomId;"); }
        }

        private static void ConcurrencyTests(HotelServices hotel)
        {
            var rooms = new Room[8];
            Parallel.For(0, rooms.Length, i => rooms[i] = hotel.SaveRoom(null, "Concurrent " + i, "No", 10));
            Check(rooms.Select(r => r.RId).Distinct().Count() == rooms.Length, "Concurrent room allocation must produce unique IDs.");
            var outcomes = new string[2];
            Parallel.For(0, 2, i =>
            {
                try
                {
                    hotel.CreateReservation(new ReservationRequest { Stay = Stay(rooms[0].RId, 0, 0), Guest = Guest() });
                    outcomes[i] = "booked";
                }
                catch (HotelConflictException) { outcomes[i] = "conflict"; }
            });
            Check(outcomes.Count(x => x == "booked") == 1 && outcomes.Count(x => x == "conflict") == 1,
                "Concurrent legacy occupancy reservations must have one winner.");
            Parallel.For(1, rooms.Length, i =>
                hotel.CreateReservation(new ReservationRequest { Stay = Stay(rooms[i].RId, 0, 0), Guest = Guest() }));
            Check(hotel.GetBills().Select(b => b.BId).Distinct().Count() == hotel.GetBills().Count, "Concurrent booking IDs must be unique.");
            var foodIds = new int[4];
            var serviceIds = new int[4];
            Parallel.For(0, 4, i =>
            {
                foodIds[i] = hotel.SaveFoodPackage(null, "Concurrent food " + i, 1).FId;
                serviceIds[i] = hotel.SaveServicePackage(null, "Concurrent service " + i, 1).SId;
            });
            Check(foodIds.Distinct().Count() == 4 && serviceIds.Distinct().Count() == 4, "Concurrent package IDs must be unique.");
        }

        private static void EmptyTableTests(HotelServices hotel)
        {
            // Only the marked, synthetic test database is ever cleared.
            Execute("UPDATE dbo.Room SET BId=NULL; DELETE dbo.Booking; DELETE dbo.Room; DELETE dbo.FoodPackage; DELETE dbo.ServicePackage;");
            Check(hotel.GetRooms().Count == 0 && hotel.GetFoodPackages().Count == 0 && hotel.GetBills().Count == 0,
                "Empty lists must not rely on a first row.");
            var room = hotel.SaveRoom(null, "After empty", "No", 100);
            var food = hotel.SaveFoodPackage(null, "After empty food", 20);
            var service = hotel.SaveServicePackage(null, "After empty service", 5);
            Check(room.RId > 100 && food.FId > 14 && service.SId > 25, "Sequences must not reuse IDs after deletions.");
            hotel.SaveRoom(room.RId, "Changed room", "No", 120);
            hotel.SaveFoodPackage(food.FId, "Changed food", 30);
            hotel.SaveServicePackage(service.SId, "Changed service", 6);
            Check(hotel.CalculateQuote(Stay(room.RId, food.FId, service.SId)).Total == 456m, "Room and package edits must feed shared quotes.");
            hotel.DeleteRoom(room.RId);
            hotel.DeleteFoodPackage(food.FId);
            hotel.DeleteServicePackage(service.SId);
            Check(hotel.GetRooms().Count == 0 && hotel.GetServicePackages().Count == 0, "Unused items can be deleted.");
        }

        internal static QuoteRequest Stay(int room, int food, int service) => new QuoteRequest
        {
            RoomId = room, FoodPackageId = food, ServicePackageId = service,
            CheckIn = new DateTime(2030, 1, 1), CheckOut = new DateTime(2030, 1, 4)
        };

        private static GuestDetails Guest() => new GuestDetails
        {
            Name = "O'Brien", Address = "Synthetic address", Phone = "Synthetic phone", NationalId = "ID'abc"
        };

        internal static object Scalar(string sql)
        {
            using (var connection = new SqlConnection(ConnectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(sql, connection)) return command.ExecuteScalar();
            }
        }

        internal static void Execute(string sql, params SqlParameter[] parameters)
        {
            using (var connection = new SqlConnection(ConnectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddRange(parameters);
                    command.ExecuteNonQuery();
                }
            }
        }

        private sealed class QuoteStore : IHotelStore
        {
            public readonly QuoteSession Session = new QuoteSession();
            public int Transactions;
            public T Read<T>(Func<IHotelSession, T> operation) => operation(Session);
            public T Transact<T>(Func<IHotelSession, T> operation) { Transactions++; throw new NotSupportedException(); }
        }

        private sealed class QuoteSession : IHotelSession
        {
            public bool MissingFood;
            public Room GetRoom(int id) => new Room { RId = id, RoomCost = 100, IsBooked = "No" };
            public FoodPackage GetFoodPackage(int id) => MissingFood ? null : new FoodPackage { FId = id, FPackageCost = 20 };
            public ServicePackage GetServicePackage(int id) => new ServicePackage { SId = id, SPackageCost = 5 };
            public IReadOnlyList<Room> GetRooms(string prefix) => throw new NotSupportedException();
            public Room InsertRoom(string category, string occupancy, decimal cost) => throw new NotSupportedException();
            public void UpdateRoom(Room room) => throw new NotSupportedException();
            public void DeleteRoom(int id) => throw new NotSupportedException();
            public IReadOnlyList<FoodPackage> GetFoodPackages() => throw new NotSupportedException();
            public IReadOnlyList<ServicePackage> GetServicePackages() => throw new NotSupportedException();
            public FoodPackage InsertFoodPackage(string name, decimal cost) => throw new NotSupportedException();
            public ServicePackage InsertServicePackage(string name, decimal cost) => throw new NotSupportedException();
            public void UpdateFoodPackage(FoodPackage package) => throw new NotSupportedException();
            public void UpdateServicePackage(ServicePackage package) => throw new NotSupportedException();
            public void DeleteFoodPackage(int id) => throw new NotSupportedException();
            public void DeleteServicePackage(int id) => throw new NotSupportedException();
            public IReadOnlyList<Booking> GetBookings() => throw new NotSupportedException();
            public Booking GetBooking(int id) => throw new NotSupportedException();
            public Booking InsertBooking(Booking booking) => throw new NotSupportedException();
            public void SettleBooking(int id, decimal total) => throw new NotSupportedException();
            public int UpdateGuest(string original, GuestDetails guest) => throw new NotSupportedException();
            public DesktopUser Authenticate(string username, string password) => throw new NotSupportedException();
        }
    }
}
