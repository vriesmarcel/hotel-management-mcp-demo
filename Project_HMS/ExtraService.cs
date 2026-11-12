using System;
using System.Linq;
using System.Windows.Forms;
using Hotel.Contracts;

namespace Project_HMS
{
    public partial class ExtraService : Form
    {
        private readonly DesktopServices services;
        private int? selectedId;

        public ExtraService(DesktopServices services)
        {
            this.services = services;
            InitializeComponent();
            txtSId.ReadOnly = true;
            ClearContent();
            PopulateGridView();
        }

        private void PopulateGridView()
        {
            DesktopUi.Run(() =>
            {
                dgvService.AutoGenerateColumns = false;
                dgvService.DataSource = services.Packages.GetServicePackages().ToList();
            });
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                var package = services.Packages.SaveServicePackage(selectedId, txtSName.Text, decimal.Parse(txtSCost.Text));
                MessageBox.Show("Service package " + package.SId + " saved successfully.");
                PopulateGridView();
                ClearContent();
            });
        }

        private void btnClear_Click(object sender, EventArgs e) => ClearContent();

        private void btnDelete_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                var package = dgvService.CurrentRow?.DataBoundItem as ServicePackage;
                if (package == null) throw new HotelValidationException("Select a service package to delete.");
                services.Packages.DeleteServicePackage(package.SId);
                MessageBox.Show("Service package " + package.SId + " has been deleted.");
                PopulateGridView();
                ClearContent();
            });
        }

        private void dgvService_DoubleClick(object sender, EventArgs e)
        {
            var package = dgvService.CurrentRow?.DataBoundItem as ServicePackage;
            if (package == null) return;
            selectedId = package.SId;
            txtSId.Text = package.SId.ToString();
            txtSName.Text = package.SName;
            txtSCost.Text = package.SPackageCost.ToString();
        }

        private void ClearContent()
        {
            selectedId = null;
            txtSId.Text = "New";
            txtSName.Clear();
            txtSCost.Clear();
        }

        private void ExtraService_FormClosed(object sender, FormClosedEventArgs e) => Application.Exit();

        private void btnBack_Click(object sender, EventArgs e)
        {
            new ManagerDashboard(services).Show();
            Hide();
        }
    }
}
