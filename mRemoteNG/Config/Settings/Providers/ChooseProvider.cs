using System.Configuration;

namespace mRemoteNG.Config.Settings.Providers;

/// <summary>
/// The provider named by <c>SettingsProviderAttribute</c> on the generated settings classes.
/// </summary>
/// <remarks>
/// <para>
/// This used to <b>inherit a different base class per build</b> — <c>PortableSettingsProvider</c>
/// when <c>PORTABLE</c> was defined, <see cref="LocalFileSettingsProvider"/> otherwise — which is why
/// the edition had to be a compile constant. A base class cannot be chosen at runtime, so nothing
/// about portability could be either.
/// </para>
/// <para>
/// The choice now happens where it can be made at runtime:
/// <see cref="PortableSettingsInitializer"/> constructs the right provider and wires it to every
/// settings class before any of them is read. This type stays because the attribute names it, and
/// because the attribute is the fallback if the initializer is ever not reached first — in which
/// case the installed behaviour is the safer of the two, writing under the user's profile rather
/// than beside an executable that may sit in Program Files.
/// </para>
/// </remarks>
public class ChooseProvider : LocalFileSettingsProvider
{
}
