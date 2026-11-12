using System;
using System.Windows.Forms;
using Hotel.Contracts;

namespace Project_HMS
{
    public sealed class DesktopServices
    {
        public IStays Stays { get; }
        public IRoomService Rooms { get; }
        public IPackageService Packages { get; }
        public IReservationService Reservations { get; }
        public ICustomerService Customers { get; }
        public IBillingService Billing { get; }
        public IDesktopAuthenticationService Authentication { get; }

        public DesktopServices(IRoomService rooms, IPackageService packages, IReservationService reservations,
            ICustomerService customers, IBillingService billing, IDesktopAuthenticationService authentication, IStays stays = null)
        {
            Stays = stays;
            Rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
            Packages = packages ?? throw new ArgumentNullException(nameof(packages));
            Reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
            Customers = customers ?? throw new ArgumentNullException(nameof(customers));
            Billing = billing ?? throw new ArgumentNullException(nameof(billing));
            Authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        }
    }

    internal static class DesktopUi
    {
        public static void Run(Action operation)
        {
            try { operation(); }
            catch (Exception exception) { MessageBox.Show("Error: " + exception.Message); }
        }
    }
}
