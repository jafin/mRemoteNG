using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ExternalConnectors;

public static class PuttyKeyFileGenerator
{
    private const int PrefixSize = 4;
    private const int PaddedPrefixSize = PrefixSize + 1;
    private const int LineLength = 64;
    private const string KeyType = "ssh-rsa";
    private const string EncryptionType = "none";

    public static string ToPuttyPrivateKey(RSACryptoServiceProvider cryptoServiceProvider, string comment = "imported-openssh-key")
    {
        var publicParameters = cryptoServiceProvider.ExportParameters(false);
        byte[] publicBuffer = new byte[3 + KeyType.Length + GetPrefixSize(publicParameters.Exponent) + publicParameters.Exponent!.Length + GetPrefixSize(publicParameters.Modulus) + publicParameters.Modulus!.Length + 1];

        using (var bw = new BinaryWriter(new MemoryStream(publicBuffer)))
        {
            bw.Write(new byte[] { 0x00, 0x00, 0x00 });
            bw.Write(Encoding.ASCII.GetBytes(KeyType));
            PutPrefixed(bw, publicParameters.Exponent, CheckIsNeddPadding(publicParameters.Exponent));
            PutPrefixed(bw, publicParameters.Modulus, CheckIsNeddPadding(publicParameters.Modulus));
        }
        var publicBlob = Convert.ToBase64String(publicBuffer);

        var privateParameters = cryptoServiceProvider.ExportParameters(true);

        byte[] privateBuffer = new byte[PaddedPrefixSize + privateParameters.D!.Length + PaddedPrefixSize + privateParameters.P!.Length + PaddedPrefixSize + privateParameters.Q!.Length + PaddedPrefixSize + privateParameters.InverseQ!.Length];

        using (var bw = new BinaryWriter(new MemoryStream(privateBuffer)))
        {
            PutPrefixed(bw, privateParameters.D, true);
            PutPrefixed(bw, privateParameters.P, true);
            PutPrefixed(bw, privateParameters.Q, true);
            PutPrefixed(bw, privateParameters.InverseQ, true);
        }
        var privateBlob = Convert.ToBase64String(privateBuffer);

        HMACSHA1 hmacSha1 = new(SHA1.HashData(Encoding.ASCII.GetBytes("putty-private-key-file-mac-key")));
        byte[] bytesToHash = new byte[PrefixSize + KeyType.Length + PrefixSize + EncryptionType.Length + PrefixSize + comment.Length + PrefixSize + publicBuffer.Length + PrefixSize + privateBuffer.Length];

        using (var bw = new BinaryWriter(new MemoryStream(bytesToHash)))
        {
            PutPrefixed(bw, Encoding.ASCII.GetBytes(KeyType));
            PutPrefixed(bw, Encoding.ASCII.GetBytes(EncryptionType));
            PutPrefixed(bw, Encoding.ASCII.GetBytes(comment));
            PutPrefixed(bw, publicBuffer);
            PutPrefixed(bw, privateBuffer);
        }

        var hash = string.Join("", hmacSha1.ComputeHash(bytesToHash).Select(x => $"{x:x2}"));

        var sb = new StringBuilder();
        sb.AppendLine("PuTTY-User-Key-File-2: " + KeyType);
        sb.AppendLine("Encryption: " + EncryptionType);
        sb.AppendLine("Comment: " + comment);

        var publicLines = SpliceText(publicBlob, LineLength);
        sb.AppendLine("Public-Lines: " + publicLines.Length);
        foreach (var line in publicLines)
        {
            sb.AppendLine(line);
        }

        var privateLines = SpliceText(privateBlob, LineLength);
        sb.AppendLine("Private-Lines: " + privateLines.Length);
        foreach (var line in privateLines)
        {
            sb.AppendLine(line);
        }

        sb.AppendLine("Private-MAC: " + hash);

        return sb.ToString();
    }

    private static void PutPrefixed(BinaryWriter bw, byte[] bytes, bool addLeadingNull = false)
    {
        bw.Write(BitConverter.GetBytes(bytes.Length + (addLeadingNull ? 1 : 0)).Reverse().ToArray());
        if (addLeadingNull)
            bw.Write(new byte[] { 0x00 });
        bw.Write(bytes);
    }

    private static string[] SpliceText(string text, int lineLength)
    {
        return Regex.Matches(text, ".{1," + lineLength + "}", RegexOptions.None, TimeSpan.FromSeconds(5)).Select(m => m.Value).ToArray();
    }

    private static int GetPrefixSize(byte[]? bytes)
    {
        if (bytes is null)
            return 0;

        return CheckIsNeddPadding(bytes) ? PaddedPrefixSize : PrefixSize;
    }

    private static bool CheckIsNeddPadding(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return false;

        // 128 == 10000000
        // This means that the number of bits can be divided by 8.
        // According to the algorithm in putty, you need to add a padding.
        return bytes[0] >= 128;
    }
}
