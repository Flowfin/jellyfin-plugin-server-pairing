using System;
using System.Globalization;
using System.Text;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// A body becoming fields, and every way the member table says one does not.
/// </summary>
/// <remarks>
/// Every expectation here is read out of the member table in <c>docs/protocol.md</c> rather than
/// out of the reader. That document says a body that is not empty is a single JSON object with
/// no nesting, that every member it names is required, that a member it does not name is refused
/// rather than ignored, that a member carrying <c>null</c> and a member appearing twice are
/// refused the same way, and that empty means zero bytes.
/// <para>
/// The shape of this file is one accepted body per message and a mutation of it per rule, which
/// is what makes each refusal about the rule it names: the unmutated body is asserted to read in
/// the same case, so a reader that refused everything would fail rather than pass.
/// </para>
/// </remarks>
public class ArrivingBodyTests
{
    /// <summary>
    /// A public key member of the length <c>docs/crypto.md</c> measured for a P-256
    /// <c>SubjectPublicKeyInfo</c>. Nothing here imports it: what the member table fixes is
    /// base64 inside a length limit.
    /// </summary>
    private static readonly string Key = Convert.ToBase64String(new byte[91]);

    /// <summary>
    /// A fingerprint digest of the length <c>docs/crypto.md</c> fixes, in the alphabet the field
    /// table fixes.
    /// </summary>
    private static readonly string Digest = new string('a', ConfirmRequestBody.DigestLength);

    /// <summary>
    /// The bodies that carry no member, so a case walks the two rather than naming one.
    /// </summary>
    /// <returns>The messages whose table row says empty.</returns>
    public static TheoryData<PairingMessage> EmptyBodied()
    {
        var data = new TheoryData<PairingMessage>();

        data.Add(PairingMessage.Revoke);
        data.Add(PairingMessage.Unpair);

        return data;
    }

    /// <summary>
    /// The bodies no reader on this plane judges, so a case walks the two rather than naming
    /// one.
    /// </summary>
    /// <returns>The messages whose body is not read here.</returns>
    public static TheoryData<PairingMessage> Unread()
    {
        var data = new TheoryData<PairingMessage>();

        data.Add(PairingMessage.Rotate);
        data.Add(PairingMessage.Exchange);

        return data;
    }

    /// <summary>
    /// The four members a <c>hello</c> carries, read back as the values that were written. This
    /// is the floor under every refusal below: without it they would all be satisfied by a
    /// reader that refused everything.
    /// </summary>
    [Fact]
    public void AHelloBecomesItsFourMembers()
    {
        var read = ArrivingBody.Read(PairingMessage.Hello, Hello());

        Assert.Equal(BodyOutcome.Read, read.Outcome);
        Assert.NotNull(read.Hello);
        Assert.Equal(Key, read.Hello!.Key);
        Assert.Equal("https://peer.example.org", read.Hello.Address);
        Assert.Equal(SupportedVersions.Lowest, read.Hello.Versions.Low);
        Assert.Equal(SupportedVersions.Highest, read.Hello.Versions.High);
    }

    /// <summary>
    /// The one member a <c>confirm</c> carries, read back as the value that was written.
    /// </summary>
    [Fact]
    public void AConfirmBecomesItsOneMember()
    {
        var read = ArrivingBody.Read(PairingMessage.Confirm, Confirm(Digest));

        Assert.Equal(BodyOutcome.Read, read.Outcome);
        Assert.NotNull(read.Confirm);
        Assert.Equal(Digest, read.Confirm!.Digest);
    }

    /// <summary>
    /// A member the table does not name is refused rather than ignored. Ignoring one is what
    /// turns it into an undocumented extension: two implementations begin relying on it and the
    /// version that was supposed to announce it never moved.
    /// </summary>
    [Fact]
    public void AMemberTheTableDoesNotNameIsRefused()
    {
        var body = Bytes(
            "{\"" + HelloRequestBody.KeyMember + "\":\"" + Key
            + "\",\"" + HelloRequestBody.VersionLowMember + "\":" + Low
            + ",\"" + HelloRequestBody.VersionHighMember + "\":" + High
            + ",\"" + HelloRequestBody.AddressMember + "\":\"https://peer.example.org\""
            + ",\"extra\":\"whatever\"}");

        Assert.Equal(BodyOutcome.DidNotParse, ArrivingBody.Read(PairingMessage.Hello, body).Outcome);
        Assert.Equal(BodyOutcome.Read, ArrivingBody.Read(PairingMessage.Hello, Hello()).Outcome);
    }

