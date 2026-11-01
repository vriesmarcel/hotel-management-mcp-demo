using System.Collections.Generic;

namespace Hotel.Contracts
{
    public interface IRoomService
    {
        IReadOnlyList<Room> GetRooms(string categoryPrefix = "");
        Room SaveRoom(int? roomId, string category, string occupancy, decimal nightlyCost);
        void DeleteRoom(int roomId);
    }

    public interface IPackageService
    {
        IReadOnlyList<FoodPackage> GetFoodPackages();
        IReadOnlyList<ServicePackage> GetServicePackages();
        FoodPackage SaveFoodPackage(int? packageId, string name, decimal dailyCost);
        ServicePackage SaveServicePackage(int? packageId, string name, decimal cost);
        void DeleteFoodPackage(int packageId);
        void DeleteServicePackage(int packageId);
    }

    public interface IReservationService
    {
        Quote CalculateQuote(QuoteRequest request, decimal advance = 0);
        decimal CalculateRemaining(QuoteRequest request, decimal advance);
        Booking CreateReservation(ReservationRequest request);
    }

    public interface ICustomerService
    {
        IReadOnlyList<Booking> GetCustomers();
        int UpdateGuest(string originalNationalId, GuestDetails guest);
    }

    public interface IBillingService
    {
        IReadOnlyList<Booking> GetBills();
        decimal CalculateChange(int bookingId, decimal paidNow);
        CheckoutReceipt CompleteCheckout(int bookingId, decimal paidNow);
    }

    public interface IDesktopAuthenticationService
    {
        DesktopUser Authenticate(string username, string password);
    }
}
