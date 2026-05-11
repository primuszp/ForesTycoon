namespace ForesTycoon
{
    partial class MainForm
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
            viewport = new Viewport();
            SuspendLayout();
            // 
            // viewport
            // 
            viewport.BackColor = System.Drawing.Color.Black;
            viewport.Dock = System.Windows.Forms.DockStyle.Fill;
            viewport.Location = new System.Drawing.Point(0, 0);
            viewport.Margin = new System.Windows.Forms.Padding(5, 3, 5, 3);
            viewport.Name = "viewport";
            viewport.Size = new System.Drawing.Size(1264, 681);
            viewport.TabIndex = 0;
            viewport.VSync = false;
            // 
            // Form1
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(1264, 681);
            Controls.Add(viewport);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MinimumSize = new System.Drawing.Size(1117, 617);
            Name = "Form1";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "ForesTycoon";
            WindowState = System.Windows.Forms.FormWindowState.Maximized;
            ResumeLayout(false);

        }

        #endregion

        private Viewport viewport;
    }
}