    /// <summary>
    /// Every member the table names is required, one at a time. A body missing one is refused
    /// rather than completed, because a default is a value neither side agreed on standing in
    /// for one they would have had to send.
    /// </summary>
    /// <param name="missing">The member left out.</param>
    [Theory]
    [InlineData(HelloRequestBody.KeyMember)]
    [InlineData(HelloRequestBody.VersionLowMember)]
    [InlineData(HelloRequestBody.VersionHighMember)]
    [InlineData(HelloRequestBody.AddressMember)]
    public void AMemberTheTableNamesIsRequired(string missing)
    {
        var written = new[]
        {
            (HelloRequestBody.KeyMember, "\"" + Key + "\""),
            (HelloRequestBody.VersionLowMember, Low),
            (HelloRequestBody.VersionHighMember, High),
            (HelloRequestBody.AddressMember, "\"https://peer.example.org\""),
        };

        var text = new StringBuilder("{");

        foreach (var (name, value) in written)
        {
            if (string.Equals(name, missing, StringComparison.Ordinal))
            {
                continue;
            }

            if (text.Length > 1)
            {
                text.Append(',');
            }

            text.Append('"').Append(name).Append("\":").Append(value);
        }

        text.Append('}');

        Assert.Equal(BodyOutcome.DidNotParse, ArrivingBody.Read(PairingMessage.Hello, Bytes(text.ToString())).Outcome);
    }

    /// <summary>
    /// A member carrying <c>null</c>, a member appearing twice, a member carrying an object, an
    /// array or a boolean, and a name written with an escape rather than as the bytes the table
    /// names. Each is refused by the document in the same words as the others, so each is a
    /// mutation of the one accepted body rather than a case of its own.
    /// </summary>
    /// <param name="member">The member text that replaces the address member.</param>
    [Theory]
    [InlineData("\"address\":null")]
    [InlineData("\"address\":\"https://peer.example.org\",\"address\":\"https://other.example.org\"")]
    [InlineData("\"address\":{\"host\":\"peer.example.org\"}")]
    [InlineData("\"address\":[\"https://peer.example.org\"]")]
    [InlineData("\"address\":true")]
    [InlineData("\"\\u0061ddress\":\"https://peer.example.org\"")]
    public void AValueOrANameOutsideTheShapeIsRefused(string member)
    {
        var body = Bytes(
            "{\"" + HelloRequestBody.KeyMember + "\":\"" + Key
            + "\",\"" + HelloRequestBody.VersionLowMember + "\":" + Low
            + ",\"" + HelloRequestBody.VersionHighMember + "\":" + High
            + "," + member + "}");

        Assert.Equal(BodyOutcome.DidNotParse, ArrivingBody.Read(PairingMessage.Hello, body).Outcome);
    }

    /// <summary>
    /// A member's JSON type is part of what the table fixes. The two version members are numbers
    /// because the one refusal body that carries a range writes them as numbers, and the other
    /// two are strings, so a body that swaps either way is two implementations disagreeing about
    /// the wire rather than one being lenient.
    /// </summary>
    /// <param name="body">The body, with one member's type moved.</param>
    [Theory]
    [InlineData("{\"key\":\"KEY\",\"versionLow\":\"1\",\"versionHigh\":1,\"address\":\"https://peer.example.org\"}")]
    [InlineData("{\"key\":\"KEY\",\"versionLow\":1,\"versionHigh\":\"1\",\"address\":\"https://peer.example.org\"}")]
    [InlineData("{\"key\":1,\"versionLow\":1,\"versionHigh\":1,\"address\":\"https://peer.example.org\"}")]
    [InlineData("{\"key\":\"KEY\",\"versionLow\":1,\"versionHigh\":1,\"address\":1}")]
    public void AMemberOfTheWrongTypeIsRefused(string body)
    {
        var written = body.Replace("KEY", Key, StringComparison.Ordinal);

        Assert.Equal(BodyOutcome.DidNotParse, ArrivingBody.Read(PairingMessage.Hello, Bytes(written)).Outcome);
    }

