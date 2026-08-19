namespace mRemoteNG.Security;

/// <summary>
/// A provider whose <see cref="ICryptographyProvider.Encrypt"/> and
/// <see cref="ICryptographyProvider.Decrypt"/> may be called concurrently on one instance.
/// </summary>
/// <remarks>
/// <para>
/// Most providers here are not. <see cref="SymmetricEncryption.AeadCryptographyProvider"/> caches
/// derived keys and salts in fields and holds one BouncyCastle cipher instance, so two threads in it
/// at once corrupt each other's results rather than merely racing — which is why
/// <c>XmlConnectionsDecryptor</c> owns the provider it resolves deferred secrets through and takes
/// it under a lock.
/// </para>
/// <para>
/// A provider can only claim this if it derives nothing and keeps no per-call state. Implementing it
/// on something that does would turn a batch decrypt into intermittent wrong answers, which is the
/// hardest possible failure to attribute — so the marker exists to be asked for explicitly rather
/// than assumed from a constructor argument.
/// </para>
/// </remarks>
public interface IThreadSafeCryptographyProvider : ICryptographyProvider
{
}
