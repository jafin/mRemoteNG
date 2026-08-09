using System.Runtime.Versioning;
using mRemoteNG.Security.SymmetricEncryption;

namespace mRemoteNG.Security.Factories;

[SupportedOSPlatform("windows")]
public class LegacyInsecureCryptoProviderFactory : ICryptoProviderFactory
{
    public ICryptographyProvider Build()
    {
        return new LegacyRijndaelCryptographyProvider();
    }
}