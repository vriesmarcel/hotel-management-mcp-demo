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
                        throw new HotelValidationException("A new room must be unbooked. Use reservation to book it.");
                    return s.InsertRoom(category, occupancy, nightlyCost);
                }
                var room = Required(s.GetRoom(roomId.Value), "Room");
                if (room.IsBooked != occupancy)
                    throw new HotelConflictException("Use reservation or checkout to change room occupancy.");
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
            if (request == null) throw new HotelValidationException("Reservation is required.");
            ValidateStay(request.Stay);
            ValidateGuest(request.Guest);
            return store.Transact(s =>
            {
                var room = Required(s.GetRoom(request.Stay.RoomId), "Room");
                if (room.IsBooked != "No")
                    throw new HotelConflictException("Room is already booked or has an invalid occupancy state.");
                // The legacy flag remains the current-occupancy rule; future availability is a later milestone.
                var quote = QuoteFor(s, request.Stay);
                ValidateAdvance(request.Advance, quote.Total);
                var booking = s.InsertBooking(new Booking
                {
                    RId = room.RId,
                    CheckIn = request.Stay.CheckIn.Date,
                    CheckOut = request.Stay.CheckOut.Date,
                    FId = request.Stay.FoodPackageId,
                    SId = request.Stay.ServicePackageId,
                    CName = request.Guest.Name,
                    CAdd = request.Guest.Address,
                    CPhone = request.Guest.Phone,
                    CNID = request.Guest.NationalId,
                    Total = quote.Total,
                    Advance = request.Advance,
                    Remaining = quote.Total - request.Advance
                });
                room.IsBooked = "Yes";
                room.BId = booking.BId;
                s.UpdateRoom(room);
                return booking;
            });
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
            Money(paidNow, "Paid amount");
            return store.Read(s => paidNow - Required(s.GetBooking(bookingId), "Booking").Remaining);
        }

        public CheckoutReceipt CompleteCheckout(int bookingId, decimal paidNow)
        {
            Money(paidNow, "Paid amount");
            return store.Transact(s =>
            {
                var booking = Required(s.GetBooking(bookingId), "Booking");
                var room = Required(s.GetRoom(booking.RId), "Room");
                if (room.IsBooked != "Yes" || room.BId != booking.BId)
                    throw new HotelConflictException("This booking is not the room's active reservation.");
                if (booking.Remaining < 0 || paidNow < booking.Remaining)
                    throw new HotelValidationException("Payment must cover the remaining balance.");
                var change = paidNow - booking.Remaining;
                s.SettleBooking(booking.BId, booking.Total);
                room.IsBooked = "No";
                room.BId = null;
                s.UpdateRoom(room);
                booking.Advance = booking.Total;
                booking.Remaining = 0;
                return new CheckoutReceipt { Booking = booking, PaidNow = paidNow, Change = change };
            });
        }

        public DesktopUser Authenticate(string username, string password)
        {
            Text(username, "Username");
            Text(password, "Password");
            return store.Read(s => s.Authenticate(username, password));
        }

        private static Quote QuoteFor(IHotelSession session, QuoteRequest request)
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

        private static void ValidateGuest(GuestDetails guest)
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
