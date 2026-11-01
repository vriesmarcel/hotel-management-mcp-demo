using System;
using System.Linq;
using System.Windows.Forms;
using Hotel.Contracts;

namespace Project_HMS
{
    public partial class Reservation : Form
    {
        private readonly DesktopServices services;

        public Reservation(DesktopServices services)
        {
            this.services = services;
            InitializeComponent();
            txtBId.Text = "New";
            txtDay.ReadOnly = true;
            txtTotal.ReadOnly = true;
            txtRemaining.ReadOnly = true;
            dtpCheckIn.ValueChanged += StayChanged;
            dtpCheckOut.ValueChanged += StayChanged;
            txtRId.TextChanged += InvalidateQuote;
            txtFId.TextChanged += InvalidateQuote;
            txtSId.TextChanged += InvalidateQuote;
            txtAdv.TextChanged += (sender, e) => txtRemaining.Clear();
            PopulateGridView();
            StayChanged(this, EventArgs.Empty);
        }

        private void PopulateGridView()
        {
            DesktopUi.Run(() =>
            {
                dgvRoom.AutoGenerateColumns = false;
                dgvRoom.DataSource = services.Rooms.GetRooms(txtAutoSearch.Text).ToList();
                dgvFood.AutoGenerateColumns = false;
                dgvFood.DataSource = services.Packages.GetFoodPackages().ToList();
                dgvService.AutoGenerateColumns = false;
                dgvService.DataSource = services.Packages.GetServicePackages().ToList();
            });
        }

        private void textBox1_TextChanged(object sender, EventArgs e) => PopulateGridView();
        private void Reservation_FormClosed(object sender, FormClosedEventArgs e) => Application.Exit();

        private QuoteRequest Stay() => new QuoteRequest
        {
            RoomId = int.Parse(txtRId.Text),
            FoodPackageId = int.Parse(txtFId.Text),
            ServicePackageId = int.Parse(txtSId.Text),
            CheckIn = dtpCheckIn.Value.Date,
            CheckOut = dtpCheckOut.Value.Date
        };

        private void StayChanged(object sender, EventArgs e)
        {
            txtDay.Clear();
            InvalidateQuote(sender, e);
        }

        private void InvalidateQuote(object sender, EventArgs e)
        {
            txtTotal.Clear();
            txtRemaining.Clear();
        }

        private void btnConfirm_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                var booking = services.Reservations.CreateReservation(new ReservationRequest
                {
                    Stay = Stay(),
                    Guest = new GuestDetails { Name = txtCName.Text, Address = txtAdd.Text, Phone = txtPhone.Text, NationalId = txtNID.Text },
                    Advance = decimal.Parse(txtAdv.Text)
                });
                MessageBox.Show("Reservation " + booking.BId + " created and room booked successfully. Total: " + booking.Total);
                ClearContent();
                PopulateGridView();
            });
        }

        private void btnTCalculate_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                var quote = services.Reservations.CalculateQuote(Stay());
                txtDay.Text = quote.Days.ToString();
                txtTotal.Text = quote.Total.ToString();
                txtRemaining.Clear();
            });
        }

        private void btnRCalculate_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                var stay = Stay();
                var quote = services.Reservations.CalculateQuote(stay, decimal.Parse(txtAdv.Text));
                txtDay.Text = quote.Days.ToString();
                txtTotal.Text = quote.Total.ToString();
                txtRemaining.Text = quote.Remaining.ToString();
            });
        }

        private void ClearContent()
        {
            txtBId.Text = "New";
            txtRId.Clear();
            dtpCheckIn.Value = DateTime.Today;
            dtpCheckOut.Value = DateTime.Today;
            txtDay.Clear();
            txtFId.Clear();
            txtSId.Clear();
            txtCName.Clear();
            txtAdd.Clear();
            txtPhone.Clear();
            txtNID.Clear();
            txtAdv.Clear();
            txtAutoSearch.Clear();
            InvalidateQuote(this, EventArgs.Empty);
        }

        private void btnClear_Click(object sender, EventArgs e) => ClearContent();

        private void btnBack_Click(object sender, EventArgs e)
        {
            new ManagerDashboard(services).Show();
            Hide();
        }
    }
}
