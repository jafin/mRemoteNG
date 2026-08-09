using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace ExternalConnectors.CPS;

public static class PasswordstateInterface
{
    private static class CPSConnectionData
    {
        public static string SsPassword = "";
        public static string SsUrl = "";
        public static string SsOtp = "";
        private static DateTime ssOtpTimeStampExpiration;

        public static bool SsSso;
        private static bool initDone;

        //token 
        //public static string ssTokenBearer = "";
        //public static DateTime ssTokenExpiresOn = DateTime.UtcNow;
        //public static string ssTokenRefresh = "";

        public static void Init()
        {
            // 2024-05-04 passwordstate currently does not support auth tokens, so we need to re-enter otp codes frequently
            if (!string.IsNullOrEmpty(SsOtp) && DateTime.Now > ssOtpTimeStampExpiration)
            {
                SsOtp = "";
                initDone = false;
            }

            if (initDone)
                return;

            var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\mRemoteCPSInterface");
            try
            {
                // display gui and ask for data
                var f = new CPSConnectionForm();
                f.tbAPIKey.Text = SsPassword; // in OTP refresh cases, this value might already be filled

                var url = key.GetValue("URL") as string;
                if (url == null || !url.Contains("://", StringComparison.OrdinalIgnoreCase))
                    url = "https://cred.domain.local/SecretServer";
                f.tbServerURL.Text = url;

                var b = key.GetValue("SSO");
                if (b == null || !string.Equals((string)b, "True", StringComparison.Ordinal))
                    SsSso = false;
                else
                    SsSso = true;
                f.cbUseSSO.Checked = SsSso;

                // show dialog
                while (true)
                {
                    _ = f.ShowDialog();

                    if (f.DialogResult != DialogResult.OK)
                        return;

                    // store values to memory
                    //ssUsername = f.tbUsername.Text;
                    SsPassword = f.tbAPIKey.Text;
                    SsUrl = f.tbServerURL.Text;
                    SsSso = f.cbUseSSO.Checked;

                    // Require HTTPS for vault connections
                    if (!SsUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show("Passwordstate server URL must use HTTPS for secure communication.",
                            "Security Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }

                    SsOtp = f.tbOTP.Text;
                    ssOtpTimeStampExpiration = DateTime.Now.AddSeconds(30);
                    // check connection first
                    try
                    {
                        if (TestCredentials())
                        {
                            initDone = true;
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        MessageBox.Show("Test Credentials failed - please check your credentials");
                    }
                }

                // write values to registry
                key.SetValue("URL", SsUrl);
                key.SetValue("SSO", SsSso);
            }
            finally
            {
                key.Close();
            }
        }
    }

    private static bool TestCredentials()
    {
        return ConnectionTest();
    }

    private static bool ConnectionTest()
    {
        if (CPSConnectionData.SsSso)
        {
            var url = $"{CPSConnectionData.SsUrl}/winapi/passwordlists/";

            using var client = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "mRemote");
            client.DefaultRequestHeaders.Add("OTP", CPSConnectionData.SsOtp);

            var json = Task.Run(() => client.GetStringAsync(url)).GetAwaiter().GetResult();
            var data = JsonSerializer.Deserialize<JsonNode>(json);
            if (data == null)
                return false;
            return true;
        }
        else
        {
            var url = $"{CPSConnectionData.SsUrl}/api/passwordlists/";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "mRemote");
            client.DefaultRequestHeaders.Add("APIKey", CPSConnectionData.SsPassword);
            client.DefaultRequestHeaders.Add("OTP", CPSConnectionData.SsOtp);

            var json = Task.Run(() => client.GetStringAsync(url)).GetAwaiter().GetResult();
            var data = JsonSerializer.Deserialize<JsonNode>(json);
            if (data == null)
                return false;
            return true;
        }
    }

    private static JsonNode? FetchDataWinAuth(int secretId)
    {
        var url = $"{CPSConnectionData.SsUrl}/winapi/passwords/{secretId}";

        using var client = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Add("User-Agent", "mRemote");
        client.DefaultRequestHeaders.Add("OTP", CPSConnectionData.SsOtp);

        var json = Task.Run(() => client.GetStringAsync(url)).GetAwaiter().GetResult();
        var data = JsonSerializer.Deserialize<JsonNode>(json);
        if (data == null)
            return null;
        var element = data[0];
        return element;
    }

