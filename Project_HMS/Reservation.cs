using System;
using System.Linq;
using System.Windows.Forms;
using Hotel.Contracts;

namespace Project_HMS
{
    public partial class Reservation : Form
    {
        private readonly DesktopServices services;
        private string quoteId;
        private CancellationTerms cancellation;
        private string requestId = Guid.NewGuid().ToString("D");

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
            CheckIn = DateTime.SpecifyKind(dtpCheckIn.Value.Date, DateTimeKind.Unspecified),
            CheckOut = DateTime.SpecifyKind(dtpCheckOut.Value.Date, DateTimeKind.Unspecified)
        };

        private void StayChanged(object sender, EventArgs e)
        {
            txtDay.Clear();
            InvalidateQuote(sender, e);
        }

        private void InvalidateQuote(object sender, EventArgs e)
        {
            quoteId = null;
            requestId = Guid.NewGuid().ToString("D");
            txtTotal.Clear();
            txtRemaining.Clear();
        }

        private void btnConfirm_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                if (quoteId == null) throw new HotelValidationException("Calculate a quote first.");
                if (MessageBox.Show($"Book room {txtRId.Text} for {txtCName.Text}, {dtpCheckIn.Value:yyyy-MM-dd} to {dtpCheckOut.Value:yyyy-MM-dd}, total {txtTotal.Text}, advance {txtAdv.Text}?\nFree cancellation through {cancellation.FreeCancellationUntil:yyyy-MM-dd HH:mm zzz} ({cancellation.HotelTimeZone}). Afterwards: {cancellation.LateCancellationFee} for one room night, excluding packages. Policy {cancellation.PolicyVersion}.", "Confirm reservation", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                var booking = services.Reservations.CreateReservation(new ReservationRequest
                {
                    QuoteId = quoteId, RequestId = requestId, Confirmed = true,
                    Stay = Stay(),
                    Guest = new GuestDetails { Name = txtCName.Text, Address = txtAdd.Text, Phone = txtPhone.Text, NationalId = txtNID.Text },
                    Advance = decimal.Parse(txtAdv.Text)
                });
                MessageBox.Show("Reservation " + booking.BId + " reserved successfully. Check in through Stay Operations on arrival. Total: " + booking.Total);
                ClearContent();
                PopulateGridView();
            });
        }

        private void btnTCalculate_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                var quote = services.Reservations.CalculateQuote(Stay());
                quoteId = quote.QuoteId;
                cancellation = quote.Cancellation;
                requestId = Guid.NewGuid().ToString("D");
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
                quoteId = quote.QuoteId;
                cancellation = quote.Cancellation;
                requestId = Guid.NewGuid().ToString("D");
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
