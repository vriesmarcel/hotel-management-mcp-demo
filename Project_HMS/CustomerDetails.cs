using System;
using System.Linq;
using System.Windows.Forms;
using Hotel.Contracts;

namespace Project_HMS
{
    public partial class CustomerDetails : Form
    {
        private readonly DesktopServices services;
        private string originalNationalId;

        public CustomerDetails(DesktopServices services)
        {
            this.services = services;
            InitializeComponent();
            PopulateGridView();
        }

        private void PopulateGridView()
        {
            DesktopUi.Run(() =>
            {
                dgvCustomer.AutoGenerateColumns = false;
                dgvCustomer.DataSource = services.Customers.GetCustomers().ToList();
            });
        }

        private void btnUpdate_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                var count = services.Customers.UpdateGuest(originalNationalId ?? txtNID.Text, new GuestDetails
                {
                    Name = txtCName.Text, Address = txtAdd.Text, Phone = txtPhone.Text, NationalId = txtNID.Text
                });
                MessageBox.Show("Customer info updated in " + count + " reservation(s).");
                PopulateGridView();
                ClearContent();
            });
        }

        private void btnClear_Click(object sender, EventArgs e) => ClearContent();

        private void dgvCustomer_DoubleClick(object sender, EventArgs e)
        {
            var booking = dgvCustomer.CurrentRow?.DataBoundItem as Booking;
            if (booking == null) return;
            originalNationalId = booking.CNID;
            txtCName.Text = booking.CName;
            txtPhone.Text = booking.CPhone;
            txtAdd.Text = booking.CAdd;
            txtNID.Text = booking.CNID;
        }

        private void ClearContent()
        {
            originalNationalId = null;
            txtCName.Clear();
            txtPhone.Clear();
            txtAdd.Clear();
            txtNID.Clear();
        }

        private void CustomerDetails_FormClosed(object sender, FormClosedEventArgs e) => Application.Exit();

        private void btnBack_Click(object sender, EventArgs e)
        {
            new ManagerDashboard(services).Show();
            Hide();
        }
    }
}
