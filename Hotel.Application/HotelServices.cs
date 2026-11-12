using System;
using System.Collections.Generic;
using Hotel.Contracts;

namespace Hotel.Application
{
    // One coordinator shares the same transaction contract across focused service interfaces.
    public sealed class HotelServices : IRoomService, IPackageService, IReservationService,
        ICustomerService, IBillingService, IDesktopAuthenticationService
    {
        private readonly IHotelStore store;

        public HotelServices(IHotelStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public IReadOnlyList<Room> GetRooms(string categoryPrefix = "")
        {
            if (categoryPrefix == null || categoryPrefix.Length > 50)
                throw new HotelValidationException("Room category search must be at most 50 characters.");
            return store.Read(s => s.GetRooms(categoryPrefix));
        }

        public Room SaveRoom(int? roomId, string category, string occupancy, decimal nightlyCost)
        {
            Text(category, "Category");
            Money(nightlyCost, "Room cost");
            if (occupancy != "Yes" && occupancy != "No")
                throw new HotelValidationException("Room occupancy must be Yes or No.");
            return store.Transact(s =>
            {
                if (!roomId.HasValue)
                {
                    if (occupancy != "No")
                        throw new HotelValidationException("A new room must be unbooked. Use check-in to occupy it.");
                    return s.InsertRoom(category, occupancy, nightlyCost);
                }
                var room = Required(s.GetRoom(roomId.Value), "Room");
                if (room.IsBooked != occupancy)
                    throw new HotelConflictException("Use check-in or checkout to change room occupancy.");
                room.Category = category;
                room.RoomCost = nightlyCost;
                s.UpdateRoom(room);
                return room;
            });
        }

        public void DeleteRoom(int roomId)
        {
            store.Transact(s =>
            {
                var room = Required(s.GetRoom(roomId), "Room");
                if (room.IsBooked == "Yes")
                    throw new HotelConflictException("A booked room cannot be deleted.");
                s.DeleteRoom(roomId);
                return true;
            });
        }

        public IReadOnlyList<FoodPackage> GetFoodPackages() => store.Read(s => s.GetFoodPackages());
        public IReadOnlyList<ServicePackage> GetServicePackages() => store.Read(s => s.GetServicePackages());

        public FoodPackage SaveFoodPackage(int? packageId, string name, decimal dailyCost)
        {
            Text(name, "Food package name", packageId == 0);
            Money(dailyCost, "Food cost");
            return store.Transact(s =>
            {
                if (!packageId.HasValue) return s.InsertFoodPackage(name, dailyCost);
                var package = Required(s.GetFoodPackage(packageId.Value), "Food package");
                package.FName = name;
                package.FPackageCost = dailyCost;
                s.UpdateFoodPackage(package);
                return package;
            });
        }

        public ServicePackage SaveServicePackage(int? packageId, string name, decimal cost)
        {
            Text(name, "Service package name", packageId == 0);
            Money(cost, "Service cost");
            return store.Transact(s =>
            {
                if (!packageId.HasValue) return s.InsertServicePackage(name, cost);
                var package = Required(s.GetServicePackage(packageId.Value), "Service package");
                package.SName = name;
                package.SPackageCost = cost;
                s.UpdateServicePackage(package);
                return package;
            });
        }

        public void DeleteFoodPackage(int packageId)
        {
            store.Transact(s =>
            {
                Required(s.GetFoodPackage(packageId), "Food package");
                s.DeleteFoodPackage(packageId);
                return true;
            });
        }

        public void DeleteServicePackage(int packageId)
        {
            store.Transact(s =>
            {
                Required(s.GetServicePackage(packageId), "Service package");
                s.DeleteServicePackage(packageId);
                return true;
            });
        }

        public Quote CalculateQuote(QuoteRequest request, decimal advance = 0)
        {
            ValidateStay(request);
            return store.Read(s =>
            {
                var quote = QuoteFor(s, request);
                ValidateAdvance(advance, quote.Total);
                quote.Remaining = quote.Total - advance;
                return quote;
            });
        }

        public decimal CalculateRemaining(QuoteRequest request, decimal advance)
        {
            return CalculateQuote(request, advance).Remaining;
        }

        public Booking CreateReservation(ReservationRequest request)
        {
            return new ReservationsService(store).CreateReservation(request);
        }

        public IReadOnlyList<Booking> GetCustomers() => store.Read(s => s.GetBookings());
        public IReadOnlyList<Booking> GetBills() => store.Read(s => s.GetBookings());

        public int UpdateGuest(string originalNationalId, GuestDetails guest)
        {
            Text(originalNationalId, "Original national ID");
            ValidateGuest(guest);
            return store.Transact(s =>
            {
                int count = s.UpdateGuest(originalNationalId, guest);
                if (count == 0) throw new HotelNotFoundException("Customer was not found.");
                return count;
            });
        }

        public decimal CalculateChange(int bookingId, decimal paidNow)
        {
            return new BillingService(store).CalculateChange(bookingId, paidNow);
        }

        public CheckoutReceipt CompleteCheckout(int bookingId, decimal paidNow)
        {
            return new BillingService(store).CompleteCheckout(bookingId, paidNow);
        }

        public DesktopUser Authenticate(string username, string password)
        {
            Text(username, "Username");
            Text(password, "Password");
            return store.Read(s => s.Authenticate(username, password));
        }

        internal static Quote QuoteFor(IHotelSession session, QuoteRequest request)
        {
            var room = Required(session.GetRoom(request.RoomId), "Room");
            var food = Required(session.GetFoodPackage(request.FoodPackageId), "Food package");
            var service = Required(session.GetServicePackage(request.ServicePackageId), "Service package");
            Money(room.RoomCost, "Stored room cost");
            Money(food.FPackageCost, "Stored food cost");
            Money(service.SPackageCost, "Stored service cost");
            int days = (request.CheckOut.Date - request.CheckIn.Date).Days;
            return new Quote { Days = days, Total = checked((room.RoomCost + food.FPackageCost) * days + service.SPackageCost) };
        }

        private static void ValidateStay(QuoteRequest request)
        {
            if (request == null) throw new HotelValidationException("Stay details are required.");
            if (request.CheckOut.Date <= request.CheckIn.Date)
                throw new HotelValidationException("Check-out must be after check-in.");
        }

        private static void ValidateAdvance(decimal advance, decimal total)
        {
            Money(advance, "Advance");
            if (advance > total) throw new HotelValidationException("Advance cannot exceed the total.");
        }

        internal static void ValidateGuest(GuestDetails guest)
        {
            if (guest == null) throw new HotelValidationException("Guest details are required.");
            Text(guest.Name, "Guest name");
            Text(guest.Address, "Guest address");
            Text(guest.Phone, "Guest phone");
            Text(guest.NationalId, "Guest national ID");
        }

        private static void Text(string value, string label, bool allowEmpty = false)
        {
            if (value == null || (!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Length > 50)
                throw new HotelValidationException(label + " must contain " + (allowEmpty ? "0" : "1") + " to 50 characters.");
        }

        private static void Money(decimal value, string label)
        {
            if (value < 0) throw new HotelValidationException(label + " cannot be negative.");
        }

        private static T Required<T>(T value, string label) where T : class
        {
            if (value == null) throw new HotelNotFoundException(label + " was not found.");
            return value;
        }
    }
}
