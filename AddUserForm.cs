using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XRServer
{
    public partial class AddUserForm : Form
    {
        private TextBox txtUsername;
        private TextBox txtPassword;
        private ComboBox cmbRole;
        private Button btnOk;
        private Button btnCancel;

        public string UserNameValue => txtUsername.Text.Trim();
        public string PasswordValue => txtPassword.Text;
        public string RoleValue => cmbRole.SelectedItem?.ToString() ?? "viewer";

        public AddUserForm()
        {
            InitializeComponent();
            InitializeCustomUi();
        }

        private void InitializeCustomUi()
        {
            this.Text = "Add User";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(420, 220);

            var lblUsername = new Label
            {
                Left = 20,
                Top = 30,
                Width = 100,
                Text = "Username"
            };

            txtUsername = new TextBox
            {
                Left = 130,
                Top = 26,
                Width = 240
            };

            var lblPassword = new Label
            {
                Left = 20,
                Top = 75,
                Width = 100,
                Text = "Password"
            };

            txtPassword = new TextBox
            {
                Left = 130,
                Top = 71,
                Width = 240,
                PasswordChar = '*'
            };

            var lblRole = new Label
            {
                Left = 20,
                Top = 120,
                Width = 100,
                Text = "Role"
            };

            cmbRole = new ComboBox
            {
                Left = 130,
                Top = 116,
                Width = 140,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbRole.Items.AddRange(new[] { "admin", "editor", "viewer" });
            cmbRole.SelectedIndex = 2;

            btnOk = new Button
            {
                Left = 190,
                Top = 165,
                Width = 85,
                Text = "OK"
            };
            btnOk.Click += BtnOk_Click;

            btnCancel = new Button
            {
                Left = 285,
                Top = 165,
                Width = 85,
                Text = "Cancel"
            };
            btnCancel.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            this.Controls.Add(lblUsername);
            this.Controls.Add(txtUsername);
            this.Controls.Add(lblPassword);
            this.Controls.Add(txtPassword);
            this.Controls.Add(lblRole);
            this.Controls.Add(cmbRole);
            this.Controls.Add(btnOk);
            this.Controls.Add(btnCancel);
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(UserNameValue))
            {
                MessageBox.Show("Username を入力してください。", "Error");
                return;
            }

            if (string.IsNullOrWhiteSpace(PasswordValue))
            {
                MessageBox.Show("Password を入力してください。", "Error");
                return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}