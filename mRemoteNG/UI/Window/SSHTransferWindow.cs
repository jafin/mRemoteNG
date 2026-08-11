using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.Connection.Protocol.SSH.Native.HostKeys;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Security.Ssh.Agent;
using mRemoteNG.Tools;
using mRemoteNG.UI.Controls;
using mRemoteNG.UI.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace mRemoteNG.UI.Window;

[SupportedOSPlatform("windows")]
public class SSHTransferWindow : BaseWindow
{
    #region Form Init

    private MrngProgressBar pbStatus = null!;
    private MrngButton btnTransfer = null!;
    private MrngTextBox txtUser = null!;
    private MrngTextBox txtPassword = null!;
    private MrngTextBox txtHost = null!;
    private MrngTextBox txtPort = null!;
    private MrngLabel lblHost = null!;
    private MrngLabel lblPort = null!;
    private MrngLabel lblUser = null!;
    private MrngLabel lblPassword = null!;
    private MrngLabel lblProtocol = null!;
    private MrngRadioButton radProtSCP = null!;
    private MrngRadioButton radProtSFTP = null!;
    private MrngGroupBox grpConnection = null!;
    private MrngButton btnBrowse = null!;
    private MrngLabel lblRemoteFile = null!;
    private MrngTextBox txtRemoteFile = null!;
    private MrngTextBox txtLocalFile = null!;
    private MrngLabel lblLocalFile = null!;
    private MrngGroupBox grpFiles = null!;

