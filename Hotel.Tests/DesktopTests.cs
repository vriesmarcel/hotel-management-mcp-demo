using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Hotel.Application;
using Hotel.Contracts;
using Project_HMS;
using WinFormsApplication = System.Windows.Forms.Application;

namespace Hotel.Tests
{
    internal static class DesktopTests
    {
        public static void Run(Hotel.Sql.SqlHotelStore store)
        {
            WinFormsApplication.EnableVisualStyles();
            WinFormsApplication.SetCompatibleTextRenderingDefault(false);
            var hotel = new HotelServices(store);
            var services = new DesktopServices(hotel, hotel, new ReservationsService(store), hotel, new BillingService(store), hotel, new StaysService(store));
            using (var dialogs = new OwnDialogs())
            {
                using (var login = Open(new Login(services)))
                {
                    Set(login, "txtUserid", "test-manager");
                    Set(login, "txtPassword", "bad");
                    Invoke(login, "btnLogin_Click");
                    Program.Check(dialogs.Last == "Login Invalid", "Desktop invalid login must be visible.");
                    Set(login, "txtUserid", "test-manager");
                    Set(login, "txtPassword", Program.TestPassword);
                    Invoke(login, "btnLogin_Click");
                    Program.Check(dialogs.Last == "Login Valid" && !login.Visible, "Desktop login can succeed after retry.");
                    foreach (var dashboard in WinFormsApplication.OpenForms.OfType<ManagerDashboard>().ToArray()) dashboard.Dispose();
                }
                using (var owner = Open(new Login(services)))
                {
                    Set(owner, "txtUserid", "test-owner");
                    Set(owner, "txtPassword", Program.TestPassword);
                    Invoke(owner, "btnLogin_Click");
                    Program.Check(owner.Visible && dialogs.Last == "No desktop workflow is implemented for this role.",
                        "Owner must not disappear into an unimplemented screen or gain manager routing.");
                }

                using (var roomForm = Open(new AddRoom(services)))
                {
                    Program.Check(Get<TextBox>(roomForm, "txtRId").ReadOnly && Get<DataGridView>(roomForm, "dgvRoom").Rows.Count == 0,
                        "Empty room grid and read-only generated ID must work.");
                    Set(roomForm, "txtCategory", "Desktop room");
                    Set(roomForm, "txtRCost", "100");
                    Invoke(roomForm, "btnSave_Click");
                    Program.Check(dialogs.Last.Contains("saved successfully") && hotel.GetRooms().Count == 1, "Desktop room creation must call the service.");
                    Invoke(roomForm, "dgvRoom_DoubleClick");
                    Set(roomForm, "txtRCost", "120");
                    Invoke(roomForm, "btnSave_Click");
                    Program.Check(hotel.GetRooms().Single().RoomCost == 120m, "Desktop selection must edit the existing room.");
                    Invoke(roomForm, "btnClear_Click");
                    Program.Check(Get<TextBox>(roomForm, "txtRId").Text == "New", "Clear must reset selected room ID.");
                }
                using (var foodForm = Open(new FoodMenu(services)))
                {
                    Set(foodForm, "txtFName", "Desktop food");
                    Set(foodForm, "txtFCost", "20");
                    Invoke(foodForm, "btnSave_Click");
                    Invoke(foodForm, "dgvFood_DoubleClick");
                    Set(foodForm, "txtFCost", "30");
                    Invoke(foodForm, "btnSave_Click");
                    Program.Check(hotel.GetFoodPackages().Single().FPackageCost == 30m, "Desktop food create/edit must work.");
                }
                using (var serviceForm = Open(new ExtraService(services)))
                {
                    Set(serviceForm, "txtSName", "Desktop service");
                    Set(serviceForm, "txtSCost", "5");
                    Invoke(serviceForm, "btnSave_Click");
                    Invoke(serviceForm, "dgvService_DoubleClick");
                    Set(serviceForm, "txtSCost", "6");
                    Invoke(serviceForm, "btnSave_Click");
                    Program.Check(hotel.GetServicePackages().Single().SPackageCost == 6m, "Desktop service create/edit must work.");
                }
                using (var reservation = Open(new Reservation(services)))
                {
                    Set(reservation, "txtAutoSearch", "Desktop");
                    Program.Check(Get<DataGridView>(reservation, "dgvRoom").Rows.Count == 1, "Desktop category search must bind typed room rows.");
                    Set(reservation, "txtRId", hotel.GetRooms().Single().RId.ToString());
                    Set(reservation, "txtFId", hotel.GetFoodPackages().Single().FId.ToString());
                    Set(reservation, "txtSId", hotel.GetServicePackages().Single().SId.ToString());
                    Get<DateTimePicker>(reservation, "dtpCheckIn").Value = DateTime.Today;
                    Get<DateTimePicker>(reservation, "dtpCheckOut").Value = DateTime.Today.AddDays(3);
                    Set(reservation, "txtAdv", "56");
                    Invoke(reservation, "btnTCalculate_Click");
                    Program.Check(Get<TextBox>(reservation, "txtDay").Text == "3" && Get<TextBox>(reservation, "txtTotal").Text == "456",
                        "Desktop quote must derive days and pricing from the service.");
                    Invoke(reservation, "btnRCalculate_Click");
                    Program.Check(Get<TextBox>(reservation, "txtRemaining").Text == "400", "Desktop remaining must use the same quote.");
                    Get<DateTimePicker>(reservation, "dtpCheckOut").Value = DateTime.Today.AddDays(4);
                    Program.Check(Get<TextBox>(reservation, "txtTotal").Text == "", "Changed stay dates must invalidate displayed totals.");
                    Get<DateTimePicker>(reservation, "dtpCheckOut").Value = DateTime.Today.AddDays(3);
                    Set(reservation, "txtCName", "Desktop guest");
                    Set(reservation, "txtAdd", "Synthetic address");
                    Set(reservation, "txtPhone", "Synthetic phone");
                    Set(reservation, "txtNID", "Desktop-ID");
                    Invoke(reservation, "btnRCalculate_Click");
                    Invoke(reservation, "btnConfirm_Click");
                    Program.Check(hotel.GetBills().Single().Total == 456m && hotel.GetRooms().Single().IsBooked == "No",
                        "Desktop confirm reserves without occupying the room.");
                    Program.Check(Get<TextBox>(reservation, "txtBId").Text == "New" && Get<TextBox>(reservation, "txtCName").Text == "",
                        "Desktop confirm must clear entry fields.");
                }
                using (var customers = Open(new CustomerDetails(services)))
                {
                    Invoke(customers, "dgvCustomer_DoubleClick");
                    Set(customers, "txtCName", "Edited desktop guest");
                    Set(customers, "txtNID", "Edited-ID");
                    Invoke(customers, "btnUpdate_Click");
                    Program.Check(hotel.GetCustomers().Single().CNID == "Edited-ID", "Desktop national ID edit must match its original value.");
                }
                services.Stays.CheckIn(hotel.GetBills().Single().BId, Guid.NewGuid().ToString(), true);
                using (var bill = Open(new BillHistory(services)))
                {
                    Invoke(bill, "dgvBooking_DoubleClick");
                    Set(bill, "txtNPaid", "450");
                    Invoke(bill, "btnCalculate_Click");
                    Program.Check(Get<TextBox>(bill, "txtReturn").Text == "50", "Desktop billing change must use stored balance.");
                    using (var bitmap = new Bitmap(900, 1200))
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        var args = new PrintPageEventArgs(graphics, new Rectangle(0, 0, 900, 1200),
                            new Rectangle(0, 0, 900, 1200), new PageSettings());
                        var prior = dialogs.Count;
                        Invoke(bill, "printDocument1_PrintPage", args);
                        Program.Check(dialogs.Count == prior, "Existing bill print rendering must not raise an error.");
                        int ink = 0;
                        for (int y = 100; y < 1000; y++)
                        for (int x = 100; x < 800; x++)
                            if (bitmap.GetPixel(x, y).A != 0) ink++;
                        Program.Check(ink > 1000, "Bill print-page rendering must produce actual receipt content.");
                    }
                    Invoke(bill, "btnUpdate_Click");
                    Program.Check(hotel.GetBills().Single().Remaining == 0 && hotel.GetRooms().Single().BId == null,
                        "Desktop checkout must settle and release atomically.");
                }
                using (var food = Open(new FoodMenu(services)))
                {
                    Invoke(food, "btnDelete_Click");
                    Program.Check(dialogs.Last.StartsWith("Error:") && hotel.GetFoodPackages().Count == 1,
                        "Desktop must surface referenced-item deletion failures.");
                }
                var freeRoom = hotel.SaveRoom(null, "Delete me", "No", 1);
                using (var rooms = Open(new AddRoom(services)))
                {
                    var grid = Get<DataGridView>(rooms, "dgvRoom");
                    grid.CurrentCell = grid.Rows.Cast<DataGridViewRow>().Single(r => ((Room)r.DataBoundItem).RId == freeRoom.RId).Cells[0];
                    Invoke(rooms, "btnDelete_Click");
                    Program.Check(hotel.GetRooms().Count == 1, "Desktop unreferenced room deletion must work.");
                }
                var freeFood = hotel.SaveFoodPackage(null, "Delete food", 1);
                using (var food = Open(new FoodMenu(services)))
                {
                    var grid = Get<DataGridView>(food, "dgvFood");
                    grid.CurrentCell = grid.Rows.Cast<DataGridViewRow>().Single(r => ((FoodPackage)r.DataBoundItem).FId == freeFood.FId).Cells[0];
                    Invoke(food, "btnDelete_Click");
                    Program.Check(hotel.GetFoodPackages().Count == 1, "Desktop unreferenced food deletion must work.");
                }
                var freeService = hotel.SaveServicePackage(null, "Delete service", 1);
                using (var service = Open(new ExtraService(services)))
                {
                    var grid = Get<DataGridView>(service, "dgvService");
                    grid.CurrentCell = grid.Rows.Cast<DataGridViewRow>().Single(r => ((ServicePackage)r.DataBoundItem).SId == freeService.SId).Cells[0];
                    Invoke(service, "btnDelete_Click");
                    Program.Check(hotel.GetServicePackages().Count == 1, "Desktop unreferenced service deletion must work.");
                }
                using (var dashboard = Open(new ManagerDashboard(services)))
                {
                    foreach (var navigation in new[] { "btnRoom_Click", "btnReservation_Click", "btnCustomer_Click", "btnFood_Click", "btnService_Click", "btnBill_Click" })
                    {
                        dashboard.Show();
                        Invoke(dashboard, navigation);
                        var child = WinFormsApplication.OpenForms.Cast<Form>().Single(f => f != dashboard);
                        Program.Check(!dashboard.Visible && child.Visible, "Dashboard navigation must preserve service injection.");
                        Invoke(child, "btnBack_Click");
                        child.Dispose();
                        foreach (var extra in WinFormsApplication.OpenForms.Cast<Form>().Where(f => f != dashboard).ToArray()) extra.Dispose();
                    }
                    dashboard.Show();
                    Invoke(dashboard, "btnLogout_Click");
                    Program.Check(WinFormsApplication.OpenForms.OfType<Login>().Count() == 1, "Logout must return to login.");
                    foreach (var login in WinFormsApplication.OpenForms.OfType<Login>().ToArray()) login.Dispose();
                }
            }
        }

