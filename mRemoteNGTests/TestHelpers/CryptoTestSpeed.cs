using mRemoteNG.Security;

namespace mRemoteNGTests.TestHelpers;

/// <summary>
/// Runs a provider's key derivation at the product's floor, for tests that need the crypto to work
/// rather than to be slow.
/// </summary>
/// <remarks>
/// <para>
/// The shipped default is 600,000 PBKDF2 iterations, which is the point of it — roughly a quarter
/// of a second per derived key on a current machine, paid by anyone trying passwords. A test fixture
/// that news up a provider in <c>[SetUp]</c> pays it per test, twice over where the test both
/// encrypts and decrypts, and none of those tests are about how long the derivation takes. Measured
/// across the suite it came to some forty seconds of nothing but PBKDF2.
/// </para>
/// <para>
/// 1000 is not an arbitrary small number: it is <c>RecoveryPasswordKeyProtector.MinimumIterations</c>
/// and the floor <c>CryptoProviderFactoryFromXml</c> clamps to, so a provider set here still behaves
/// like one the product would accept. Nothing about the round trip changes — the count is an input
/// to the KDF, not a switch between code paths.
/// </para>
/// <para>
/// Do not use it in a test that is about the iteration count itself. Those set their own value and
/// assert on it, which is the whole reason the property is settable.
/// </para>
/// </remarks>
internal static class CryptoTestSpeed
{
    /// <summary>The lowest count the product accepts anywhere.</summary>
    internal const int Iterations = 1000;

    /// <summary>Returns the same provider, deriving keys at <see cref="Iterations"/>.</summary>
    internal static T AtTestSpeed<T>(this T provider)
        where T : ICryptographyProvider
    {
        provider.KeyDerivationIterations = Iterations;
        return provider;
    }
}