    /// <summary>
    /// A value outside the limit the field table gives it, one member at a time. A violation is
    /// a refusal rather than a truncation, which is that table's own sentence.
    /// </summary>
    /// <param name="body">The body carrying the value outside its limit.</param>
    [Theory]
    [InlineData("{\"key\":\"not base64 at all\",\"versionLow\":1,\"versionHigh\":1,\"address\":\"https://peer.example.org\"}")]
    [InlineData("{\"key\":\"\",\"versionLow\":1,\"versionHigh\":1,\"address\":\"https://peer.example.org\"}")]
    [InlineData("{\"key\":\"KEY\",\"versionLow\":99999,\"versionHigh\":99999,\"address\":\"https://peer.example.org\"}")]
    [InlineData("{\"key\":\"KEY\",\"versionLow\":2,\"versionHigh\":1,\"address\":\"https://peer.example.org\"}")]
    [InlineData("{\"key\":\"KEY\",\"versionLow\":-1,\"versionHigh\":1,\"address\":\"https://peer.example.org\"}")]
    [InlineData("{\"key\":\"KEY\",\"versionLow\":1.5,\"versionHigh\":2,\"address\":\"https://peer.example.org\"}")]
    [InlineData("{\"key\":\"KEY\",\"versionLow\":1,\"versionHigh\":1,\"address\":\"\"}")]
    public void AValueOutsideItsLimitIsRefused(string body)
    {
        var written = body.Replace("KEY", Key, StringComparison.Ordinal);

        Assert.Equal(BodyOutcome.DidNotParse, ArrivingBody.Read(PairingMessage.Hello, Bytes(written)).Outcome);
    }

    /// <summary>
    /// An address longer than the field table allows is refused, and one exactly at the limit is
    /// not, so what is asserted is the boundary rather than that some long value fails.
    /// </summary>
    [Fact]
    public void AnAddressIsRefusedOneCharacterPastItsLimit()
    {
        Assert.Equal(BodyOutcome.Read, ArrivingBody.Read(PairingMessage.Hello, Hello(Address(PeerAddress.LengthLimit))).Outcome);
        Assert.Equal(
            BodyOutcome.DidNotParse,
            ArrivingBody.Read(PairingMessage.Hello, Hello(Address(PeerAddress.LengthLimit + 1))).Outcome);
    }

    /// <summary>
    /// A key longer than the field table allows is refused, and one exactly at the limit is not.
    /// </summary>
    [Fact]
    public void AKeyIsRefusedOneCharacterPastItsLimit()
    {
        var atTheLimit = Convert.ToBase64String(new byte[HelloRequestBody.KeyLengthLimit / 4 * 3]);
        var pastIt = Convert.ToBase64String(new byte[(HelloRequestBody.KeyLengthLimit / 4 * 3) + 3]);

        Assert.Equal(HelloRequestBody.KeyLengthLimit, atTheLimit.Length);
        Assert.Equal(BodyOutcome.Read, ArrivingBody.Read(PairingMessage.Hello, Hello(key: atTheLimit)).Outcome);
        Assert.Equal(BodyOutcome.DidNotParse, ArrivingBody.Read(PairingMessage.Hello, Hello(key: pastIt)).Outcome);
    }

