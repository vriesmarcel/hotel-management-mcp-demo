using System;
using System.Linq;
using System.Windows.Forms;
using Hotel.Contracts;

namespace Project_HMS
{
    public partial class AddRoom : Form
    {
        private readonly DesktopServices services;
        private int? selectedId;

        public AddRoom(DesktopServices services)
        {
            this.services = services;
            InitializeComponent();
            txtRId.ReadOnly = true;
            ClearContent();
            PopulateGridView();
        }

        private void PopulateGridView()
        {
            DesktopUi.Run(() =>
            {
                dgvRoom.AutoGenerateColumns = false;
                dgvRoom.DataSource = services.Rooms.GetRooms().ToList();
            });
        }

        private void AddRoom_FormClosed(object sender, FormClosedEventArgs e) => Application.Exit();

        private void dgvRoom_DoubleClick(object sender, EventArgs e)
        {
            var room = dgvRoom.CurrentRow?.DataBoundItem as Room;
            if (room == null) return;
            selectedId = room.RId;
            txtRId.Text = room.RId.ToString();
            txtCategory.Text = room.Category;
            txtIsBooked.Text = room.IsBooked;
            txtRCost.Text = room.RoomCost.ToString();
        }

        private void ClearContent()
        {
            selectedId = null;
            txtRId.Text = "New";
            txtCategory.Clear();
            txtIsBooked.Text = "No";
            txtRCost.Clear();
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                var room = services.Rooms.SaveRoom(selectedId, txtCategory.Text, txtIsBooked.Text, decimal.Parse(txtRCost.Text));
                MessageBox.Show("Room " + room.RId + " saved successfully.");
                PopulateGridView();
                ClearContent();
            });
        }

        private void btnClear_Click(object sender, EventArgs e) => ClearContent();

        private void btnDelete_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                var room = dgvRoom.CurrentRow?.DataBoundItem as Room;
                if (room == null) throw new HotelValidationException("Select a room to delete.");
                services.Rooms.DeleteRoom(room.RId);
                MessageBox.Show("Room " + room.RId + " has been deleted.");
                PopulateGridView();
                ClearContent();
            });
        }

        private void btnBack_Click(object sender, EventArgs e)
        {
            new ManagerDashboard(services).Show();
            Hide();
        }
    }
}