        private static T Open<T>(T form) where T : Form
        {
            form.Show();
            WinFormsApplication.DoEvents();
            return form;
        }

        private static T Get<T>(Form form, string name) where T : class =>
            (T)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        private static void Set(Form form, string name, string text) => Get<TextBox>(form, name).Text = text;
        private static void Invoke(Form form, string method, EventArgs args = null)
        {
            form.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { form, args ?? EventArgs.Empty });
            WinFormsApplication.DoEvents();
        }

        // Only dismisses dialogs created by this test process, never other desktop windows.
        private sealed class OwnDialogs : IDisposable
        {
            private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
            [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, WindowCallback callback, IntPtr parameter);
            [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
            [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int maxCount);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string className, string title);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
            [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
            private readonly System.Windows.Forms.Timer timer;
            private volatile string last = "";
            private int count;
            public string Last => last;
            public int Count => System.Threading.Volatile.Read(ref count);

            public OwnDialogs()
            {
                uint process = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                uint uiThread = GetCurrentThreadId();
                // A Forms timer runs on the same desktop and is pumped by modal dialogs.
                timer = new System.Windows.Forms.Timer { Interval = 50 };
                timer.Tick += (sender, args) => EnumThreadWindows(uiThread, (window, parameter) =>
                {
                    uint owner;
                    GetWindowThreadProcessId(window, out owner);
                    if (owner != process) return true;
                    var className = new StringBuilder(100);
                    GetClassName(window, className, className.Capacity);
                    if (className.ToString() == "#32770")
                    {
                        var text = new StringBuilder(1000);
                        var child = IntPtr.Zero;
                        while ((child = FindWindowEx(window, child, "Static", null)) != IntPtr.Zero)
                        {
                            GetWindowText(child, text, text.Capacity);
                            if (text.Length > 0) last = text.ToString();
                        }
                        System.Threading.Interlocked.Increment(ref count);
                        // The OK control can be IDCANCEL (2) on this Windows version.
                        // Click the actual control instead of assuming command ID 1.
                        var button = FindWindowEx(window, IntPtr.Zero, "Button", null);
                        if (button != IntPtr.Zero) PostMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                    }
                    return true;
                }, IntPtr.Zero);
                timer.Start();
            }

            public void Dispose() => timer.Dispose();
        }
    }
}
