using System;
using System.Configuration;
using System.Windows.Forms;
using Hotel.Application;
using Hotel.Sql;

namespace Project_HMS
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                var connection = Environment.GetEnvironmentVariable("HMS_CONNECTION_STRING")
                    ?? ConfigurationManager.ConnectionStrings["HotelDatabase"]?.ConnectionString;
                if (string.IsNullOrWhiteSpace(connection)) throw new ConfigurationErrorsException("Configure HotelDatabase in ConnectionStrings.local.config.");
                var store = new SqlHotelStore(connection);
                var hotel = new HotelServices(store);
                var services = new DesktopServices(hotel, hotel, new ReservationsService(store), hotel,
                    new BillingService(store), hotel, new StaysService(store));
                Application.Run(new Login(services));
            }
            catch (Exception exception) { MessageBox.Show(exception.Message, "Hotel startup"); }
        }
    }
}
