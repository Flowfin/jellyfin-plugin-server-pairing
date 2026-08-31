using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.KeyStore;

/// <summary>
/// What a key store file that is there and is not a key store does.
/// </summary>
/// <remarks>
/// Issue #33's rule, in its own words: a corrupt or unreadable store fails the plugin closed,
/// and it does not start with an empty store, because starting empty looks exactly like a
/// fresh install and would let an operator re-pair over the top of a state they have not
/// actually lost.
/// <para>
/// The subject is the difference between two files that a reader cannot tell apart from the
/// outside: one this plugin has never written to, and one it wrote and something then
/// damaged. The first answers with nothing and is the ordinary case; the second is the one
/// that must not.
/// </para>
/// <para>
/// The cases are grouped by what part of the file is wrong rather than by how the damage
/// happened, because the store reads bytes and cannot know the second. What is deliberately
/// NOT here is a store restored from a backup or copied to a second machine: those files are
/// intact key stores and nothing in them is damaged, so no reading of one file tells them
/// apart from the store they are a copy of. That half of #33 stays open and
/// <c>docs/keystore.md</c> says which cases this plugin can and cannot see.
/// </para>
/// </remarks>
public sealed class StoreDamagedTests : IDisposable
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";

    private static readonly DateTimeOffset _at = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    private readonly List<string> _directories = new List<string>();

    /// <summary>
    /// Every file this plugin never wrote, one per shape of damage. Each parses as JSON, so
    /// none of them is caught by the parser failing, and none of them is the shape a store
    /// has.
    /// </summary>
    /// <remarks>
    /// The literal <c>null</c> is in this list on purpose. It is the one shape the store used
    /// to answer as empty deliberately, and under #33's rule it is a file that exists and does
    /// not hold what a store holds, which is the case the rule is about.
    /// </remarks>
    public static TheoryData<string, string> ParsesAndIsNotAStore => new TheoryData<string, string>
    {
        { "the literal null", "null" },
        { "an array", "[]" },
        { "an array of pairings", "[{\"format\":1,\"pairings\":{}}]" },
        { "a number", "17" },
        { "a string", "\"pairings\"" },
        { "a boolean", "true" },
    };

    /// <summary>
    /// Files that do not parse at all, which is what truncation and a partial overwrite
    /// actually look like on disk.
    /// </summary>
    public static TheoryData<string, string> DoesNotParse => new TheoryData<string, string>
    {
        { "an empty file", string.Empty },
        { "whitespace", "   \n" },
        { "a truncated document", "{\"format\":1,\"pairings\":{\"" },
        { "text that is not JSON", "<html>not a key store</html>" },
        { "a NUL byte run", "\0\0\0\0" },
    };

    /// <summary>
    /// Documents that parse as an object and carry the current format, and whose pairings
    /// member is not the object every write of this store puts there.
    /// </summary>
    public static TheoryData<string, string> WrongInsideTheEnvelope => new TheoryData<string, string>
    {
        { "no pairings member", "{\"format\":1}" },
        { "pairings as an array", "{\"format\":1,\"pairings\":[]}" },
        { "pairings as a string", "{\"format\":1,\"pairings\":\"none\"}" },
        { "pairings as null", "{\"format\":1,\"pairings\":null}" },
        { "a pairing that is a number", "{\"format\":1,\"pairings\":{\"p\":5}}" },
        { "a pairing whose current key is a number", "{\"format\":1,\"pairings\":{\"p\":{\"current\":5}}}" },
    };

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var directory in _directories.Where(candidate => Directory.Exists(candidate)))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A file that parses and is not a key store is refused rather than answered as an empty
    /// store. This is the case the issue singles out: an empty answer is what a fresh
    /// installation gives, so an operator meeting one re-pairs over a state that is still on
    /// the disk in front of them.
    /// </summary>
    /// <param name="shape">What is wrong with the file, for the case name.</param>
    /// <param name="bytes">The file's whole content.</param>
    [Theory]
    [MemberData(nameof(ParsesAndIsNotAStore))]
    public void AFileThatParsesAndIsNotAStoreIsRefused(string shape, string bytes)
    {
        Assert.NotEmpty(shape);

        var file = WithContent(bytes);

        var refusal = Assert.Throws<StoreDamagedException>(() => new FilePairingKeyStore(file).Pairings());

        Assert.Equal(file, refusal.File);
    }

    /// <summary>
    /// A file that does not parse is refused with the same answer rather than with whatever
    /// the serialiser happens to throw. An operator reading a log needs one sentence naming
    /// the file and saying that the pairings are not lost, and a parser's own message is
    /// neither.
    /// </summary>
    /// <param name="shape">What is wrong with the file, for the case name.</param>
    /// <param name="bytes">The file's whole content.</param>
    [Theory]
    [MemberData(nameof(DoesNotParse))]
    public void AFileThatDoesNotParseIsRefusedWithTheSameAnswer(string shape, string bytes)
    {
        Assert.NotEmpty(shape);

        var file = WithContent(bytes);

        var refusal = Assert.Throws<StoreDamagedException>(() => new FilePairingKeyStore(file).Pairings());

        Assert.Equal(file, refusal.File);
        Assert.NotNull(refusal.InnerException);
    }

    /// <summary>
    /// A document that carries the envelope and holds something other than pairings inside it
    /// is refused. The envelope parsing is not the whole of the read: a file damaged inside
    /// the member the keys live in reaches the deserialiser rather than the parser.
    /// </summary>
    /// <param name="shape">What is wrong with the file, for the case name.</param>
    /// <param name="bytes">The file's whole content.</param>
    [Theory]
    [MemberData(nameof(WrongInsideTheEnvelope))]
    public void ADocumentWhosePairingsAreNotPairingsIsRefused(string shape, string bytes)
    {
        Assert.NotEmpty(shape);

        var file = WithContent(bytes);

        var refusal = Assert.Throws<StoreDamagedException>(() => new FilePairingKeyStore(file).Pairings());

        Assert.Equal(file, refusal.File);
    }

    /// <summary>
    /// Every operation refuses, not the one that happens to be asked first. A store that
    /// refused a read and accepted a write would put a key into a file it has already said it
    /// cannot read, and the file it wrote would be a store holding one pairing where there
    /// were several.
    /// </summary>
    [Fact]
    public void NoOperationOnADamagedStoreAnswers()
    {
        var store = new FilePairingKeyStore(WithContent("[]"));

        Assert.Throws<StoreDamagedException>(() => store.Pairings());
        Assert.Throws<StoreDamagedException>(() => store.Live(PairingId, _at));
        Assert.Throws<StoreDamagedException>(() => store.Both(PairingId, _at));
        Assert.Throws<StoreDamagedException>(() => store.Add(PairingId, KeyMaterial.Fresh()));
        Assert.Throws<StoreDamagedException>(() => store.Replace(PairingId, KeyMaterial.Fresh(), _at));
        Assert.Throws<StoreDamagedException>(() => store.Destroy(PairingId));
    }

    /// <summary>
    /// The refusal changes nothing on disk. What an operator has to do about a damaged store
    /// depends on what is in it, and a plugin that repaired, truncated or replaced the file on
    /// its way to refusing would have taken that away before anybody read it.
    /// </summary>
    [Fact]
    public void ARefusedStoreIsLeftExactlyAsItWas()
    {
        var file = WithContent("{\"format\":1,\"pairings\":[]}");
        var was = File.ReadAllBytes(file);

        Assert.Throws<StoreDamagedException>(() => new FilePairingKeyStore(file).Destroy(PairingId));

        Assert.Equal(was, File.ReadAllBytes(file));
        Assert.Equal(
            new[] { Path.GetFileName(file) },
            Directory.GetFiles(Path.GetDirectoryName(file)!).Select(Path.GetFileName));
    }

    /// <summary>
    /// A file carrying no format number is format 0, which is migrated the first time it is
    /// read. One whose members are not pairings is refused, and the refusal writes nothing:
    /// neither the store in the current format nor the copy a migration leaves beside it.
    /// </summary>
    /// <remarks>
    /// This is the case the read order exists for. Reading the pairings after the migration
    /// would put a rewritten file and a copy of the original on the disk before anything
    /// noticed the keys were not keys, and the refusal says nothing has changed the file.
    /// </remarks>
    [Fact]
    public void AFormatZeroFileThatHoldsNoPairingsIsRefusedWithNothingWritten()
    {
        var file = WithContent("{\"p\":\"not a pairing\"}");
        var was = File.ReadAllBytes(file);

        Assert.Throws<StoreDamagedException>(() => new FilePairingKeyStore(file).Pairings());

        Assert.Equal(was, File.ReadAllBytes(file));
        Assert.Equal(
            new[] { Path.GetFileName(file) },
            Directory.GetFiles(Path.GetDirectoryName(file)!).Select(Path.GetFileName));
    }

    /// <summary>
    /// The refusal names the file, says the pairings are still in it, and says what not to do.
    /// A message that only says the store could not be read sends an operator to re-pair,
    /// which is the one action that overwrites what they still have.
    /// </summary>
    [Fact]
    public void TheRefusalNamesTheFileAndSaysNotToPairOverIt()
    {
        var file = WithContent("not a key store");

        var refusal = Assert.Throws<StoreDamagedException>(() => new FilePairingKeyStore(file).Pairings());

        Assert.Contains(file, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("damaged", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("aside", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The floor case. A store whose file was never written still answers with nothing, and a
    /// store holding a pairing still answers with it, so the refusal above is about damage and
    /// not about every read.
    /// </summary>
    /// <remarks>
    /// Without this, a change that refused every read would pass every case above. The two
    /// halves are asserted together because the failure worth catching is a refusal that has
    /// swallowed the ordinary path rather than either half on its own.
    /// </remarks>
    [Fact]
    public void AnAbsentFileAndAWrittenStoreBothStillAnswer()
    {
        var file = Path.Join(TemporaryDirectory(), KeyStorePath.FileName);

        var store = new FilePairingKeyStore(file);

        Assert.Empty(store.Pairings());

        store.Add(PairingId, KeyMaterial.Fresh());

        Assert.Equal(new[] { PairingId }, store.Pairings());
        Assert.NotNull(new FilePairingKeyStore(file).Live(PairingId, _at));
    }

    private string WithContent(string bytes)
    {
        var file = Path.Join(TemporaryDirectory(), KeyStorePath.FileName);

        File.WriteAllText(file, bytes);

        return file;
    }

    /// <summary>
    /// A directory the store would accept as its own.
    /// </summary>
    /// <returns>The directory, which exists.</returns>
    /// <remarks>
    /// Made through <see cref="StorePermissions.PrepareDirectory"/> rather than through
    /// <see cref="Directory.CreateDirectory(string)"/>, because on a platform that expresses a
    /// Unix mode the store refuses a directory wider than its own and one made at the process
    /// umask is wider. The cases here need a directory that exists before the store does, to
    /// put the damaged file in, so they make it the way the store would.
    /// </remarks>
    private string TemporaryDirectory()
    {
        var directory = Path.Join(
            Path.GetTempPath(),
            "server-pairing-damaged-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        _directories.Add(directory);

        StorePermissions.PrepareDirectory(directory);

        return directory;
    }
}
