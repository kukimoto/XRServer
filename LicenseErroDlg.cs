using System;
using System.Drawing;
using System.Windows.Forms;

namespace XRServer.Licensing
{
    public class LicenseErrorDialog : Form
    {
        private readonly string _deviceId;

        private Label lblMessage;
        private TextBox txtDeviceId;
        private Button btnCopy;
        private Button btnOk;

        public LicenseErrorDialog(string errorMessage, string deviceId)
        {
            _deviceId = deviceId ?? "";

            InitializeComponent();

            lblMessage.Text = errorMessage ?? "License Error";
            txtDeviceId.Text = _deviceId;
        }

        private void InitializeComponent()
        {
            this.Text = "License Error";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(720, 220);

            // エラーメッセージ表示
            lblMessage = new Label();
            lblMessage.AutoSize = false;
            lblMessage.Location = new Point(20, 20);
            lblMessage.Size = new Size(680, 60);
            lblMessage.TextAlign = ContentAlignment.MiddleLeft;

            // Device ID 表示用
            // ReadOnly + TextBox にすることで選択コピー可能
            txtDeviceId = new TextBox();
            txtDeviceId.Location = new Point(20, 100);
            txtDeviceId.Size = new Size(680, 28);
            txtDeviceId.ReadOnly = true;
            txtDeviceId.TabStop = true;

            // フォーカス時に全部選択したい場合
            txtDeviceId.Enter += (s, e) => txtDeviceId.SelectAll();
            txtDeviceId.Click += (s, e) => txtDeviceId.SelectAll();

            // 明示的なコピーボタン
            btnCopy = new Button();
            btnCopy.Text = "コピー";
            btnCopy.Location = new Point(440, 160);
            btnCopy.Size = new Size(120, 32);
            btnCopy.Click += BtnCopy_Click;

            // OKボタン
            // OK押下時にも自動コピーする
            btnOk = new Button();
            btnOk.Text = "OK";
            btnOk.Location = new Point(580, 160);
            btnOk.Size = new Size(120, 32);
            btnOk.Click += BtnOk_Click;

            this.AcceptButton = btnOk;

            this.Controls.Add(lblMessage);
            this.Controls.Add(txtDeviceId);
            this.Controls.Add(btnCopy);
            this.Controls.Add(btnOk);
        }

        private void BtnCopy_Click(object sender, EventArgs e)
        {
            CopyDeviceIdToClipboard();
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            // OK押下時に自動コピー
            CopyDeviceIdToClipboard();

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void CopyDeviceIdToClipboard()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_deviceId))
                {
                    Clipboard.SetText(_deviceId);
                }
            }
            catch
            {
                // クリップボード失敗時は無視
                // 必要ならログ出力に変えてもよい
            }
        }
    }
}