    /// <summary>
    /// A fingerprint digest is exactly the length and the alphabet the field table fixes.
    /// </summary>
    /// <param name="digest">The digest presented.</param>
    [Theory]
    [InlineData("")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void ADigestOutsideItsShapeIsRefused(string digest)
    {
        Assert.Equal(BodyOutcome.DidNotParse, ArrivingBody.Read(PairingMessage.Confirm, Confirm(digest)).Outcome);
        Assert.Equal(BodyOutcome.Read, ArrivingBody.Read(PairingMessage.Confirm, Confirm(Digest)).Outcome);
    }

    /// <summary>
    /// A body that is not one object at all, and a body carrying anything after the object ends.
    /// Neither is the shape the document fixes and each is refused rather than read as far as it
    /// goes.
    /// </summary>
    /// <param name="body">The bytes presented.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("{\"digest\":\"DIGEST\"} {\"digest\":\"DIGEST\"}")]
    [InlineData("{\"digest\":\"DIGEST\"}trailing")]
    [InlineData("{\"digest\":\"DIGEST\",}")]
    [InlineData("{/* a comment */\"digest\":\"DIGEST\"}")]
    [InlineData("{\"digest\":\"DIGEST\"")]
    public void ABodyThatIsNotOneWholeObjectIsRefused(string body)
    {
        var written = body.Replace("DIGEST", Digest, StringComparison.Ordinal);

        Assert.Equal(BodyOutcome.DidNotParse, ArrivingBody.Read(PairingMessage.Confirm, Bytes(written)).Outcome);
    }

    /// <summary>
    /// Empty means zero bytes. A message whose table row says it carries no body is read where
    /// nothing arrived, and is refused where anything did, so a member cannot be smuggled into a
    /// body the document says has none and then relied on.
    /// </summary>
    /// <param name="message">The message whose row says empty.</param>
    [Theory]
    [MemberData(nameof(EmptyBodied))]
    public void EmptyMeansZeroBytes(PairingMessage message)
    {
        Assert.Equal(BodyOutcome.Read, ArrivingBody.Read(message, ReadOnlySpan<byte>.Empty).Outcome);

        foreach (var written in new[] { "{}", " ", "\n", "{\"reason\":\"revoked\"}" })
        {
            Assert.Equal(BodyOutcome.DidNotParse, ArrivingBody.Read(message, Bytes(written)).Outcome);
        }
    }

    /// <summary>
    /// The two bodies nothing on this plane reads are answered as unread rather than as read or
    /// refused, whatever they carry. An <c>exchange</c> is opaque to this layer by the document,
    /// and a <c>rotate</c> has a member table and no reader yet; a case asserting either was
    /// refused would be asserting a rule neither has.
    /// </summary>
    /// <param name="message">The message whose body is not read here.</param>
    [Theory]
    [MemberData(nameof(Unread))]
    public void ABodyNoReaderJudgesIsNeitherReadNorRefused(PairingMessage message)
    {
        foreach (var written in new[] { string.Empty, "{}", "not json at all", "{\"anything\":1}" })
        {
            var read = ArrivingBody.Read(message, Bytes(written));

            Assert.Equal(BodyOutcome.NotReadHere, read.Outcome);
            Assert.Null(read.Hello);
            Assert.Null(read.Confirm);
        }
    }

    /// <summary>
    /// A message outside the defined set is a caller error rather than a refusal, which is the
    /// same answer every other table in this plugin gives one. Guessing a body for it would
    /// serve a seventh message this protocol does not have.
    /// </summary>
    [Fact]
    public void AMessageOutsideTheDefinedSetIsACallerError()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ArrivingBody.Read((PairingMessage)99, ReadOnlySpan<byte>.Empty));
    }

    private static string Low => SupportedVersions.Lowest.ToString(CultureInfo.InvariantCulture);

    private static string High => SupportedVersions.Highest.ToString(CultureInfo.InvariantCulture);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>
    /// An address of an exact length, built out of a host label so that what changes between two
    /// of them is the length and nothing else.
    /// </summary>
    /// <param name="length">How many characters the whole address carries.</param>
    /// <returns>The address.</returns>
    private static string Address(int length)
    {
        const string Prefix = "https://";
        const string Suffix = ".example.org";

        return Prefix + new string('a', length - Prefix.Length - Suffix.Length) + Suffix;
    }

    private static byte[] Hello(string? address = null, string? key = null) => Bytes(
        "{\"" + HelloRequestBody.KeyMember + "\":\"" + (key ?? Key)
        + "\",\"" + HelloRequestBody.VersionLowMember + "\":" + Low
        + ",\"" + HelloRequestBody.VersionHighMember + "\":" + High
        + ",\"" + HelloRequestBody.AddressMember + "\":\"" + (address ?? "https://peer.example.org") + "\"}");

    private static byte[] Confirm(string digest) =>
        Bytes("{\"" + ConfirmRequestBody.DigestMember + "\":\"" + digest + "\"}");
}
