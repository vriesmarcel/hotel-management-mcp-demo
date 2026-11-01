using System;
using System.Configuration;
using System.Windows.Forms;
using Hotel.Application;
using Hotel.Contracts;
using Hotel.Sql;

namespace Project_HMS
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                var connectionString = Environment.GetEnvironmentVariable("HMS_CONNECTION_STRING");
                if (string.IsNullOrWhiteSpace(connectionString))
                    connectionString = ConfigurationManager.ConnectionStrings["HotelDatabase"]?.ConnectionString;
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new ConfigurationErrorsException("Set HMS_CONNECTION_STRING or configure HotelDatabase in the local application configuration.");
                var hotel = new HotelServices(new SqlHotelStore(connectionString));
                var services = new DesktopServices(hotel, hotel, hotel, hotel, hotel, hotel);
                Application.Run(new Login(services));
            }
            catch (ConfigurationErrorsException)
            {
                MessageBox.Show("Database configuration is missing or invalid. Set HMS_CONNECTION_STRING or the local HotelDatabase connection string.");
            }
            catch (HotelInfrastructureException exception)
            {
                MessageBox.Show("Error: " + exception.Message);
            }
        }
    }
}
