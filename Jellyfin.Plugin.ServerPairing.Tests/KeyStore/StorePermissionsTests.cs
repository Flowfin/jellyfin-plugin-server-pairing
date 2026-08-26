using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.KeyStore;

/// <summary>
/// When the store's file and directory come into existence, and with what permissions.
/// </summary>
/// <remarks>
/// Two failures are being refused here and they are not the same one. Nothing exists before
/// the first pairing, so a server that has never paired has no file to protect; and what does
/// come into existence is created with its permissions rather than given them afterwards, so
/// there is no instant at which key material is on disk under a wider mode.
/// <para>
/// Every case that needs a Unix mode carries <see cref="UnixModeFactAttribute"/>, which skips
/// it with its reason on a platform that has no such mode rather than letting it pass there. A
/// test that quietly passes on the platform that cannot express the thing it is about is worse
/// than no test: it reports a guard on every run of the suite while only ever having been
/// executed on half of them. The suite runs on a Linux runner in CI, which is where these are
/// actually evaluated.
/// </para>
/// </remarks>
public sealed class StorePermissionsTests : IDisposable
{
    /// <summary>
    /// The one pairing identifier every case here adds, as a field so that the comparison does
    /// not build an array on each call.
    /// </summary>
    private static readonly string[] OnePairing = { "pairing" };

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "server-pairing-permissions-" + Guid.NewGuid().ToString("n", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Removes the temporary directory this test wrote into.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A server that has never paired has no store on disk. Constructing the store, asking it
    /// what it holds and asking it for a key all leave the directory absent, so an operator who
    /// installs this plugin and does nothing with it has no file to protect.
    /// </summary>
    [Fact]
    public void NothingIsCreatedUntilSomethingIsWritten()
    {
        var file = Path.Combine(_root, KeyStorePath.DirectoryName, KeyStorePath.FileName);
        var store = new FilePairingKeyStore(file);

        Assert.Empty(store.Pairings());
        Assert.Null(store.Live("pairing", DateTimeOffset.UnixEpoch));
        Assert.Null(store.Both("pairing", DateTimeOffset.UnixEpoch));
        store.Destroy("pairing");

        Assert.False(Directory.Exists(_root), "The store created its directory before anything was written to it.");
        Assert.False(File.Exists(file), "The store created its file before anything was written to it.");
    }

    /// <summary>
    /// The first write is what brings both into existence, which is the other half of the case
    /// above: lazily created has to mean created, not never created.
    /// </summary>
    [Fact]
    public void TheFirstWriteCreatesBoth()
    {
        var file = Path.Combine(_root, KeyStorePath.DirectoryName, KeyStorePath.FileName);
        var store = new FilePairingKeyStore(file);

        store.Add("pairing", KeyMaterial.From(new byte[32]));

        Assert.True(Directory.Exists(Path.GetDirectoryName(file)));
        Assert.True(File.Exists(file));
        Assert.Equal(OnePairing, store.Pairings());
    }

    /// <summary>
    /// The directory and the file carry the store's modes and nothing wider, read back off the
    /// filesystem after a real write rather than asserted about the constants.
    /// </summary>
    [UnixModeFact]
    [UnsupportedOSPlatform("windows")]
    public void BothAreCreatedWithTheModesTheStoreNames()
    {
        var directory = Path.Combine(_root, KeyStorePath.DirectoryName);
        var file = Path.Combine(directory, KeyStorePath.FileName);

        new FilePairingKeyStore(file).Add("pairing", KeyMaterial.From(new byte[32]));

        Assert.Equal(StorePermissions.DirectoryMode, File.GetUnixFileMode(directory));
        Assert.Equal(StorePermissions.FileMode, File.GetUnixFileMode(file));
    }

    /// <summary>
    /// Neither mode gives anything to a group or to the world. This is the property the two
    /// constants exist for, stated separately from their value so that widening one of them is
    /// refused here rather than silently agreed with by the case above.
    /// </summary>
    [Fact]
    public void NeitherModeReachesPastTheOwner()
    {
        var pastTheOwner = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

        Assert.Equal(UnixFileMode.None, StorePermissions.DirectoryMode & pastTheOwner);
        Assert.Equal(UnixFileMode.None, StorePermissions.FileMode & pastTheOwner);
        Assert.Equal(UnixFileMode.None, StorePermissions.FileMode & UnixFileMode.UserExecute);
    }

    /// <summary>
    /// There is no instant at which the store's bytes sit under a wider mode. The temporary
    /// file the atomic write goes through is what a move puts in place, so it is the file whose
    /// creation mode becomes the store's, and it is read while it still exists rather than
    /// inferred from the destination afterwards.
    /// </summary>
    [UnixModeFact]
    [UnsupportedOSPlatform("windows")]
    public void TheTemporaryIsNarrowBeforeItBecomesTheStore()
    {
        var directory = Path.Combine(_root, KeyStorePath.DirectoryName);
        var file = Path.Combine(directory, KeyStorePath.FileName);
        var seen = UnixFileMode.None;

        var store = new FilePairingKeyStore(
            file,
            (temporary, destination) =>
            {
                seen = File.GetUnixFileMode(temporary);
                File.Move(temporary, destination, overwrite: true);
            });

        store.Add("pairing", KeyMaterial.From(new byte[32]));

        Assert.Equal(StorePermissions.FileMode, seen);
    }

    /// <summary>
    /// A temporary left behind by a process that died does not carry its permissions onto the
    /// store's file. The temporary is what a move puts in place, so a stale one at a wide mode
    /// truncated rather than created would hand that mode to the keys - which is what
    /// truncating does, because a mode is only applied to a file that is actually created.
    /// </summary>
    [UnixModeFact]
    [UnsupportedOSPlatform("windows")]
    public void AWideTemporaryLeftBehindDoesNotCarryItsModeOntoTheStore()
    {
        var directory = Path.Combine(_root, KeyStorePath.DirectoryName);
        var file = Path.Combine(directory, KeyStorePath.FileName);

        StorePermissions.PrepareDirectory(directory);

        var stale = file + AtomicWrite.TemporarySuffix;

        File.WriteAllText(stale, "{}");
        File.SetUnixFileMode(stale, StorePermissions.FileMode | UnixFileMode.OtherRead | UnixFileMode.GroupRead);

        // The setup is asserted before the guard is: a stale temporary that came out narrow
        // would leave this case asserting nothing.
        Assert.NotEqual(UnixFileMode.None, File.GetUnixFileMode(stale) & ~StorePermissions.FileMode);

        new FilePairingKeyStore(file).Add("pairing", KeyMaterial.From(new byte[32]));

        Assert.Equal(StorePermissions.FileMode, File.GetUnixFileMode(file));
    }

    /// <summary>
    /// A directory that is already there with permissions wider than the store would set is
    /// refused, and the refusal names the path so that an operator's next action is obvious.
    /// It is not narrowed: taking a permission away from a directory somebody widened is a
    /// change to their server made without saying so.
    /// </summary>
    [UnixModeFact]
    [UnsupportedOSPlatform("windows")]
    public void APreExistingOverPermissiveDirectoryIsRefusedAndNamed()
    {
        var directory = Path.Combine(_root, KeyStorePath.DirectoryName);
        var file = Path.Combine(directory, KeyStorePath.FileName);

        Directory.CreateDirectory(directory, StorePermissions.DirectoryMode | UnixFileMode.OtherRead);

        // What the directory actually came out as, rather than what was asked for. A umask can
        // take a permission back off, and a case whose setup silently produced a narrow
        // directory would assert a refusal that never had anything to refuse. This fails rather
        // than passing if that happens.
        var asCreated = File.GetUnixFileMode(directory);

        Assert.NotEqual(UnixFileMode.None, asCreated & ~StorePermissions.DirectoryMode);

        var refusal = Assert.Throws<InvalidOperationException>(
            () => new FilePairingKeyStore(file).Add("pairing", KeyMaterial.From(new byte[32])));

        Assert.Contains(directory, refusal.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(file), "The store wrote its file into a directory it had refused.");
        Assert.Equal(asCreated, File.GetUnixFileMode(directory));
    }

    /// <summary>
    /// The near miss the case above needs to be read as a refusal rather than as a store that
    /// refuses every directory it finds. A directory already there at exactly the store's own
    /// mode is written into.
    /// </summary>
    [UnixModeFact]
    [UnsupportedOSPlatform("windows")]
    public void APreExistingDirectoryAtTheStoreSOwnModeIsAccepted()
    {
        var directory = Path.Combine(_root, KeyStorePath.DirectoryName);
        var file = Path.Combine(directory, KeyStorePath.FileName);

        Directory.CreateDirectory(directory, StorePermissions.DirectoryMode);

        new FilePairingKeyStore(file).Add("pairing", KeyMaterial.From(new byte[32]));

        Assert.True(File.Exists(file));
        Assert.Equal(StorePermissions.FileMode, File.GetUnixFileMode(file));
    }

    /// <summary>
    /// Every permission a directory can carry beyond the store's own is refused, one at a time,
    /// rather than only the one the case above happens to set. A guard that catches
    /// world-readable and lets group-writable through is the shape this walks.
    /// </summary>
    [UnixModeFact]
    [UnsupportedOSPlatform("windows")]
    public void EveryPermissionPastTheStoreSOwnIsRefusedOnItsOwn()
    {
        var extras = Enum.GetValues<UnixFileMode>()
            .Where(mode => mode != UnixFileMode.None)
            .Where(mode => (mode & StorePermissions.DirectoryMode) == UnixFileMode.None)
            .ToArray();

        Assert.NotEmpty(extras);

        foreach (var extra in extras)
        {
            var directory = Path.Combine(_root, extra.ToString());
            var file = Path.Combine(directory, KeyStorePath.FileName);

            Directory.CreateDirectory(directory, StorePermissions.DirectoryMode | extra);

            // A mode the platform declined to set is not a case about this guard. Sticky and
            // set-user-id are not honoured everywhere, and a directory that came back at the
            // store's own mode carries nothing for the guard to refuse.
            if (File.GetUnixFileMode(directory) == StorePermissions.DirectoryMode)
            {
                continue;
            }

            Assert.Throws<InvalidOperationException>(
                () => new FilePairingKeyStore(file).Add("pairing", KeyMaterial.From(new byte[32])));
        }
    }
}
