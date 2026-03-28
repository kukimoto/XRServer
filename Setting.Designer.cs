namespace XRServer
{
    partial class Setting
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            OK = new Button();
            label1 = new Label();
            portNo = new TextBox();
            label2 = new Label();
            label3 = new Label();
            label4 = new Label();
            label5 = new Label();
            label6 = new Label();
            label7 = new Label();
            wwwroot = new TextBox();
            Phproot = new TextBox();
            StoraegeDir = new TextBox();
            AllowedCidrs = new TextBox();
            GEMINI_API_KEY = new TextBox();
            GEMINI_MODEL = new TextBox();
            Close = new Button();
            SuspendLayout();
            // 
            // OK
            // 
            OK.Location = new Point(390, 287);
            OK.Name = "OK";
            OK.Size = new Size(75, 23);
            OK.TabIndex = 0;
            OK.Text = "OK";
            OK.UseVisualStyleBackColor = true;
            OK.Click += OK_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(32, 33);
            label1.Name = "label1";
            label1.Size = new Size(57, 15);
            label1.TabIndex = 1;
            label1.Text = "ポート番号";
            // 
            // portNo
            // 
            portNo.Location = new Point(167, 30);
            portNo.Name = "portNo";
            portNo.Size = new Size(295, 23);
            portNo.TabIndex = 2;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(32, 78);
            label2.Name = "label2";
            label2.Size = new Size(119, 15);
            label2.TabIndex = 3;
            label2.Text = "WWW ルートディレクトリ";
            //label2.Click += label2_Click;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(35, 115);
            label3.Name = "label3";
            label3.Size = new Size(102, 15);
            label3.TabIndex = 4;
            label3.Text = "php-cgi .exeのパス";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(34, 152);
            label4.Name = "label4";
            label4.Size = new Size(120, 15);
            label4.TabIndex = 5;
            label4.Text = "データベースのディレクトリ";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(35, 184);
            label5.Name = "label5";
            label5.Size = new Size(93, 15);
            label5.TabIndex = 6;
            label5.Text = "許可されたIP範囲";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(35, 216);
            label6.Name = "label6";
            label6.Size = new Size(106, 15);
            label6.TabIndex = 7;
            label6.Text = "GEMINI　API　キー";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(38, 251);
            label7.Name = "label7";
            label7.Size = new Size(75, 15);
            label7.TabIndex = 8;
            label7.Text = "GEMI　モデル";
            // 
            // wwwroot
            // 
            wwwroot.Location = new Point(167, 76);
            wwwroot.Name = "wwwroot";
            wwwroot.Size = new Size(295, 23);
            wwwroot.TabIndex = 9;
            // 
            // Phproot
            // 
            Phproot.Location = new Point(170, 115);
            Phproot.Name = "Phproot";
            Phproot.Size = new Size(295, 23);
            Phproot.TabIndex = 10;
            // 
            // StoraegeDir
            // 
            StoraegeDir.Location = new Point(170, 152);
            StoraegeDir.Name = "StoraegeDir";
            StoraegeDir.Size = new Size(295, 23);
            StoraegeDir.TabIndex = 11;
            // 
            // AllowedCidrs
            // 
            AllowedCidrs.Location = new Point(167, 184);
            AllowedCidrs.Name = "AllowedCidrs";
            AllowedCidrs.Size = new Size(295, 23);
            AllowedCidrs.TabIndex = 12;
            // 
            // GEMINI_API_KEY
            // 
            GEMINI_API_KEY.Location = new Point(167, 216);
            GEMINI_API_KEY.Name = "GEMINI_API_KEY";
            GEMINI_API_KEY.Size = new Size(295, 23);
            GEMINI_API_KEY.TabIndex = 13;
            // 
            // GEMINI_MODEL
            // 
            GEMINI_MODEL.Location = new Point(170, 248);
            GEMINI_MODEL.Name = "GEMINI_MODEL";
            GEMINI_MODEL.Size = new Size(295, 23);
            GEMINI_MODEL.TabIndex = 14;
            // 
            // Close
            // 
            Close.Location = new Point(309, 287);
            Close.Name = "Close";
            Close.Size = new Size(75, 23);
            Close.TabIndex = 15;
            Close.Text = "Close";
            Close.UseVisualStyleBackColor = true;
            // 
            // Setting
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(495, 331);
            Controls.Add(Close);
            Controls.Add(GEMINI_MODEL);
            Controls.Add(GEMINI_API_KEY);
            Controls.Add(AllowedCidrs);
            Controls.Add(StoraegeDir);
            Controls.Add(Phproot);
            Controls.Add(wwwroot);
            Controls.Add(label7);
            Controls.Add(label6);
            Controls.Add(label5);
            Controls.Add(label4);
            Controls.Add(label3);
            Controls.Add(label2);
            Controls.Add(portNo);
            Controls.Add(label1);
            Controls.Add(OK);
            Name = "Setting";
            Text = "Setting";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button OK;
        private Label label1;
        private TextBox portNo;
        private Label label2;
        private Label label3;
        private Label label4;
        private Label label5;
        private Label label6;
        private Label label7;
        private TextBox wwwroot;
        private TextBox Phproot;
        private TextBox StoraegeDir;
        private TextBox AllowedCidrs;
        private TextBox GEMINI_API_KEY;
        private TextBox GEMINI_MODEL;
        private Button Close;
    }
}