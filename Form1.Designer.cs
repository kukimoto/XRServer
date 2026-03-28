namespace XRServer
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            Console = new TextBox();
            Quit = new Button();
            Address = new Label();
            Setting = new Button();
            label1 = new Label();
            Clear = new Button();
            SuspendLayout();
            // 
            // Console
            // 
            Console.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Console.Location = new Point(22, 43);
            Console.Multiline = true;
            Console.Name = "Console";
            Console.ScrollBars = ScrollBars.Vertical;
            Console.Size = new Size(766, 360);
            Console.TabIndex = 0;
            // 
            // Quit
            // 
            Quit.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            Quit.Location = new Point(713, 415);
            Quit.Name = "Quit";
            Quit.Size = new Size(75, 23);
            Quit.TabIndex = 1;
            Quit.Text = "Quit";
            Quit.UseVisualStyleBackColor = true;
            Quit.Click += Quit_Click;
            // 
            // Address
            // 
            Address.AutoSize = true;
            Address.Location = new Point(114, 9);
            Address.Name = "Address";
            Address.Size = new Size(49, 15);
            Address.TabIndex = 2;
            Address.Text = "Address";
            Address.Click += label1_Click;
            // 
            // Setting
            // 
            Setting.Location = new Point(508, 415);
            Setting.Name = "Setting";
            Setting.Size = new Size(75, 23);
            Setting.TabIndex = 3;
            Setting.Text = "Preferene";
            Setting.UseVisualStyleBackColor = true;
            Setting.Click += btnSetting_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(49, 8);
            label1.Name = "label1";
            label1.Size = new Size(69, 15);
            label1.TabIndex = 4;
            label1.Text = "URL  http://";
            label1.TextAlign = ContentAlignment.MiddleRight;
            // 
            // Clear
            // 
            Clear.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            Clear.Location = new Point(589, 415);
            Clear.Name = "Clear";
            Clear.Size = new Size(75, 23);
            Clear.TabIndex = 5;
            Clear.Text = "Clear";
            Clear.UseVisualStyleBackColor = true;
            Clear.Click += Clear_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(Clear);
            Controls.Add(label1);
            Controls.Add(Setting);
            Controls.Add(Address);
            Controls.Add(Quit);
            Controls.Add(Console);
            Name = "Form1";
            Text = "Form1";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox Console;
        private Button Quit;
        private Label Address;
        private Button Setting;
        private Label label1;
        private Button Clear;
    }
}
