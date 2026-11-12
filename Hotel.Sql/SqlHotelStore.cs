using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using Hotel.Application;
using Hotel.Contracts;

namespace Hotel.Sql
{
    public sealed partial class SqlHotelStore : IHotelStore
    {
        private readonly string connectionString;

        public SqlHotelStore(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("SQL connection configuration is required.", nameof(connectionString));
            try { this.connectionString = new SqlConnectionStringBuilder(connectionString).ConnectionString; }
            catch (ArgumentException)
            {
                throw new HotelInfrastructureException("Database configuration is invalid. Check the local connection string.");
            }
        }

        public T Read<T>(Func<IHotelSession, T> operation) => Execute(operation, false);
        public T Transact<T>(Func<IHotelSession, T> operation) => Execute(operation, true);

        private T Execute<T>(Func<IHotelSession, T> operation, bool writable)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                    {
                        if (writable)
                        {
                            // Serializes legacy multi-table writes with a consistent lock order.
                            using (var command = new SqlCommand(
                                "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
                                "@Resource=N'Hotel.ApplicationWrites', @LockMode='Exclusive', " +
                                "@LockOwner='Transaction', @LockTimeout=15000; SELECT @result;",
                                connection, transaction))
                            {
                                if ((int)command.ExecuteScalar() < 0)
                                    throw new HotelConflictException("Another hotel operation is busy. Please retry.");
                            }
                        }
                        var result = operation(new Session(connection, transaction, writable));
                        transaction.Commit();
                        return result;
                    }
                }
            }
            catch (SqlException exception)
            {
                // SQL messages can contain user data; never log statements, parameters or credentials.
                Trace.TraceError("Hotel SQL operation failed. SQL error number: {0}.", exception.Number);
                if (exception.Number == 547)
                    throw new HotelConflictException("This item is referenced by a reservation and cannot be deleted or changed.");
                if (exception.Number == 2601 || exception.Number == 2627)
                    throw new HotelConflictException("An item with this ID already exists. Check the database ID sequence configuration.");
                if (exception.Number == 1205 || exception.Number == 1222)
                    throw new HotelConflictException("Another database operation conflicted. Please retry.");
                if (exception.Number == 208)
                    throw new HotelInfrastructureException("A required database object is missing. Verify the schema and apply the ID sequence migration.");
                throw new HotelInfrastructureException("The database operation failed. Check SQL availability and local configuration.");
            }
        }

        private sealed partial class Session : IHotelSession, IWorkflowSession
        {
            private const string BookingColumns = "BId,RId,CheckIn,CheckOut,FId,SId,CName,CAdd,CPhone,CNID,Total,Advance,Remaining";
            private readonly SqlConnection connection;
            private readonly SqlTransaction transaction;
            private readonly bool writable;

            public Session(SqlConnection connection, SqlTransaction transaction, bool writable)
            {
                this.connection = connection;
                this.transaction = transaction;
                this.writable = writable;
            }

            private SqlCommand Command(string sql, params SqlParameter[] parameters)
            {
                var command = new SqlCommand(sql, connection, transaction);
                command.Parameters.AddRange(parameters);
                return command;
            }

            private static SqlParameter Id(string name, int value) => new SqlParameter(name, SqlDbType.Int) { Value = value };
            private static SqlParameter Text(string name, string value, int size = 50) =>
                new SqlParameter(name, SqlDbType.NVarChar, size) { Value = value };
            private static SqlParameter Cost(string name, decimal value) =>
                new SqlParameter(name, SqlDbType.Float) { Value = Convert.ToDouble(value) };
            private static SqlParameter Date(string name, DateTime value) =>
                new SqlParameter(name, SqlDbType.Date) { Value = value.Date };

            private IReadOnlyList<T> Query<T>(string sql, Func<SqlDataReader, T> map, params SqlParameter[] parameters)
            {
                var rows = new List<T>();
                using (var command = Command(sql, parameters))
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) rows.Add(map(reader));
                return rows.AsReadOnly();
            }

            private T Single<T>(string sql, Func<SqlDataReader, T> map, params SqlParameter[] parameters) where T : class
            {
                var rows = Query(sql, map, parameters);
                if (rows.Count > 1) throw new HotelConflictException("The database contains ambiguous matching records.");
                return rows.Count == 0 ? null : rows[0];
            }

            private int Write(string sql, params SqlParameter[] parameters)
            {
                if (!writable) throw new InvalidOperationException("Writes require an application transaction.");
                using (var command = Command(sql, parameters)) return command.ExecuteNonQuery();
            }

            private void WriteOne(string sql, params SqlParameter[] parameters)
            {
                if (Write(sql, parameters) != 1) throw new HotelNotFoundException("The item no longer exists.");
            }

            public IReadOnlyList<Room> GetRooms(string categoryPrefix)
            {
                var prefix = categoryPrefix.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
                return Query("SELECT RId,Category,IsBooked,RoomCost,BId FROM dbo.Room WHERE Category LIKE @prefix ESCAPE '~' ORDER BY RId;",
                    MapRoom, Text("@prefix", prefix, 101));
            }

            public Room GetRoom(int roomId) => Single(
                "SELECT RId,Category,IsBooked,RoomCost,BId FROM dbo.Room WHERE RId=@id;", MapRoom, Id("@id", roomId));

            public Room InsertRoom(string category, string occupancy, decimal cost)
            {
                int id = NextId("SELECT NEXT VALUE FOR dbo.HotelRoomId;");
                WriteOne("INSERT dbo.Room (RId,Category,IsBooked,RoomCost,BId) VALUES (@id,@category,@occupancy,@cost,NULL);",
                    Id("@id", id), Text("@category", category), Text("@occupancy", occupancy), Cost("@cost", cost));
                return new Room { RId = id, Category = category, IsBooked = occupancy, RoomCost = cost };
            }

            public void UpdateRoom(Room room) => WriteOne(
                "UPDATE dbo.Room SET Category=@category,IsBooked=@occupancy,RoomCost=@cost,BId=@booking WHERE RId=@id;",
                Id("@id", room.RId), Text("@category", room.Category), Text("@occupancy", room.IsBooked), Cost("@cost", room.RoomCost),
                new SqlParameter("@booking", SqlDbType.Int) { Value = (object)room.BId ?? DBNull.Value });

            public void DeleteRoom(int roomId) => WriteOne("DELETE dbo.Room WHERE RId=@id;", Id("@id", roomId));

            public IReadOnlyList<FoodPackage> GetFoodPackages() =>
                Query("SELECT FId,FName,FPackageCost FROM dbo.FoodPackage ORDER BY FId;", MapFood);
            public IReadOnlyList<ServicePackage> GetServicePackages() =>
                Query("SELECT SId,SName,SPackageCost FROM dbo.ServicePackage ORDER BY SId;", MapService);
            public FoodPackage GetFoodPackage(int packageId) =>
                Single("SELECT FId,FName,FPackageCost FROM dbo.FoodPackage WHERE FId=@id;", MapFood, Id("@id", packageId));
            public ServicePackage GetServicePackage(int packageId) =>
                Single("SELECT SId,SName,SPackageCost FROM dbo.ServicePackage WHERE SId=@id;", MapService, Id("@id", packageId));

            public FoodPackage InsertFoodPackage(string name, decimal cost)
            {
                int id = NextId("SELECT NEXT VALUE FOR dbo.HotelFoodPackageId;");
                WriteOne("INSERT dbo.FoodPackage (FId,FName,FPackageCost) VALUES (@id,@name,@cost);",
                    Id("@id", id), Text("@name", name), Cost("@cost", cost));
                return new FoodPackage { FId = id, FName = name, FPackageCost = cost };
            }

            public ServicePackage InsertServicePackage(string name, decimal cost)
            {
                int id = NextId("SELECT NEXT VALUE FOR dbo.HotelServicePackageId;");
                WriteOne("INSERT dbo.ServicePackage (SId,SName,SPackageCost) VALUES (@id,@name,@cost);",
                    Id("@id", id), Text("@name", name), Cost("@cost", cost));
                return new ServicePackage { SId = id, SName = name, SPackageCost = cost };
            }

            public void UpdateFoodPackage(FoodPackage package) => WriteOne(
                "UPDATE dbo.FoodPackage SET FName=@name,FPackageCost=@cost WHERE FId=@id;",
                Id("@id", package.FId), Text("@name", package.FName), Cost("@cost", package.FPackageCost));
            public void UpdateServicePackage(ServicePackage package) => WriteOne(
                "UPDATE dbo.ServicePackage SET SName=@name,SPackageCost=@cost WHERE SId=@id;",
                Id("@id", package.SId), Text("@name", package.SName), Cost("@cost", package.SPackageCost));
            public void DeleteFoodPackage(int packageId) => WriteOne("DELETE dbo.FoodPackage WHERE FId=@id;", Id("@id", packageId));
            public void DeleteServicePackage(int packageId) => WriteOne("DELETE dbo.ServicePackage WHERE SId=@id;", Id("@id", packageId));

            public IReadOnlyList<Booking> GetBookings() =>
                Query("SELECT " + BookingColumns + " FROM dbo.Booking ORDER BY BId;", MapBooking);
            public Booking GetBooking(int bookingId) =>
                Single("SELECT " + BookingColumns + " FROM dbo.Booking WHERE BId=@id;", MapBooking, Id("@id", bookingId));

            public Booking InsertBooking(Booking booking)
            {
                booking.BId = NextId("SELECT NEXT VALUE FOR dbo.HotelBookingId;");
                WriteOne("INSERT dbo.Booking (" + BookingColumns + ") " +
                    "VALUES (@id,@room,@checkIn,@checkOut,@food,@service,@name,@address,@phone,@nid,@total,@advance,@remaining);",
                    Id("@id", booking.BId), Id("@room", booking.RId), Date("@checkIn", booking.CheckIn), Date("@checkOut", booking.CheckOut),
                    Id("@food", booking.FId), Id("@service", booking.SId), Text("@name", booking.CName), Text("@address", booking.CAdd),
                    Text("@phone", booking.CPhone), Text("@nid", booking.CNID), Cost("@total", booking.Total),
                    Cost("@advance", booking.Advance), Cost("@remaining", booking.Remaining));
                return booking;
            }

            public void SettleBooking(int bookingId, decimal total) => WriteOne(
                "UPDATE dbo.Booking SET Advance=@total,Remaining=0 WHERE BId=@id;", Id("@id", bookingId), Cost("@total", total));

            public int UpdateGuest(string originalNationalId, GuestDetails guest) => Write(
                "UPDATE dbo.Booking SET CName=@name,CAdd=@address,CPhone=@phone,CNID=@nid WHERE CNID=@original;",
                Text("@name", guest.Name), Text("@address", guest.Address), Text("@phone", guest.Phone),
                Text("@nid", guest.NationalId), Text("@original", originalNationalId));

            public DesktopUser Authenticate(string username, string password) =>
                Single("SELECT UId,Username,Role FROM dbo.Login WHERE Username=@username AND Password=@password;",
                    reader => new DesktopUser { UserId = reader.GetInt32(0), Username = reader.GetString(1), Role = reader.GetString(2) },
                    Text("@username", username), Text("@password", password));

            private int NextId(string sql)
            {
                if (!writable) throw new InvalidOperationException("ID allocation requires an application transaction.");
                using (var command = Command(sql)) return (int)command.ExecuteScalar();
            }

            private static Room MapRoom(SqlDataReader reader) => new Room
            {
                RId = reader.GetInt32(0), Category = reader.GetString(1), IsBooked = reader.GetString(2),
                RoomCost = Convert.ToDecimal(reader.GetDouble(3)), BId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4)
            };

            private static FoodPackage MapFood(SqlDataReader reader) => new FoodPackage
            {
                FId = reader.GetInt32(0), FName = reader.GetString(1), FPackageCost = Convert.ToDecimal(reader.GetDouble(2))
            };

            private static ServicePackage MapService(SqlDataReader reader) => new ServicePackage
            {
                SId = reader.GetInt32(0), SName = reader.GetString(1), SPackageCost = Convert.ToDecimal(reader.GetDouble(2))
            };

            private static Booking MapBooking(SqlDataReader reader) => new Booking
            {
                BId = reader.GetInt32(0), RId = reader.GetInt32(1), CheckIn = reader.GetDateTime(2), CheckOut = reader.GetDateTime(3),
                FId = reader.GetInt32(4), SId = reader.GetInt32(5), CName = reader.GetString(6), CAdd = reader.GetString(7),
                CPhone = reader.GetString(8), CNID = reader.GetString(9), Total = Convert.ToDecimal(reader.GetDouble(10)),
                Advance = Convert.ToDecimal(reader.GetDouble(11)), Remaining = Convert.ToDecimal(reader.GetDouble(12))
            };
        }
    }
}
