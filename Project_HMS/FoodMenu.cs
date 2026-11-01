using System;
using System.Linq;
using System.Windows.Forms;
using Hotel.Contracts;

namespace Project_HMS
{
    public partial class FoodMenu : Form
    {
        private readonly DesktopServices services;
        private int? selectedId;

        public FoodMenu(DesktopServices services)
        {
            this.services = services;
            InitializeComponent();
            txtFId.ReadOnly = true;
            ClearContent();
            PopulateGridView();
        }

        private void PopulateGridView()
        {
            DesktopUi.Run(() =>
            {
                dgvFood.AutoGenerateColumns = false;
                dgvFood.DataSource = services.Packages.GetFoodPackages().ToList();
            });
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                var package = services.Packages.SaveFoodPackage(selectedId, txtFName.Text, decimal.Parse(txtFCost.Text));
                MessageBox.Show("Food package " + package.FId + " saved successfully.");
                PopulateGridView();
                ClearContent();
            });
        }

        private void btnClear_Click(object sender, EventArgs e) => ClearContent();

        private void btnDelete_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                var package = dgvFood.CurrentRow?.DataBoundItem as FoodPackage;
                if (package == null) throw new HotelValidationException("Select a food package to delete.");
                services.Packages.DeleteFoodPackage(package.FId);
                MessageBox.Show("Food package " + package.FId + " has been deleted.");
                PopulateGridView();
                ClearContent();
            });
        }

        private void dgvFood_DoubleClick(object sender, EventArgs e)
        {
            var package = dgvFood.CurrentRow?.DataBoundItem as FoodPackage;
            if (package == null) return;
            selectedId = package.FId;
            txtFId.Text = package.FId.ToString();
            txtFName.Text = package.FName;
            txtFCost.Text = package.FPackageCost.ToString();
        }

        private void ClearContent()
        {
            selectedId = null;
            txtFId.Text = "New";
            txtFName.Clear();
            txtFCost.Clear();
        }

        private void FoodMenu_FormClosed(object sender, FormClosedEventArgs e) => Application.Exit();

        private void btnBack_Click(object sender, EventArgs e)
        {
            new ManagerDashboard(services).Show();
            Hide();
        }
    }
}
