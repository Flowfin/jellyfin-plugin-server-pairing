using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Mapping;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Jellyfin.Plugin.ServerPairing.Tests.Mapping;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// The act that stops a pairing here: one call over two stores, with no peer involved.
/// </summary>
/// <remarks>
/// This is the first done condition of issue #24, which the cases in
/// <see cref="RevocationTests"/> could only stipulate. They destroy a key by hand and assert
/// what a peer meets afterwards; what was missing was anything that performs the revocation,
/// because <see cref="PairingStateMachine"/> had no shipped record store to write a
/// <c>Revoked</c> record through until <see cref="FilePairingRecordStore"/> landed.
/// <para>
/// Both stores here are the real ones over real files in a directory of their own, and the
/// assertions are made through second instances built over the same paths, so what is proved
/// survives the object rather than living in one.
/// </para>
/// <para>
/// THE PEER IS UNREACHABLE IN THE STRONGEST SENSE AVAILABLE. It is not a send that fails; there
/// is no send. <see cref="TheRevocationIsBuiltFromStoresAndNothingThatCouldReachAPeer"/> is
/// what holds that shape in place, because a channel added to the operation later would make a
/// revocation depend on reaching the peer without any case here noticing.
/// </para>
/// </remarks>
public sealed class AdministrativeRevocationTests : IDisposable
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string Administrator = "administrator";

    private static readonly DateTimeOffset At = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    private readonly List<string> _directories = new List<string>();

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var directory in _directories.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A revocation completes with nothing reached and nothing answered, and afterwards the key
    /// is gone from the store and the pairing is recorded as revoked.
    /// </summary>
    /// <remarks>
    /// The first done condition of issue #24. Both stores are re-opened over the same files
    /// before anything is asserted, so a key removed in memory and left on disk, or a record
    /// written to an object and not to a file, fails here.
    /// </remarks>
    [Fact]
    public void ARevocationCompletesWithNoPeerAndTheKeyIsGone()
    {
        var directory = FreshDirectory();
        var keyFile = Path.Join(directory, KeyStorePath.FileName);
        var recordFile = Path.Join(directory, RecordStorePath.FileName);

        new FilePairingKeyStore(keyFile).Add(PairingId, KeyMaterial.Fresh());
        Pair(recordFile);

        var outcome = Revocation(keyFile, recordFile).Revoke(PairingId, Administrator, At);

        Assert.Equal(RevocationOutcome.Revoked, outcome);
        Assert.Null(new FilePairingKeyStore(keyFile).Both(PairingId, At));
        Assert.DoesNotContain(PairingId, new FilePairingKeyStore(keyFile).Pairings());
        Assert.Equal(PairingState.Revoked, Machine(recordFile).StateOf(PairingId));
    }

    /// <summary>
    /// The revoked pairing's record says who decided it and when, and the record is kept rather
    /// than deleted.
    /// </summary>
    /// <remarks>
    /// <c>Revoked</c> keeps its record on purpose, so that a later request naming that
    /// identifier is refused as revoked rather than treated as new. A revocation that reached
    /// <c>Absent</c> instead would pass the state assertion above on a machine that had simply
    /// forgotten the pairing.
    /// </remarks>
    [Fact]
    public void TheRevokedPairingKeepsARecordNamingWhoDecidedIt()
    {
        var directory = FreshDirectory();
        var keyFile = Path.Join(directory, KeyStorePath.FileName);
        var recordFile = Path.Join(directory, RecordStorePath.FileName);

        new FilePairingKeyStore(keyFile).Add(PairingId, KeyMaterial.Fresh());
        Pair(recordFile);

        Revocation(keyFile, recordFile).Revoke(PairingId, Administrator, At);

        var record = new FilePairingRecordStore(recordFile).Read(PairingId);

        Assert.NotNull(record);
        Assert.Equal(PairingState.Revoked, record.State);
        Assert.Equal(Administrator, record.Actor);
        Assert.Equal(At, record.At);
    }

    /// <summary>
    /// The key is destroyed even where the record store says there is nothing to revoke.
    /// </summary>
    /// <remarks>
    /// The two stores are separate files and can disagree - one restored from a backup, one
    /// replaced by hand - so a record saying <see cref="PairingState.Absent"/> is not evidence
    /// that no key is held. A revocation that read the record first and returned early would
    /// leave a live key on a server whose operator has been told the link is stopped, which is
    /// the direction this whole issue is against.
    /// </remarks>
    [Fact]
    public void TheKeyGoesEvenWhereTheRecordSaysThereIsNothingToRevoke()
    {
        var directory = FreshDirectory();
        var keyFile = Path.Join(directory, KeyStorePath.FileName);
        var recordFile = Path.Join(directory, RecordStorePath.FileName);

        new FilePairingKeyStore(keyFile).Add(PairingId, KeyMaterial.Fresh());

        var outcome = Revocation(keyFile, recordFile).Revoke(PairingId, Administrator, At);

        Assert.Equal(RevocationOutcome.NothingToRevoke, outcome);
        Assert.Null(new FilePairingKeyStore(keyFile).Both(PairingId, At));
    }

    /// <summary>
    /// A record that cannot be written does not put the key back, so the failure that is left
    /// is a stale record rather than a live key.
    /// </summary>
    /// <remarks>
    /// This is the ordering the operation is designed around, asserted rather than described.
    /// Writing the record first and destroying afterwards would leave the opposite residual: a
    /// server reporting the pairing revoked and still authenticating the peer with the key it
    /// holds.
    /// </remarks>
    [Fact]
    public void AFailureRecordingTheTransitionLeavesNoKeyBehind()
    {
        var directory = FreshDirectory();
        var keyFile = Path.Join(directory, KeyStorePath.FileName);
        var keys = new FilePairingKeyStore(keyFile);

        keys.Add(PairingId, KeyMaterial.Fresh());

        var records = new RefusingRecords();
        var revocation = new Revocation(keys, new PairingStateMachine(records, new InMemoryUserMappings()));

        Assert.Throws<IOException>(() => revocation.Revoke(PairingId, Administrator, At));
        Assert.Null(new FilePairingKeyStore(keyFile).Both(PairingId, At));
    }

    /// <summary>
    /// Revoking twice is the same as revoking once, and the second one says so.
    /// </summary>
    /// <remarks>
    /// <c>Revoked</c> is terminal, so a second revocation records nothing. It still sweeps the
    /// key store, which is the case above; what this asserts is that the caller is told the
    /// difference rather than being told a link was stopped twice.
    /// </remarks>
    [Fact]
    public void RevokingASecondTimeRecordsNothingAndSaysSo()
    {
        var directory = FreshDirectory();
        var keyFile = Path.Join(directory, KeyStorePath.FileName);
        var recordFile = Path.Join(directory, RecordStorePath.FileName);

        new FilePairingKeyStore(keyFile).Add(PairingId, KeyMaterial.Fresh());
        Pair(recordFile);

        var revocation = Revocation(keyFile, recordFile);

        Assert.Equal(RevocationOutcome.Revoked, revocation.Revoke(PairingId, Administrator, At));
        Assert.Equal(RevocationOutcome.NothingToRevoke, revocation.Revoke(PairingId, Administrator, At));
        Assert.Equal(PairingState.Revoked, Machine(recordFile).StateOf(PairingId));
    }

    /// <summary>
    /// The operation is constructed from the two stores and from nothing that could reach a
    /// peer.
    /// </summary>
    /// <remarks>
    /// Every case above passes on a machine with no network, which proves that a revocation does
    /// not need the peer only for as long as the operation has no way of asking. A channel added
    /// to the constructor later would not redden any of them, and a revocation that waits for a
    /// peer that is hostile or gone is the failure issue #24 opens with. So the shape is asserted
    /// directly: two parameters, both stores.
    /// </remarks>
    [Fact]
    public void TheRevocationIsBuiltFromStoresAndNothingThatCouldReachAPeer()
    {
        var parameters = typeof(Revocation)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Equal(new[] { typeof(IPairingKeyStore), typeof(PairingStateMachine) }, parameters);
    }

    /// <summary>
    /// A record store holding one active pairing that refuses every write, standing in for a
    /// disk that has stopped accepting them.
    /// </summary>
    /// <remarks>
    /// It has to answer the read as well as refuse the write. A store answering nothing puts the
    /// pairing in <see cref="PairingState.Absent"/>, where a revocation records nothing and
    /// reaches no write at all, so the case would pass without the refusal ever being met.
    /// </remarks>
    private sealed class RefusingRecords : IPairingRecordStore
    {
        /// <inheritdoc />
        public IReadOnlyList<string> Pairings() => Array.Empty<string>();

        /// <inheritdoc />
        public PairingRecord? Read(string pairingId)
            => new PairingRecord(pairingId, PairingState.Active, PairingState.ConfirmedHere, "Confirm", "peer", At, "https://peer.example");

        /// <inheritdoc />
        public void Write(PairingRecord record) => throw new IOException("the record store is not writable");

        /// <inheritdoc />
        public void Delete(string pairingId) => throw new IOException("the record store is not writable");
    }

    /// <summary>
    /// A directory of its own, removed when the class is disposed.
    /// </summary>
    /// <returns>The directory.</returns>
    private string FreshDirectory()
    {
        var directory = Path.Join(Path.GetTempPath(), "server-pairing-tests-" + Guid.NewGuid().ToString("n"));

        _directories.Add(directory);

        return directory;
    }

    /// <summary>
    /// Walks a pairing from nothing to <see cref="PairingState.Active"/> through the machine, so
    /// that what is revoked below is a live pairing rather than a half-built one.
    /// </summary>
    /// <param name="recordFile">The record store's file.</param>
    private static void Pair(string recordFile)
    {
        var machine = Machine(recordFile);

        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At);
        machine.Receive(PairingId, PairingMessage.Hello, OfferedKey.NotApplicable, "peer", At);
        machine.Apply(PairingId, LocalEvent.FingerprintConfirmed, Administrator, At);
        machine.Receive(PairingId, PairingMessage.Confirm, OfferedKey.NotApplicable, "peer", At);
    }

    /// <summary>
    /// A state machine over the record file, with a mapping store that holds nothing.
    /// </summary>
    /// <param name="recordFile">The record store's file.</param>
    /// <returns>The state machine.</returns>
    private static PairingStateMachine Machine(string recordFile)
        => new PairingStateMachine(new FilePairingRecordStore(recordFile), new InMemoryUserMappings());

    /// <summary>
    /// A revocation over both files, built fresh so that nothing it does is explained by an
    /// object a previous call left behind.
    /// </summary>
    /// <param name="keyFile">The key store's file.</param>
    /// <param name="recordFile">The record store's file.</param>
    /// <returns>The operation.</returns>
    private static Revocation Revocation(string keyFile, string recordFile)
        => new Revocation(new FilePairingKeyStore(keyFile), Machine(recordFile));
}
