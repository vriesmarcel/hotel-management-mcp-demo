using System;
using System.Windows.Forms;

namespace Project_HMS
{
    public partial class ManagerDashboard : Form
    {
        private readonly DesktopServices services;

        public ManagerDashboard(DesktopServices services)
        {
            this.services = services;
            InitializeComponent();
            var stays = new Button { Text = "Arrivals / Check-in", Left = 98, Top = 530, Width = 286, Height = 30, Enabled = services.Stays != null };
            stays.Click += (sender, e) => { using (var form = new StayOperations(services.Stays)) form.ShowDialog(this); };
            panel1.Controls.Add(stays);
        }

        private void btnRoom_Click(object sender, EventArgs e)
        {
            AddRoom ar = new AddRoom(services);
            ar.Visible = true;
            this.Visible = false;
        }

        private void btnReservation_Click(object sender, EventArgs e)
        {
            Reservation rs = new Reservation(services);
            rs.Visible = true;
            this.Visible = false;
        }

        private void btnCustomer_Click(object sender, EventArgs e)
        {
            CustomerDetails cd = new CustomerDetails(services);
            cd.Visible = true;
            this.Visible = false;
        }

        private void btnFood_Click(object sender, EventArgs e)
        {
            FoodMenu fm = new FoodMenu(services);
            fm.Visible = true;
            this.Visible = false;
        }

        private void btnService_Click(object sender, EventArgs e)
        {
            ExtraService es = new ExtraService(services);
            es.Visible = true;
            this.Visible = false;
        }

        private void btnBill_Click(object sender, EventArgs e)
        {
            BillHistory bh = new BillHistory(services);
            bh.Visible = true;
            this.Visible = false;
        }

        private void btnLogout_Click(object sender, EventArgs e)
        {
            Login lg = new Login(services);
            lg.Visible = true;
            this.Visible = false;
        }

        private void ManagerDashboard_FormClosed(object sender, FormClosedEventArgs e)
        {
            Application.Exit();
        }

    }
}
