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
    internal static partial class Program
    {
        private static int assertions;
        internal static string ConnectionString;
        internal static string TestPassword;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Console.Error));
                Boundaries();
                UnitRules();
                ResourceRules();
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
                    WorkflowTests();
                    Console.WriteLine("PASS: SQL pricing, lifecycle, rollback, concurrent IDs and conflict checks.");
                    DesktopTests.Run(new SqlHotelStore(ConnectionString));
                }
                Console.WriteLine("PASS: " + assertions + " assertions" + (args.Contains("--integration") ? " including SQL integration and WinForms workflows." : " (boundaries and application rules)."));
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL: " + exception.GetType().Name + ": " + exception.Message);
                Console.Error.WriteLine(exception.StackTrace);
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