    private static JsonNode? FetchDataApiKeyAuth(int secretId)
    {
        var url = $"{CPSConnectionData.SsUrl}/api/passwords/{secretId}";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Add("User-Agent", "mRemote");
        client.DefaultRequestHeaders.Add("APIKey", CPSConnectionData.SsPassword);
        client.DefaultRequestHeaders.Add("OTP", CPSConnectionData.SsOtp);

        var json = Task.Run(() => client.GetStringAsync(url)).GetAwaiter().GetResult();
        var data = JsonSerializer.Deserialize<JsonNode>(json);
        if (data == null)
            return null;
        var element = data[0];
        return element;
    }

    private static void FetchSecret(int secretId, out string secretUsername, out string secretPassword,
        out string secretDomain, out string privatekey)
    {
        // clear return variables
        secretDomain = "";
        secretUsername = "";
        secretPassword = "";
        privatekey = "";
        var privateKeyPassPhrase = "";
        JsonNode? element;

        if (CPSConnectionData.SsSso)
            element = FetchDataWinAuth(secretId);
        else
            element = FetchDataApiKeyAuth(secretId);

        if (element == null)
            return;

        var dom = element["Domain"];
        if (dom != null) secretDomain = dom.ToString();

        var user = element["UserName"];
        if (user != null) secretUsername = user.ToString();

        var pw = element["Password"];
        if (pw != null) secretPassword = pw.ToString();

        var privkey = element["GenericField1"];
        if (privkey != null) privatekey = privkey.ToString();

        var phrase = element["GenericField3"];
        if (phrase != null) privateKeyPassPhrase = phrase.ToString();

        // need to decode the private key?
        if (!string.IsNullOrEmpty(privateKeyPassPhrase))
        {
            try
            {
                var key = DecodePrivateKey(privatekey, privateKeyPassPhrase);
                privatekey = key;
            }
            catch (Exception ex)
            {
                _ = ex; // Intentionally suppressed
            }
        }

        // conversion to putty format necessary?
        if (!string.IsNullOrEmpty(privatekey) &&
            !privatekey.StartsWith("PuTTY-User-Key-File-2", StringComparison.Ordinal))
        {
            try
            {
                var key = ImportPrivateKey(privatekey);
                privatekey = PuttyKeyFileGenerator.ToPuttyPrivateKey(key);
            }
            catch (Exception ex)
            {
                _ = ex; // Intentionally suppressed
            }
        }
    }

    #region PUTTY KEY HANDLING

    // decode rsa private key with encryption password
    private static string DecodePrivateKey(string encryptedPrivateKey, string password)
    {
        TextReader textReader = new StringReader(encryptedPrivateKey);
        var pemReader = new PemReader(textReader, new PasswordFinder(password));

        var keyPair = (AsymmetricCipherKeyPair)pemReader.ReadObject();

        TextWriter textWriter = new StringWriter();
        var pemWriter = new PemWriter(textWriter);
        pemWriter.WriteObject(keyPair.Private);
        pemWriter.Writer.Flush();

        return "" + textWriter;
    }

    private sealed class PasswordFinder(string password) : IPasswordFinder
    {
        public char[] GetPassword()
        {
            return password.ToCharArray();
        }
    }

    // read private key pem string to rsacryptoserviceprovider
    public static RSACryptoServiceProvider ImportPrivateKey(string pem)
    {
        var pr = new PemReader(new StringReader(pem));
        var keyPair = (AsymmetricCipherKeyPair)pr.ReadObject();
        var rsaParams = DotNetUtilities.ToRSAParameters((RsaPrivateCrtKeyParameters)keyPair.Private);
        var rsa = new RSACryptoServiceProvider();
        rsa.ImportParameters(rsaParams);
        return rsa;
    }

    #endregion


    // input: must be the secret id to fetch
    public static void FetchSecretFromServer(string secretId, out string username, out string password,
        out string domain, out string privatekey)
    {
        // get secret id
        var sid = Int32.Parse(secretId);

        // init connection credentials, display popup if necessary
        CPSConnectionData.Init();

        // get the secret
        FetchSecret(sid, out username, out password, out domain, out privatekey);
    }
}