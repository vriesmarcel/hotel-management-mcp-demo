using System;
using System.Linq;
using System.Windows.Forms;
using Hotel.Contracts;

namespace Project_HMS
{
    public sealed class StayOperations : Form
    {
        public StayOperations(IStays stays)
        {
            Text = "Stay Operations — arrivals and check-in";
            Width = 850; Height = 450;
            var date = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 130 };
            var refresh = new Button { Text = "Refresh" };
            var checkIn = new Button { Text = "Check in", Width = 100 };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
            bar.Controls.AddRange(new Control[] { date, refresh, checkIn });
            var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true, AllowUserToAddRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
            Controls.Add(grid); Controls.Add(bar);
            Action reload = () => DesktopUi.Run(() => grid.DataSource = stays.GetArrivals(date.Value.Date).Select(b => new { b.BId, b.RId, b.CName, b.CheckIn, b.CheckOut, b.Status }).ToList());
            refresh.Click += (sender, e) => reload();
            date.ValueChanged += (sender, e) => reload();
            checkIn.Click += (sender, e) => DesktopUi.Run(() =>
            {
                if (grid.CurrentRow == null) throw new HotelValidationException("Select an arrival first.");
                int id = Convert.ToInt32(grid.CurrentRow.Cells["BId"].Value);
                if (MessageBox.Show("Check in reservation " + id + "?", "Confirm arrival", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                stays.CheckIn(id, Guid.NewGuid().ToString("D"), true);
                reload();
            });
            Shown += (sender, e) => reload();
        }
    }
}
