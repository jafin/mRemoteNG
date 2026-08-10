using mRemoteNG.UI.Controls;

namespace mRemoteNG.UI.Forms.OptionsPages
{

    public sealed partial class TerminalPage : OptionsPage
    {

        //UserControl overrides dispose to clean up the component list.
        [System.Diagnostics.DebuggerNonUserCode()]
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing && components != null)
                {
                    components.Dispose();
                }
            }
            finally
            {
                try { base.Dispose(disposing); }
                catch (System.NullReferenceException) { /* finalizer-safe: Control.ContextMenuStrip may be null on non-STA thread */ }
            }
        }

        //Required by the Windows Form Designer
        private System.ComponentModel.Container components = null;

        [System.Diagnostics.DebuggerStepThrough()]
        private void InitializeComponent()
        {
            lblIntro = new MrngLabel();
            lblFontFamily = new MrngLabel();
            txtFontFamily = new MrngTextBox();
            lblFontSize = new MrngLabel();
            numFontSize = new System.Windows.Forms.NumericUpDown();
            lblColorScheme = new MrngLabel();
            cboColorScheme = new MrngComboBox();
            lblScrollback = new MrngLabel();
            numScrollback = new System.Windows.Forms.NumericUpDown();
            chkCtrlVPastes = new MrngCheckBox();
            lblCtrlVNote = new MrngLabel();
            ((System.ComponentModel.ISupportInitialize)numFontSize).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numScrollback).BeginInit();
            SuspendLayout();
            //
            // lblIntro
            //
            lblIntro.AutoSize = true;
            lblIntro.Location = new System.Drawing.Point(6, 10);
            lblIntro.Name = "lblIntro";
            lblIntro.Size = new System.Drawing.Size(400, 15);
            lblIntro.TabIndex = 0;
            lblIntro.Text = "Applies to connections using the native SSH terminal.";
            //
            // lblFontFamily
            //
            lblFontFamily.AutoSize = true;
            lblFontFamily.Location = new System.Drawing.Point(6, 44);
            lblFontFamily.Name = "lblFontFamily";
            lblFontFamily.Size = new System.Drawing.Size(70, 15);
            lblFontFamily.TabIndex = 1;
            lblFontFamily.Text = "Font";
            //
            // txtFontFamily
            //
            txtFontFamily.Location = new System.Drawing.Point(160, 41);
            txtFontFamily.Name = "txtFontFamily";
            txtFontFamily.Size = new System.Drawing.Size(280, 23);
            txtFontFamily.TabIndex = 2;
            //
            // lblFontSize
            //
            lblFontSize.AutoSize = true;
            lblFontSize.Location = new System.Drawing.Point(6, 76);
            lblFontSize.Name = "lblFontSize";
            lblFontSize.Size = new System.Drawing.Size(70, 15);
            lblFontSize.TabIndex = 3;
            lblFontSize.Text = "Font size";
            //
            // numFontSize
            //
            numFontSize.Location = new System.Drawing.Point(160, 73);
            numFontSize.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
            numFontSize.Minimum = new decimal(new int[] { 6, 0, 0, 0 });
            numFontSize.Name = "numFontSize";
            numFontSize.Size = new System.Drawing.Size(80, 23);
            numFontSize.TabIndex = 4;
            numFontSize.Value = new decimal(new int[] { 14, 0, 0, 0 });
            //
            // lblColorScheme
            //
            lblColorScheme.AutoSize = true;
            lblColorScheme.Location = new System.Drawing.Point(6, 108);
            lblColorScheme.Name = "lblColorScheme";
            lblColorScheme.Size = new System.Drawing.Size(70, 15);
            lblColorScheme.TabIndex = 5;
            lblColorScheme.Text = "Colour scheme";
            //
            // cboColorScheme
            //
            cboColorScheme.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cboColorScheme.Location = new System.Drawing.Point(160, 105);
            cboColorScheme.Name = "cboColorScheme";
            cboColorScheme.Size = new System.Drawing.Size(280, 23);
            cboColorScheme.TabIndex = 6;
            //
            // lblScrollback
            //
            lblScrollback.AutoSize = true;
            lblScrollback.Location = new System.Drawing.Point(6, 140);
            lblScrollback.Name = "lblScrollback";
            lblScrollback.Size = new System.Drawing.Size(70, 15);
            lblScrollback.TabIndex = 7;
            lblScrollback.Text = "Scrollback lines";
            //
            // numScrollback
            //
            numScrollback.Increment = new decimal(new int[] { 500, 0, 0, 0 });
            numScrollback.Location = new System.Drawing.Point(160, 137);
            numScrollback.Maximum = new decimal(new int[] { 200000, 0, 0, 0 });
            numScrollback.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numScrollback.Name = "numScrollback";
            numScrollback.Size = new System.Drawing.Size(120, 23);
            numScrollback.TabIndex = 8;
            numScrollback.Value = new decimal(new int[] { 5000, 0, 0, 0 });
            //
            // chkCtrlVPastes
            //
            chkCtrlVPastes.AutoSize = true;
            chkCtrlVPastes.Location = new System.Drawing.Point(9, 180);
            chkCtrlVPastes.Name = "chkCtrlVPastes";
            chkCtrlVPastes.Size = new System.Drawing.Size(300, 19);
            chkCtrlVPastes.TabIndex = 9;
            chkCtrlVPastes.Text = "Ctrl+V pastes";
            chkCtrlVPastes.UseVisualStyleBackColor = true;
            //
            // lblCtrlVNote
            //
            lblCtrlVNote.Location = new System.Drawing.Point(26, 202);
            lblCtrlVNote.Name = "lblCtrlVNote";
            lblCtrlVNote.Size = new System.Drawing.Size(430, 46);
            lblCtrlVNote.TabIndex = 10;
            lblCtrlVNote.Text = "Ctrl+V note";
            //
            // TerminalPage
            //
            Controls.Add(lblIntro);
            Controls.Add(lblFontFamily);
            Controls.Add(txtFontFamily);
            Controls.Add(lblFontSize);
            Controls.Add(numFontSize);
            Controls.Add(lblColorScheme);
            Controls.Add(cboColorScheme);
            Controls.Add(lblScrollback);
            Controls.Add(numScrollback);
            Controls.Add(chkCtrlVPastes);
            Controls.Add(lblCtrlVNote);
            Name = "TerminalPage";
            Size = new System.Drawing.Size(560, 350);
            ((System.ComponentModel.ISupportInitialize)numFontSize).EndInit();
            ((System.ComponentModel.ISupportInitialize)numScrollback).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        internal MrngLabel lblIntro;
        internal MrngLabel lblFontFamily;
        internal MrngTextBox txtFontFamily;
        internal MrngLabel lblFontSize;
        internal System.Windows.Forms.NumericUpDown numFontSize;
        internal MrngLabel lblColorScheme;
        internal MrngComboBox cboColorScheme;
        internal MrngLabel lblScrollback;
        internal System.Windows.Forms.NumericUpDown numScrollback;
        internal MrngCheckBox chkCtrlVPastes;
        internal MrngLabel lblCtrlVNote;
    }
}
