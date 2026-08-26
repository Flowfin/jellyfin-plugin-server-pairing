using Jellyfin.Plugin.ServerPairing.KeyStore;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.KeyStore;

/// <summary>
/// A case that can only be evaluated where a Unix file mode exists, skipped with its reason
/// anywhere else rather than passed.
/// </summary>
/// <remarks>
/// A test about a permission cannot assert anything on a platform that has no such permission,
/// and the two ways of handling that are not equally honest. Returning early leaves a green
/// case in the report on every run, which reads as a guard that was evaluated and was not; this
/// leaves a skipped case carrying the sentence saying why. The suite is run on both platforms -
/// a Linux runner in CI and whatever a person develops on - so the difference is visible rather
/// than theoretical.
/// <para>
/// The skip is decided when the attribute is constructed, which is discovery time, so the
/// runner reports the case as skipped with this reason rather than running it and being told
/// afterwards. That is what xunit's version 2 offers; a dynamic skip inside the body is version
/// 3's, and reaching for it here would mean a package this tree does not carry.
/// </para>
/// </remarks>
internal sealed class UnixModeFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnixModeFactAttribute"/> class.
    /// </summary>
    public UnixModeFactAttribute()
    {
        if (!StorePermissions.PlatformExpressesThem)
        {
            Skip = "This platform cannot express a Unix file mode, so the permissions the key store "
                + "creates its directory and file with cannot be read back and this case is not "
                + "evaluated here. It is evaluated on the Linux runner the suite runs on in CI. "
                + "Nothing about this run says the store's permissions are right.";
        }
    }
}
