using System;
using System.Windows.Forms;

namespace Project_HMS
{
    public partial class Login : Form
    {
        private readonly DesktopServices services;

        public Login(DesktopServices services)
        {
            this.services = services;
            InitializeComponent();
        }

        private void btnLogin_Click(object sender, EventArgs e)
        {
            DesktopUi.Run(() =>
            {
                var user = services.Authentication.Authenticate(txtUserid.Text, txtPassword.Text);
                txtPassword.Clear();
                if (user == null)
                {
                    MessageBox.Show("Login Invalid");
                    lblWrong.Visible = true;
                    txtUserid.Clear();
                    return;
                }
                if (user.Role != "Manager")
                {
                    MessageBox.Show("No desktop workflow is implemented for this role.");
                    return;
                }
                lblWrong.Visible = false;
                MessageBox.Show("Login Valid");
                new ManagerDashboard(services).Show();
                Hide();
            });
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            txtUserid.Clear();
            txtPassword.Clear();
            lblWrong.Visible = false;
        }
    }
}
