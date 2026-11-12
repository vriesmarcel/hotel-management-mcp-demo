using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Hotel.Contracts;

namespace Project_HMS
{
    public partial class BillHistory : Form
    {
        private readonly DesktopServices services;
        private int? selectedBookingId;
        private string requestId;

        public BillHistory(DesktopServices services)
        {
            this.services = services;
            InitializeComponent();
            txtBId.ReadOnly = true;
            txtRId.ReadOnly = true;
            txtCName.ReadOnly = true;
            txtPhone.ReadOnly = true;
            txtTotal.ReadOnly = true;
            txtAdvance.ReadOnly = true;
            txtRemaining.ReadOnly = true;
            txtReturn.ReadOnly = true;
            dtpCheckIn.Enabled = false;
            dtpCheckOut.Enabled = false;
            txtNPaid.TextChanged += (sender, e) => txtReturn.Clear();
            this.PopulateGridView();
        }

        private void PopulateGridView()
        {
            try
            {
                this.dgvBooking.AutoGenerateColumns = false;
                this.dgvBooking.DataSource = services.Billing.GetBills().ToList();
            }
            catch (Exception exc)
            {
                MessageBox.Show("Error: " + exc.Message);
            }
        }

        private void BillHistory_FormClosed(object sender, FormClosedEventArgs e)
        {
            Application.Exit();
        }

        private void dgvBooking_DoubleClick(object sender, EventArgs e)
        {
            var booking = dgvBooking.CurrentRow?.DataBoundItem as Booking;
            if (booking == null) return;
            selectedBookingId = booking.BId;
            requestId = Guid.NewGuid().ToString("D");
            txtBId.Text = booking.BId.ToString();
            txtRId.Text = booking.RId.ToString();
            txtCName.Text = booking.CName;
            txtPhone.Text = booking.CPhone;
            dtpCheckIn.Value = booking.CheckIn;
            dtpCheckOut.Value = booking.CheckOut;
            txtAdvance.Text = booking.Advance.ToString();
            txtRemaining.Text = booking.Remaining.ToString();
            txtTotal.Text = booking.Total.ToString();
            txtNPaid.Clear();
            txtReturn.Clear();
        }

        private void btnCalculate_Click(object sender, EventArgs e)
        {
            try
            {
                if (!selectedBookingId.HasValue) throw new HotelValidationException("Select a bill first.");
                txtReturn.Text = services.Billing.CalculateChange(selectedBookingId.Value, decimal.Parse(txtNPaid.Text)).ToString();
            }
            catch (Exception exc)
            {
                MessageBox.Show("Error: " + exc.Message);
            }
        }


        private void printDocument1_PrintPage(object sender, System.Drawing.Printing.PrintPageEventArgs e)
        {
            try
            {
                int x = 100, y = 100; //start position

                //Image newImage = Image.FromFile("logo.png");
                int height = 50;
                //int ix = x, iy = y; //image position
                //e.Graphics.DrawImage(newImage, ix, iy, width, height);

                x += 0; //left align texts with logo image
                y += height + 30; //some space below logo

                var header = new Font("Calibri", 21, FontStyle.Bold);
                int hdy = (int)header.GetHeight(e.Graphics); //30; //line height spacing

                var fnt = new Font("Times new Roman", 14, FontStyle.Bold);
                int dy = (int)fnt.GetHeight(e.Graphics); //20; //line height spacing

                e.Graphics.DrawString("Hotel Dream", header, Brushes.Black, new PointF(x, y)); y += hdy+hdy;
                e.Graphics.DrawString("Bill Id : " + txtBId.Text, fnt, Brushes.Black, new PointF(x, y)); y += dy;
                e.Graphics.DrawString("Room Id : " + txtRId.Text, fnt, Brushes.Black, new PointF(x, y)); y += dy;
                e.Graphics.DrawString("Customer Name : " + txtCName.Text, fnt, Brushes.Black, new PointF(x, y)); y += dy;
                e.Graphics.DrawString("Phone No : " + txtPhone.Text, fnt, Brushes.Black, new PointF(x, y)); y += dy;
                e.Graphics.DrawString("Check In : " + dtpCheckIn.Text, fnt, Brushes.Black, new PointF(x, y)); y += dy;
                e.Graphics.DrawString("Check Out : " + dtpCheckOut.Text, fnt, Brushes.Black, new PointF(x, y)); y += dy;
                e.Graphics.DrawString("Total : " + txtTotal.Text, fnt, Brushes.Black, new PointF(x, y)); y += dy;
                e.Graphics.DrawString("Advance : " + txtAdvance.Text, fnt, Brushes.Black, new PointF(x, y)); y += dy;
                e.Graphics.DrawString("Now Paid Amount : " + txtNPaid.Text, fnt, Brushes.Black, new PointF(x, y)); y += dy;
                e.Graphics.DrawString("Returned Amount : " + txtReturn.Text, fnt, Brushes.Black, new PointF(x, y)); y += dy;
            }
            catch (Exception exc)
            {
                MessageBox.Show("Error: " + exc.Message);
            }

        }

        private void btnPrint_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                if (!selectedBookingId.HasValue) throw new HotelValidationException("Select a bill first.");
                txtReturn.Text = services.Billing.CalculateChange(selectedBookingId.Value, decimal.Parse(txtNPaid.Text)).ToString();
                printPreviewDialog1.Document = printDocument1;
                printPreviewDialog1.ShowDialog();
            });
        }

        private void btnUpdate_Click(object sender, EventArgs e)
        {
            try
            {
                if (!selectedBookingId.HasValue) throw new HotelValidationException("Select a bill first.");
                if (MessageBox.Show($"Settle booking {selectedBookingId.Value} with {txtNPaid.Text} paid now and check out?", "Confirm checkout", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                var receipt = ((IBilling)services.Billing).CompleteCheckout(selectedBookingId.Value, decimal.Parse(txtNPaid.Text), requestId, true);
                MessageBox.Show("Booking settled and room released successfully. Returned amount: " + receipt.Change);
                this.PopulateGridView();
                this.ClearContent();
            }
            catch (Exception exc)
            {
                MessageBox.Show("Error: " + exc.Message);
            }
        }

        private void ClearContent()
        {
            selectedBookingId = null;
            this.txtBId.Text = "";
            this.txtRId.Text = "";
            this.dtpCheckIn.Text = "";
            this.dtpCheckOut.Text = "";
            this.txtCName.Text = "";
            this.txtPhone.Text = "";
            this.txtTotal.Text = "";
            this.txtAdvance.Text = "";
            this.txtRemaining.Text = "";
            this.txtNPaid.Text = "";
            this.txtReturn.Text = "";
        }

        private void btnBack_Click(object sender, EventArgs e)
        {
            ManagerDashboard md = new ManagerDashboard(services);
            md.Visible = true;
            this.Visible = false;
        }
    }
}
