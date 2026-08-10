using System;
using System.Security.Cryptography;
using System.Text;


namespace mRemoteNG.Security.KeyDerivation;

public class Pkcs5S2KeyGenerator : IKeyDerivationFunction
{
    private readonly int _iterations;
    private readonly int _keyBitSize;
    private readonly HashAlgorithmName _pseudoRandomFunction;

    /// <param name="iterations">
    /// Required. A defaulted iteration count is indistinguishable at the call site from a chosen
    /// one, and the value this class used to default to was three orders of magnitude below the
    /// configured one — so a <c>new()</c> that merely forgot an argument produced a weak key.
    /// </param>
    /// <param name="pseudoRandomFunction">
    /// The PBKDF2 PRF. SHA-1 is the historical value and what every connection file written before
    /// the format recorded it used, so it stays the default: a caller that does not think about this
    /// gets a file every other build can still read.
    /// </param>
    public Pkcs5S2KeyGenerator(int keyBitSize, int iterations, HashAlgorithmName? pseudoRandomFunction = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1000);
        ArgumentOutOfRangeException.ThrowIfNegative(keyBitSize);

        HashAlgorithmName function = pseudoRandomFunction ?? KeyDerivationPrf.Default;
        if (!KeyDerivationPrf.IsSupported(function))
            throw new ArgumentOutOfRangeException(nameof(pseudoRandomFunction), function,
                "Deriving with a function this build cannot record would produce a file it could not read back.");

        _keyBitSize = keyBitSize;
        _iterations = iterations;
        _pseudoRandomFunction = function;
    }

    public byte[] DeriveKey(string password, byte[] salt)
    {
        int keyLengthBytes = _keyBitSize / 8;
        if (keyLengthBytes == 0) return [];

        byte[] passwordInBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            // Use .NET native PBKDF2 (CNG-accelerated) instead of BouncyCastle's managed
            // Pkcs5S2ParametersGenerator. Output is identical (RFC 2898) but ~5x faster at high
            // iteration counts.
            return Rfc2898DeriveBytes.Pbkdf2(passwordInBytes, salt, _iterations, _pseudoRandomFunction, keyLengthBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordInBytes);
        }
    }
}
