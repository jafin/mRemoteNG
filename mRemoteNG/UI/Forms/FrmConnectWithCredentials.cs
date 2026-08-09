using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.UI.Forms;

[SupportedOSPlatform("windows")]
public partial class FrmConnectWithCredentials : Form
{
    public string Username => txtUsername.Text;

    public string Password => txtPassword.Text;

    public string Domain => txtDomain.Text;

    private Label lblUsername = null!;
    private TextBox txtUsername = null!;
    private Label lblPassword = null!;
    private TextBox txtPassword = null!;
    private Label lblDomain = null!;
    private TextBox txtDomain = null!;
    private Button btnConnect = null!;
    private Button btnCancel = null!;

    public FrmConnectWithCredentials(string defaultUsername, string defaultDomain)
    {
        InitializeComponent();
        txtUsername.Text = defaultUsername;
        txtDomain.Text = defaultDomain;

        Text = "Connect with Credentials";
        // Attempt to look up localized string for title if possible, but "Connect with Credentials" is fine for now.
        // We can check if Language.ConnectWithCredentials exists later, but I didn't see it in the truncated list.

        btnConnect.Text = Language.Connect;
        btnCancel.Text = Language._Cancel;
        lblUsername.Text = Language.Username;
        lblPassword.Text = Language.Password;
        lblDomain.Text = Language.Domain;
    }

    private void InitializeComponent()
    {
        lblUsername = new Label();
        txtUsername = new TextBox();
        lblPassword = new Label();
        txtPassword = new TextBox();
        lblDomain = new Label();
        txtDomain = new TextBox();
        btnConnect = new Button();
        btnCancel = new Button();
        SuspendLayout();
        //
        // lblUsername
        //
        lblUsername.AutoSize = true;
        lblUsername.Location = new System.Drawing.Point(12, 15);
        lblUsername.Name = "lblUsername";
        lblUsername.Size = new System.Drawing.Size(58, 13);
        lblUsername.TabIndex = 0;
        lblUsername.Text = "Username:";
        //
        // txtUsername
        //
        txtUsername.Location = new System.Drawing.Point(80, 12);
        txtUsername.Name = "txtUsername";
        txtUsername.Size = new System.Drawing.Size(192, 20);
        txtUsername.TabIndex = 1;
        //
        // lblPassword
        //
        lblPassword.AutoSize = true;
        lblPassword.Location = new System.Drawing.Point(12, 41);
        lblPassword.Name = "lblPassword";
        lblPassword.Size = new System.Drawing.Size(56, 13);
        lblPassword.TabIndex = 2;
        lblPassword.Text = "Password:";
        //
        // txtPassword
        //
        txtPassword.Location = new System.Drawing.Point(80, 38);
        txtPassword.Name = "txtPassword";
        txtPassword.Size = new System.Drawing.Size(192, 20);
        txtPassword.TabIndex = 3;
        txtPassword.UseSystemPasswordChar = true;
        //
        // lblDomain
        //
        lblDomain.AutoSize = true;
        lblDomain.Location = new System.Drawing.Point(12, 67);
        lblDomain.Name = "lblDomain";
        lblDomain.Size = new System.Drawing.Size(46, 13);
        lblDomain.TabIndex = 4;
        lblDomain.Text = "Domain:";
        //
        // txtDomain
        //
        txtDomain.Location = new System.Drawing.Point(80, 64);
        txtDomain.Name = "txtDomain";
        txtDomain.Size = new System.Drawing.Size(192, 20);
        txtDomain.TabIndex = 5;
        //
        // btnConnect
        //
        btnConnect.DialogResult = DialogResult.OK;
        btnConnect.Location = new System.Drawing.Point(116, 100);
        btnConnect.Name = "btnConnect";
        btnConnect.Size = new System.Drawing.Size(75, 23);
        btnConnect.TabIndex = 6;
        btnConnect.Text = "Connect";
        btnConnect.UseVisualStyleBackColor = true;
        //
        // btnCancel
        //
        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Location = new System.Drawing.Point(197, 100);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new System.Drawing.Size(75, 23);
        btnCancel.TabIndex = 7;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;
        //
        // FrmConnectWithCredentials
        //
        AcceptButton = btnConnect;
        CancelButton = btnCancel;
        AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(284, 135);
        Controls.Add(btnCancel);
        Controls.Add(btnConnect);
        Controls.Add(txtDomain);
        Controls.Add(lblDomain);
        Controls.Add(txtPassword);
        Controls.Add(lblPassword);
        Controls.Add(txtUsername);
        Controls.Add(lblUsername);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "FrmConnectWithCredentials";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Connect with Credentials";
        ResumeLayout(false);
        PerformLayout();

    }
}