    private void InitializeComponent()
    {
        System.ComponentModel.ComponentResourceManager resources =
            new(typeof(SSHTransferWindow));
        grpFiles = new MrngGroupBox();
        lblLocalFile = new MrngLabel();
        txtLocalFile = new MrngTextBox();
        btnTransfer = new MrngButton();
        txtRemoteFile = new MrngTextBox();
        lblRemoteFile = new MrngLabel();
        btnBrowse = new MrngButton();
        grpConnection = new MrngGroupBox();
        radProtSFTP = new MrngRadioButton();
        radProtSCP = new MrngRadioButton();
        lblProtocol = new MrngLabel();
        lblPassword = new MrngLabel();
        lblUser = new MrngLabel();
        lblPort = new MrngLabel();
        lblHost = new MrngLabel();
        txtPort = new MrngTextBox();
        txtHost = new MrngTextBox();
        txtPassword = new MrngTextBox();
        txtUser = new MrngTextBox();
        pbStatus = new MrngProgressBar();
        grpFiles.SuspendLayout();
        grpConnection.SuspendLayout();
        SuspendLayout();
        // 
        // grpFiles
        // 
        grpFiles.Controls.Add(lblLocalFile);
        grpFiles.Controls.Add(txtLocalFile);
        grpFiles.Controls.Add(btnTransfer);
        grpFiles.Controls.Add(txtRemoteFile);
        grpFiles.Controls.Add(lblRemoteFile);
        grpFiles.Controls.Add(btnBrowse);
        grpFiles.FlatStyle = FlatStyle.Flat;
        grpFiles.Location = new System.Drawing.Point(12, 172);
        grpFiles.Name = "grpFiles";
        grpFiles.Size = new System.Drawing.Size(668, 175);
        grpFiles.TabIndex = 2000;
        grpFiles.TabStop = false;
        grpFiles.Text = "Files";
        // 
        // lblLocalFile
        // 
        lblLocalFile.AutoSize = true;
        lblLocalFile.Location = new System.Drawing.Point(6, 30);
        lblLocalFile.Name = "lblLocalFile";
        lblLocalFile.Size = new System.Drawing.Size(55, 13);
        lblLocalFile.TabIndex = 10;
        lblLocalFile.Text = "Local file:";
        // 
        // txtLocalFile
        // 
        txtLocalFile.BorderStyle = BorderStyle.FixedSingle;
        txtLocalFile.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular,
            System.Drawing.GraphicsUnit.Point, ((byte)(0)));
        txtLocalFile.Location = new System.Drawing.Point(105, 28);
        txtLocalFile.Name = "txtLocalFile";
        txtLocalFile.Size = new System.Drawing.Size(455, 22);
        txtLocalFile.TabIndex = 20;
        // 
        // btnTransfer
        // 
        btnTransfer._mice = MrngButton.MouseState.HOVER;
        btnTransfer.FlatStyle = FlatStyle.Flat;
        btnTransfer.Image = Properties.Resources.SyncArrow_16x;
        btnTransfer.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
        btnTransfer.Location = new System.Drawing.Point(562, 145);
        btnTransfer.Name = "btnTransfer";
        btnTransfer.Size = new System.Drawing.Size(100, 24);
        btnTransfer.TabIndex = 10000;
        btnTransfer.Text = "Transfer";
        btnTransfer.UseVisualStyleBackColor = true;
        btnTransfer.Click += new EventHandler(btnTransfer_Click);
        // 
        // txtRemoteFile
        // 
        txtRemoteFile.BorderStyle = BorderStyle.FixedSingle;
        txtRemoteFile.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular,
            System.Drawing.GraphicsUnit.Point, ((byte)(0)));
        txtRemoteFile.Location = new System.Drawing.Point(105, 60);
        txtRemoteFile.Name = "txtRemoteFile";
        txtRemoteFile.Size = new System.Drawing.Size(542, 22);
        txtRemoteFile.TabIndex = 50;
        // 
        // lblRemoteFile
        // 
        lblRemoteFile.AutoSize = true;
        lblRemoteFile.Location = new System.Drawing.Point(6, 67);
        lblRemoteFile.Name = "lblRemoteFile";
        lblRemoteFile.Size = new System.Drawing.Size(68, 13);
        lblRemoteFile.TabIndex = 40;
        lblRemoteFile.Text = "Remote file:";
        // 
        // btnBrowse
        // 
        btnBrowse._mice = MrngButton.MouseState.HOVER;
        btnBrowse.FlatStyle = FlatStyle.Flat;
        btnBrowse.Location = new System.Drawing.Point(566, 28);
        btnBrowse.Name = "btnBrowse";
        btnBrowse.Size = new System.Drawing.Size(81, 22);
        btnBrowse.TabIndex = 30;
        btnBrowse.Text = "Browse";
        btnBrowse.UseVisualStyleBackColor = true;
        btnBrowse.Click += new EventHandler(btnBrowse_Click);
        // 
        // grpConnection
        // 
        grpConnection.Controls.Add(radProtSFTP);
        grpConnection.Controls.Add(radProtSCP);
        grpConnection.Controls.Add(lblProtocol);
        grpConnection.Controls.Add(lblPassword);
        grpConnection.Controls.Add(lblUser);
        grpConnection.Controls.Add(lblPort);
        grpConnection.Controls.Add(lblHost);
        grpConnection.Controls.Add(txtPort);
        grpConnection.Controls.Add(txtHost);
        grpConnection.Controls.Add(txtPassword);
        grpConnection.Controls.Add(txtUser);
        grpConnection.FlatStyle = FlatStyle.Flat;
        grpConnection.Location = new System.Drawing.Point(12, 12);
        grpConnection.Name = "grpConnection";
        grpConnection.Size = new System.Drawing.Size(668, 154);
        grpConnection.TabIndex = 1000;
        grpConnection.TabStop = false;
        grpConnection.Text = "Connection";
        // 
        // radProtSFTP
        // 
        radProtSFTP.AutoSize = true;
        radProtSFTP.FlatStyle = FlatStyle.Flat;
        radProtSFTP.Location = new System.Drawing.Point(164, 113);
        radProtSFTP.Name = "radProtSFTP";
        radProtSFTP.Size = new System.Drawing.Size(47, 17);
        radProtSFTP.TabIndex = 90;
        radProtSFTP.Text = "SFTP";
        radProtSFTP.UseVisualStyleBackColor = true;
        // 
        // radProtSCP
        // 
        radProtSCP.AutoSize = true;
        radProtSCP.Checked = true;
        radProtSCP.FlatStyle = FlatStyle.Flat;
        radProtSCP.Location = new System.Drawing.Point(105, 113);
        radProtSCP.Name = "radProtSCP";
        radProtSCP.Size = new System.Drawing.Size(43, 17);
        radProtSCP.TabIndex = 80;
        radProtSCP.TabStop = true;
        radProtSCP.Text = "SCP";
        radProtSCP.UseVisualStyleBackColor = true;
        // 
        // lblProtocol
        // 
        lblProtocol.AutoSize = true;
        lblProtocol.Location = new System.Drawing.Point(6, 117);
        lblProtocol.Name = "lblProtocol";
        lblProtocol.Size = new System.Drawing.Size(53, 13);
        lblProtocol.TabIndex = 90;
        lblProtocol.Text = "Protocol:";
        // 
        // lblPassword
        // 
        lblPassword.AutoSize = true;
        lblPassword.Location = new System.Drawing.Point(6, 88);
        lblPassword.Name = "lblPassword";
        lblPassword.Size = new System.Drawing.Size(59, 13);
        lblPassword.TabIndex = 70;
        lblPassword.Text = "Password:";
        // 
        // lblUser
        // 
        lblUser.AutoSize = true;
        lblUser.Location = new System.Drawing.Point(6, 58);
        lblUser.Name = "lblUser";
        lblUser.Size = new System.Drawing.Size(33, 13);
        lblUser.TabIndex = 50;
        lblUser.Text = "User:";
        // 
        // lblPort
        // 
        lblPort.AutoSize = true;
        lblPort.Location = new System.Drawing.Point(228, 115);
        lblPort.Name = "lblPort";
        lblPort.Size = new System.Drawing.Size(31, 13);
        lblPort.TabIndex = 30;
        lblPort.Text = "Port:";
        // 
        // lblHost
        // 
        lblHost.AutoSize = true;
        lblHost.Location = new System.Drawing.Point(6, 27);
        lblHost.Name = "lblHost";
        lblHost.Size = new System.Drawing.Size(34, 13);
        lblHost.TabIndex = 10;
        lblHost.Text = "Host:";
        // 
        // txtPort
        // 
        txtPort.BorderStyle = BorderStyle.FixedSingle;
        txtPort.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular,
            System.Drawing.GraphicsUnit.Point, ((byte)(0)));
        txtPort.Location = new System.Drawing.Point(271, 110);
        txtPort.Name = "txtPort";
        txtPort.Size = new System.Drawing.Size(30, 22);
        txtPort.TabIndex = 100;
        txtPort.Text = "22";
        txtPort.TextAlign = HorizontalAlignment.Center;
        // 
        // txtHost
        // 
        txtHost.BorderStyle = BorderStyle.FixedSingle;
        txtHost.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular,
            System.Drawing.GraphicsUnit.Point, ((byte)(0)));
        txtHost.Location = new System.Drawing.Point(105, 19);
        txtHost.Name = "txtHost";
        txtHost.Size = new System.Drawing.Size(471, 22);
        txtHost.TabIndex = 20;
        // 
        // txtPassword
        // 
        txtPassword.BorderStyle = BorderStyle.FixedSingle;
        txtPassword.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular,
            System.Drawing.GraphicsUnit.Point, ((byte)(0)));
        txtPassword.Location = new System.Drawing.Point(105, 81);
        txtPassword.Name = "txtPassword";
        txtPassword.Size = new System.Drawing.Size(471, 22);
        txtPassword.TabIndex = 60;
        txtPassword.UseSystemPasswordChar = true;
        // 
        // txtUser
        // 
        txtUser.BorderStyle = BorderStyle.FixedSingle;
        txtUser.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular,
            System.Drawing.GraphicsUnit.Point, ((byte)(0)));
        txtUser.Location = new System.Drawing.Point(105, 51);
        txtUser.Name = "txtUser";
        txtUser.Size = new System.Drawing.Size(471, 22);
        txtUser.TabIndex = 40;
        // 
        // pbStatus
        // 
        pbStatus.Location = new System.Drawing.Point(12, 353);
        pbStatus.Name = "pbStatus";
        pbStatus.Size = new System.Drawing.Size(668, 23);
        pbStatus.Style = ProgressBarStyle.Continuous;
        pbStatus.TabIndex = 3000;
        // 
        // SSHTransferWindow
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new System.Drawing.Size(692, 423);
        Controls.Add(grpFiles);
        Controls.Add(grpConnection);
        Controls.Add(pbStatus);
        Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular,
            System.Drawing.GraphicsUnit.Point, ((byte)(0)));
        Name = "SSHTransferWindow";
        TabText = "SSH File Transfer";
        Text = "SSH File Transfer";
        Load += new EventHandler(SSHTransfer_Load);
        grpFiles.ResumeLayout(false);
        grpFiles.PerformLayout();
        grpConnection.ResumeLayout(false);
        grpConnection.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    #region Private Properties

    private readonly OpenFileDialog oDlg;

    #endregion

    #region Public Properties

    public string Hostname
    {
        get => txtHost.Text;
        set => txtHost.Text = value;
    }

    public string Port
    {
        get => txtPort.Text;
        set => txtPort.Text = value;
    }

    public string Username
    {
        get => txtUser.Text;
        set => txtUser.Text = value;
    }

    public string Password
    {
        get => txtPassword.Text;
        set => txtPassword.Text = value;
    }

    #endregion

    #region Form Stuff

    private void SSHTransfer_Load(object sender, EventArgs e)
    {
        ApplyTheme();
        ApplyLanguage();
        Icon = Resources.ImageConverter.GetImageAsIcon(Properties.Resources.SyncArrow_16x);
        DisplayProperties display = new();
        if (btnTransfer.Image is not null)
            btnTransfer.Image = display.ScaleImage(btnTransfer.Image);
    }

    private void ApplyLanguage()
    {
        grpFiles.Text = Language.Files;
        lblLocalFile.Text = Language.LocalFile + ":";
        lblRemoteFile.Text = Language.RemoteFile + ":";
        btnBrowse.Text = Language._Browse;
        grpConnection.Text = Language.Connection;
        lblProtocol.Text = Language.Protocol;
        lblPassword.Text = Language.Password;
        lblUser.Text = Language.User + ":";
        lblPort.Text = Language.Port;
        lblHost.Text = Language.Host + ":";
        btnTransfer.Text = Language.Transfer;
        TabText = Language.Transfer;
        Text = Language.Transfer;
    }

    #endregion

    #region Private Methods

    private SecureTransfer? st;

    private void StartTransfer(SecureTransfer.SshTransferProtocol Protocol)
    {
        if (AllFieldsSet() == false)
        {
            Runtime.MessageCollector.AddMessage(MessageClass.ErrorMsg, Language.PleaseFillAllFields);
            return;
        }

        if (File.Exists(txtLocalFile.Text) == false)
        {
            Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, Language.LocalFileDoesNotExist);
            return;
        }

        try
        {
            // This window has never verified host keys, so a user transferring to a host they have
            // never opened a session to will now be asked. The store is the shared one, so a host
            // already accepted anywhere in the application costs nothing here. The verifier
            // marshals to this window's thread, which is free because the connection is made on
            // the transfer thread — a dialog cannot be shown by the thread waiting for it.
            HostKeyGate hostKeys = new(SharedHostKeyStore.Instance, new DialogHostKeyVerifier(this));

            // Built here, connected on the background thread: everything in this statement reads a
            // control, and controls belong to this thread.
            st = new SecureTransfer(txtHost.Text, int.Parse(txtPort.Text, CultureInfo.InvariantCulture),
                BuildCredential(), Protocol, txtLocalFile.Text, txtRemoteFile.Text, hostKeys);
            st.UploadProgress += SecureTransfer_UploadProgress;
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace(Language.SshTransferFailed, ex);
            st?.Dispose();
            st = null;
            return;
        }

        // Before the thread starts, not inside it. The window no longer freezes while connecting,
        // so a second click would otherwise start a second transfer and abandon the first.
        DisableButtons();

        Thread t = new(StartTransferBG);
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
    }

    /// <summary>
    /// Builds the credential for a transfer from the fields on this window, consulting an SSH
    /// agent when one is enabled.
    /// </summary>
    /// <remarks>
    /// This window is standalone — it has no <see cref="Connection.ConnectionInfo"/> to resolve
    /// against and no external credential provider, so it goes straight to the neutral
    /// credential rather than through <c>SshCredentialResolver</c>. Consulting the agent here is
    /// what makes an agent-authenticated transfer possible at all: previously the only
    /// credential this window could offer was the typed password.
    /// </remarks>
    private ResolvedSshCredential BuildCredential()
    {
        IReadOnlyList<SshAgentIdentity> agentIdentities = SshAgentSettings.Default.IsEnabled
            ? new SshNetAgentProvider().GetIdentities(SshAgentQuery.Default)
            : [];

        return new ResolvedSshCredential(txtUser.Text,
            secret: txtPassword.Text,
            agentIdentities: agentIdentities);
    }

    private void SecureTransfer_UploadProgress(object? sender, SecureTransferProgressEventArgs e)
    {
        if (e.Total <= 0)
            return;

        // If the file size is over 2 gigs, convert to kb. This means we'll support a 2TB file.
        bool inKilobytes = e.Total > int.MaxValue;

        SshTransfer_Progress(
            Convert.ToInt32(inKilobytes ? e.Transferred / 1024 : e.Transferred),
            Convert.ToInt32(inKilobytes ? e.Total / 1024 : e.Total));
    }

    private void StartTransferBG()
    {
        SecureTransfer? transfer = st;
        if (transfer is null)
            return;

        try
        {
            try
            {
                // Connecting here rather than on the UI thread. It blocks for the whole handshake,
                // and the host key question is asked inside it: a dialog that marshals to this
                // window could never be answered by the thread that was waiting for the connection.
                // It also stops an unreachable host freezing the application until TCP gives up.
                transfer.Connect();
            }
            catch (Exception ex)
            {
                // The message a failed connect reported when it ran on the UI thread. A connection
                // that was never made is not a transfer that failed part way through.
                Runtime.MessageCollector.AddExceptionStackTrace(Language.SshTransferFailed, ex);
                return;
            }

            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg,
                $"Transfer of {Path.GetFileName(transfer.SrcFile)} started.", true);
            // This thread exists to keep the upload off the UI thread, so blocking it here is
            // the point. Progress now arrives on UploadProgress as bytes go out, which replaces
            // the 50 ms poll of SftpUploadAsyncResult.UploadedBytes that this used to run.
            transfer.UploadAsync().GetAwaiter().GetResult();

            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg,
                $"Transfer of {Path.GetFileName(transfer.SrcFile)} completed.", true);
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace(Language.SshBackgroundTransferFailed, ex,
                MessageClass.ErrorMsg, false);
        }
        finally
        {
            transfer.Disconnect();
            transfer.Dispose();

            // However this ended. The button is disabled before the thread starts now, so leaving
            // it disabled on a failure would put the window out of service until it was reopened.
            EnableButtons();
        }
    }

    private bool AllFieldsSet()
    {
        if (txtHost.Text != "" && txtPort.Text != "" && txtUser.Text != "" && txtLocalFile.Text != "" &&
            txtRemoteFile.Text != "")
        {
            if (txtPassword.Text == "")
            {
                if (MessageBox.Show(FrmMain.Default, Language.EmptyPasswordContinue, @"Question?",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                {
                    return false;
                }
            }

            if (txtRemoteFile.Text.EndsWith('/') || txtRemoteFile.Text.EndsWith('\\'))
            {
                txtRemoteFile.Text +=
                    txtLocalFile.Text[(txtLocalFile.Text.LastIndexOf('\\') + 1)..];
            }

            return true;
        }
        else
        {
            return false;
        }
    }


    private int maxVal;
    private int curVal;

    private delegate void SetStatusCB();

    private void SetStatus()
    {
        if (pbStatus.InvokeRequired)
        {
            SetStatusCB d = SetStatus;
            pbStatus.Invoke(d);
        }
        else
        {
            pbStatus.Maximum = maxVal;
            pbStatus.Value = curVal;
        }
    }

    private void EnableButtons() => SetTransferEnabled(true);

    private void DisableButtons() => SetTransferEnabled(false);

    /// <summary>
    /// Marshals the transfer button's state onto the UI thread, tolerating a window that has since
    /// been closed.
    /// </summary>
    /// <remarks>
    /// The tolerance is the point. This is called from the transfer thread's <c>finally</c>, and a
    /// user who closes the window while a transfer is running would otherwise take an unhandled
    /// <see cref="ObjectDisposedException"/> on a background thread — which ends the process rather
    /// than the transfer.
    /// </remarks>
    private void SetTransferEnabled(bool enabled)
    {
        try
        {
            if (btnTransfer.IsDisposed)
                return;

            if (btnTransfer.InvokeRequired)
                btnTransfer.Invoke(() => btnTransfer.Enabled = enabled);
            else
                btnTransfer.Enabled = enabled;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // Closed between the check and the marshal. There is no button left to re-enable, and
            // the transfer's own outcome has already been reported.
        }
    }

    private void SshTransfer_Progress(int transferredBytes, int totalBytes)
    {
        maxVal = totalBytes;
        curVal = transferredBytes;

        SetStatus();
    }

    #endregion

    #region Public Methods

    public SSHTransferWindow()
    {
        WindowType = WindowType.SSHTransfer;
        DockPnl = new DockContent();
        InitializeComponent();

        oDlg = new OpenFileDialog
        {
            Filter = @"All Files (*.*)|*.*",
            CheckFileExists = true
        };
    }

    #endregion

    #region Form Stuff

    private void btnBrowse_Click(object sender, EventArgs e)
    {
        if (oDlg.ShowDialog() != DialogResult.OK) return;
        if (oDlg.FileName != "")
        {
            txtLocalFile.Text = oDlg.FileName;
        }
    }

    private void btnTransfer_Click(object sender, EventArgs e)
    {
        if (radProtSCP.Checked)
        {
            StartTransfer(SecureTransfer.SshTransferProtocol.Scp);
        }
        else if (radProtSFTP.Checked)
        {
            StartTransfer(SecureTransfer.SshTransferProtocol.Sftp);
        }
    }

    #endregion
}