using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// The peer address is the one thing an operator types that decides where this server sends
/// an authenticated request, so what may be typed is a list rather than a pattern that passed
/// the examples somebody tried.
/// </summary>
public class PeerAddressTests
{
    /// <summary>
    /// One accepted and one refused example for each form <c>docs/protocol.md</c> lists,
    /// with the rule that refuses the second. The refused example of each row is a near
    /// neighbour of the accepted one rather than obvious rubbish, because the mistake worth
    /// refusing is the one somebody will actually type.
    /// </summary>
    [Theory]
    [InlineData("domain name", "https://peer.example.org", "https://peer.example.org/pairing", PeerAddressOutcome.PathPresent)]
    [InlineData("domain name and port", "https://peer.example.org:8920", "https://peer.example.org:0", PeerAddressOutcome.PortNotAllowed)]
    [InlineData("IPv4 literal", "https://192.0.2.10", "https://operator@192.0.2.10", PeerAddressOutcome.UserInfoPresent)]
    [InlineData("bracketed IPv6 literal", "https://[2001:db8::10]:8920", "https://2001:db8::10", PeerAddressOutcome.NotAnAbsoluteUri)]
    public void EachListedFormHasAnAcceptedAndARefusedExample(string form, string accepted, string refused, PeerAddressOutcome refusal)
    {
        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse(accepted, out var address));
        Assert.NotNull(address);
        Assert.Equal(refusal, PeerAddress.Parse(refused, out var none));
        Assert.Null(none);
        Assert.NotEmpty(form);
    }

    /// <summary>
    /// Everything outside the list is refused, and the refusal names the rule. A single
    /// invalid-address answer for every cause is what makes an operator retype the address
    /// they already typed.
    /// </summary>
    [Theory]
    [InlineData(null, PeerAddressOutcome.Empty)]
    [InlineData("", PeerAddressOutcome.Empty)]
    [InlineData("peer.example.org", PeerAddressOutcome.NotAnAbsoluteUri)]
    [InlineData("https://peer.example.org\n", PeerAddressOutcome.NotAnAbsoluteUri)]
    [InlineData("http://peer.example.org", PeerAddressOutcome.SchemeNotAllowed)]
    [InlineData("ftp://peer.example.org", PeerAddressOutcome.SchemeNotAllowed)]
    [InlineData("https://peer.example.org?a=1", PeerAddressOutcome.QueryPresent)]
    [InlineData("https://peer.example.org#top", PeerAddressOutcome.FragmentPresent)]
    [InlineData("https://peer.example.org//", PeerAddressOutcome.PathPresent)]
    [InlineData("https://peer.exämple.org", PeerAddressOutcome.NotAnAbsoluteUri)]
    [InlineData("https://peer.example.org.", PeerAddressOutcome.HostFormNotAllowed)]
    [InlineData("https://peer_example.org", PeerAddressOutcome.HostFormNotAllowed)]
    [InlineData("https://peer..example.org", PeerAddressOutcome.NotAnAbsoluteUri)]
    public void EverythingOutsideTheListIsRefusedByTheRuleThatRefusedIt(string? candidate, PeerAddressOutcome expected)
    {
        Assert.Equal(expected, PeerAddress.Parse(candidate, out var address));
        Assert.Null(address);
    }

    /// <summary>
    /// The length limit is the one in the field table, and it is checked before anything is
    /// parsed, so a long candidate costs nothing to refuse.
    /// </summary>
    [Fact]
    public void AnAddressLongerThanTheFieldLimitIsRefused()
    {
        var host = new string('a', PeerAddress.LengthLimit);

        Assert.Equal(PeerAddressOutcome.TooLong, PeerAddress.Parse("https://" + host + ".example.org", out _));
    }

    /// <summary>
    /// Two spellings of one address compare equal, because an operator who typed the default
    /// port or a trailing slash approved the same peer as one who did not. The comparison is
    /// on what this type produces rather than on what was typed.
    /// </summary>
    [Theory]
    [InlineData("https://peer.example.org")]
    [InlineData("https://peer.example.org/")]
    [InlineData("https://peer.example.org:443")]
    [InlineData("https://PEER.example.ORG")]
    public void OneAddressSpeltSeveralWaysIsTheSameApprovedAddress(string spelling)
    {
        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse("https://peer.example.org", out var approved));

        Assert.True(approved!.Approves(spelling));
    }

    /// <summary>
    /// A peer names an address of its own in <c>hello</c>. Anything that is not the approved
    /// address is not approved, whether it is a different peer, a different port, a scheme
    /// this plugin does not talk, or something that does not parse at all.
    /// </summary>
    [Theory]
    [InlineData("https://other.example.org")]
    [InlineData("https://peer.example.org:8920")]
    [InlineData("http://peer.example.org")]
    [InlineData("https://peer.example.org/pairing")]
    [InlineData("peer.example.org")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingOtherThanTheApprovedAddressIsNotApproved(string? claimed)
    {
        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse("https://peer.example.org", out var approved));

        Assert.False(approved!.Approves(claimed));
    }

    /// <summary>
    /// The recorded address cannot be moved by anything that arrives on the wire. There is
    /// no member on any type in the plugin that can hold a
    /// <see cref="PeerAddress"/> and be written after construction, and no public constructor
    /// that would let one be built from a message field without going through the operator
    /// entry path.
    ///
    /// This reads the built assembly rather than the source, so it holds for the endpoints
    /// that do not exist yet: the day a message type gains a settable address, this reds.
    ///
    /// Compiler-generated types are skipped, the same carve-out and for the same reason as
    /// <c>StaticStateTests</c>: an async method's state machine holds each of its parameters
    /// in a writable field, and that field is the method's own argument rather than a place
    /// a peer could reach.
    /// </summary>
    [Fact]
    public void NothingInThePluginCanWriteAnAddressAfterItIsBuilt()
    {
        var writable = typeof(PeerAddress).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(PeerAddress) && p.CanWrite)
                .Select(p => t.Name + "." + p.Name)
                .Concat(t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(f => f.FieldType == typeof(PeerAddress) && !f.IsInitOnly)
                    .Select(f => t.Name + "." + f.Name)))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), writable);
        Assert.Empty(typeof(PeerAddress).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    /// <summary>
    /// The check above passes over an assembly that holds no writable address at all, which
    /// is the state of the tree today, so on its own it proves nothing about the scan. This
    /// is the same scan over a type that does hold one, and it has to find it.
    /// </summary>
    [Fact]
    public void TheScanFindsAWritableAddressWhenThereIsOne()
    {
        var writable = typeof(AddressThatCanBeOverwritten)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(PeerAddress) && p.CanWrite)
            .Select(p => p.Name)
            .ToArray();

        Assert.Equal(nameof(AddressThatCanBeOverwritten.Address), Assert.Single(writable));
    }

    /// <summary>
    /// A cleartext address approves the claim a cleartext peer makes about itself. The claim
    /// goes through the same parse the address did, so an address accepted under the
    /// operator's acknowledgement would otherwise approve nothing at all and every hello from
    /// the peer the operator entered would be refused for naming the address it was given.
    /// </summary>
    [Fact]
    public void ACleartextAddressApprovesTheClaimACleartextPeerMakes()
    {
        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse("http://peer.example", true, out var approved));

        Assert.True(approved!.Approves("http://peer.example"));
        Assert.True(approved.Approves("HTTP://Peer.Example:80/"));
    }

    /// <summary>
    /// It widens nothing. A claim under the other scheme canonicalises to a different value
    /// and fails the comparison in both directions, so a peer cannot move a pairing from one
    /// scheme to the other by claiming it.
    /// </summary>
    [Fact]
    public void NeitherSchemeApprovesAClaimUnderTheOther()
    {
        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse("http://peer.example", true, out var cleartext));
        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse("https://peer.example", out var secure));

        Assert.False(cleartext!.Approves("https://peer.example"));
        Assert.False(secure!.Approves("http://peer.example"));
    }

    /// <summary>
    /// The overload that takes no acknowledgement refuses in the closed direction, so a
    /// caller who has read no setting cannot reach the permissive answer by forgetting to
    /// pass one.
    /// </summary>
    [Fact]
    public void TheOverloadTakingNoAcknowledgementRefusesCleartext()
    {
        Assert.Equal(PeerAddressOutcome.SchemeNotAllowed, PeerAddress.Parse("http://peer.example", out var refused));
        Assert.Null(refused);

        Assert.Equal(PeerAddressOutcome.SchemeNotAllowed, PeerAddress.Parse("http://peer.example", false, out var alsoRefused));
        Assert.Null(alsoRefused);
    }

    /// <summary>
    /// The shape the scan above exists to refuse. It lives in the test assembly, which the
    /// scan does not read, so it proves the scan without being caught by it.
    /// </summary>
    private sealed class AddressThatCanBeOverwritten
    {
        public PeerAddress? Address { get; set; }
    }
}
