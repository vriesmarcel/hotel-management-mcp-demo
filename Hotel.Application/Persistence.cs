using System;
using System.Collections.Generic;
using Hotel.Contracts;

namespace Hotel.Application
{
    public interface IHotelStore
    {
        T Read<T>(Func<IHotelSession, T> operation);
        T Transact<T>(Func<IHotelSession, T> operation);
    }

    public interface IHotelSession
    {
        IReadOnlyList<Room> GetRooms(string categoryPrefix);
        Room GetRoom(int roomId);
        Room InsertRoom(string category, string occupancy, decimal cost);
        void UpdateRoom(Room room);
        void DeleteRoom(int roomId);
        IReadOnlyList<FoodPackage> GetFoodPackages();
        IReadOnlyList<ServicePackage> GetServicePackages();
        FoodPackage GetFoodPackage(int packageId);
        ServicePackage GetServicePackage(int packageId);
        FoodPackage InsertFoodPackage(string name, decimal cost);
        ServicePackage InsertServicePackage(string name, decimal cost);
        void UpdateFoodPackage(FoodPackage package);
        void UpdateServicePackage(ServicePackage package);
        void DeleteFoodPackage(int packageId);
        void DeleteServicePackage(int packageId);
        IReadOnlyList<Booking> GetBookings();
        Booking GetBooking(int bookingId);
        Booking InsertBooking(Booking booking);
        void SettleBooking(int bookingId, decimal total);
        int UpdateGuest(string originalNationalId, GuestDetails guest);
        DesktopUser Authenticate(string username, string password);
    }
}
