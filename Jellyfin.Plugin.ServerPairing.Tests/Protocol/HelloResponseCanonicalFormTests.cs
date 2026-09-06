using System;
using System.Text;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// Which value line 3 of a response's canonical form holds, and what the type that builds
/// those bytes does with it.
/// </summary>
/// <remarks>
/// <c>docs/protocol.md</c> fixes a response's canonical form at six lines and makes line 3 the
/// pairing identifier. On a <c>hello</c> response there were two candidates for it, the 32
/// zeros the request carried and the identifier the two public keys derive, and the document
/// named neither, so two implementations that both followed it signed different bytes. The
/// document fixes the derived identifier now, and this file is that answer read back.
/// <para>
/// WHAT NO CASE HERE ASSERTS IS WHICH VALUE A CALLER PASSES, because there is no caller.
/// Nothing in this repository builds a <c>hello</c> response or signs one; the enrolment is
/// issue #19. A case asserting the derived value reached line 3 would have to make the call
/// itself and would be asserting its own argument, which is a green that reads as a guard and
/// is not one. What is asserted instead is the property that makes the document's answer the
/// whole of the obligation: this type reproduces the identifier it is handed, so the value put
/// in is the value signed, and the mistake can only live at the caller.
/// </para>
/// </remarks>
public class HelloResponseCanonicalFormTests
{
    /// <summary>
    /// The identifier two public keys derive, in the shape the field table fixes: 32 lowercase
    /// hex characters. The value carries no meaning here; what matters is that it is not the
    /// placeholder.
    /// </summary>
    private const string Derived = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";

    /// <summary>
    /// The placeholder a <c>hello</c> request carries, which is the reading the document used
    /// to allow for the response as well.
    /// </summary>
    private const string Placeholder = "00000000000000000000000000000000";

    private const string Nonce = "0123456789abcdef0123456789abcdef";

    private const string Version = "1";

    private const string Timestamp = "1786000000";

    /// <summary>
    /// A response's canonical form is six lines, each ending in one line feed, with no carriage
    /// return anywhere, and it opens with the response label rather than the request one. That
    /// is the shape <c>docs/protocol.md</c> fixes, and it is asserted here rather than left to
    /// be inferred from a signature matching.
    /// </summary>
    [Fact]
    public void AResponseCanonicalFormIsSixLinesEndingInLineFeeds()
    {
        var canonical = Encoding.ASCII.GetString(Form(Derived));

        Assert.DoesNotContain("\r", canonical, StringComparison.Ordinal);
        Assert.EndsWith("\n", canonical, StringComparison.Ordinal);
        Assert.Equal(6, canonical.Split('\n').Length - 1);
        Assert.StartsWith(CanonicalForm.ResponseLabel + "\n", canonical, StringComparison.Ordinal);
    }

    /// <summary>
    /// Line 3 is the identifier the caller handed in, byte for byte, and that holds for the
    /// derived value and for the placeholder alike. Neither is substituted for the other and
    /// neither is normalised, so which value a <c>hello</c> response signs is decided entirely
    /// where the call is made.
    /// </summary>
    /// <param name="pairingId">The identifier handed to the builder.</param>
    [Theory]
    [InlineData(Derived)]
    [InlineData(Placeholder)]
    public void LineThreeIsTheIdentifierTheCallerHandedIn(string pairingId)
    {
        var lines = Encoding.ASCII.GetString(Form(pairingId)).Split('\n');

        Assert.Equal(pairingId, lines[2]);
    }

    /// <summary>
    /// The two readings the document allowed produce different bytes, and they differ on line 3
    /// and nowhere else. That is why the question was worth answering rather than leaving to
    /// each implementer: a signature made under one reading is made over bytes the other never
    /// builds, so two servers that chose differently would refuse each other at the first
    /// signature in a pairing while agreeing about everything else.
    /// </summary>
    [Fact]
    public void TheTwoReadingsDifferOnLineThreeAndNowhereElse()
    {
        var derived = Encoding.ASCII.GetString(Form(Derived)).Split('\n');
        var placeholder = Encoding.ASCII.GetString(Form(Placeholder)).Split('\n');

        Assert.NotEqual(derived[2], placeholder[2]);

        for (var line = 0; line < derived.Length; line++)
        {
            if (line == 2)
            {
                continue;
            }

            Assert.Equal(derived[line], placeholder[line]);
        }
    }

    private static byte[] Form(string pairingId)
        => CanonicalForm.ForResponse(
            Version,
            pairingId,
            Nonce,
            Timestamp,
            ReadOnlyMemory<byte>.Empty);
}
