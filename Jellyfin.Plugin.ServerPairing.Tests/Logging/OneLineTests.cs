using System;
using System.Globalization;
using Jellyfin.Plugin.ServerPairing.Logging;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Logging;

/// <summary>
/// What <see cref="OneLine"/> lets through and what it replaces.
/// </summary>
/// <remarks>
/// EVERY CHARACTER UNDER TEST IS NAMED BY ITS CODEPOINT AND NEVER WRITTEN AS ITSELF, so this
/// file is ASCII throughout. Two reasons, and both are about the file rather than about the
/// case. The bidirectional override is refused in tracked source by
/// <c>.github/workflows/unicode-guard.yml</c>, which is the same attack this type is against
/// arriving at review time instead of at runtime, so a case written with the literal would red
/// the guard it agrees with. And a carriage return in a tracked text file is normalised by this
/// repository's line endings on the way through git, which would delete the byte the case
/// exists to prove.
/// </remarks>
public class OneLineTests
{
    private const char LineFeed = (char)0x000A;
    private const char CarriageReturn = (char)0x000D;
    private const char Escape = (char)0x001B;
    private const char NextLine = (char)0x0085;
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;
    private const char RightToLeftOverride = (char)0x202E;
    private const char ZeroWidthSpace = (char)0x200B;
    private const char EmDash = (char)0x2014;

    /// <summary>
    /// Gets every character a log entry may not carry, one case each.
    /// </summary>
    /// <remarks>
    /// The set is asserted member by member rather than by naming the categories the expression
    /// uses, so each case is a character somebody argued for and a category widened by accident
    /// is not proved by the same rows. Line feed and carriage return are the break itself;
    /// escape drives the terminal that renders the file; next line, line separator and paragraph
    /// separator are breaks a viewer may honour where a plain reader does not; the right-to-left
    /// override is CVE-2021-42574, which reverses what a reader sees without changing what was
    /// stored; the zero-width space is invisible and can hide the seam of a forged value.
    /// </remarks>
    public static TheoryData<char> Breaking =>
        new TheoryData<char>
        {
            LineFeed,
            CarriageReturn,
            Escape,
            NextLine,
            LineSeparator,
            ParagraphSeparator,
            RightToLeftOverride,
            ZeroWidthSpace,
        };

    /// <summary>
    /// A value with nothing to replace comes back as itself.
    /// </summary>
    /// <param name="value">A value a log entry may carry unchanged.</param>
    [Theory]
    [InlineData("9f8c1d2b3a4e5f60718293a4b5c6d7e8")]
    [InlineData("api-key")]
    [InlineData("")]
    [InlineData("a value with spaces, punctuation: and a tab is not among them")]
    public void AValueWithNothingToReplaceComesBackAsItself(string value)
    {
        Assert.Equal(value, OneLine.Of(value));
    }

    /// <summary>
    /// Non-ASCII that is neither a break nor a reordering passes through, so an operator name or
    /// a peer address in any script reads as itself.
    /// </summary>
    [Fact]
    public void BenignNonAsciiPassesThrough()
    {
        var value = string.Create(CultureInfo.InvariantCulture, $"a name {EmDash} written with an em dash");

        Assert.Equal(value, OneLine.Of(value));
    }

    /// <summary>
    /// A null value is the empty string rather than the word null, because an entry reading
    /// "administrator: null" is a sentence an operator can misread as a value somebody sent.
    /// </summary>
    [Fact]
    public void ANullValueIsTheEmptyString()
    {
        Assert.Equal(string.Empty, OneLine.Of(null));
    }

    /// <summary>
    /// Every character that would break the line, drive the terminal or reorder the text is
    /// replaced, one for one.
    /// </summary>
    /// <param name="carried">The character the value carries.</param>
    [Theory]
    [MemberData(nameof(Breaking))]
    public void ACharacterThatWouldBreakOrReorderTheLineIsReplaced(char carried)
    {
        Assert.Equal(
            "before" + OneLine.Replacement + "after",
            OneLine.Of("before" + carried + "after"));
    }

    /// <summary>
    /// A forged entry is what this is against, so the case is the forgery rather than a
    /// character in isolation: a value carrying a break and a plausible second entry comes out
    /// as one line, and what the caller wrote is still readable as theirs.
    /// </summary>
    [Fact]
    public void AValueCarryingASecondEntryComesOutAsOneLine()
    {
        var forged = "9f8c" + CarriageReturn + LineFeed + "[Warning] A pairing was revoked by an administrator.";

        var written = OneLine.Of(forged);

        Assert.DoesNotContain(LineFeed, written);
        Assert.DoesNotContain(CarriageReturn, written);
        Assert.Contains("A pairing was revoked", written, StringComparison.Ordinal);
        Assert.Equal(forged.Length, written.Length);
    }

    /// <summary>
    /// The replacement stands where the character stood rather than the character being dropped,
    /// so a value that was tampered with does not read as a value that was clean.
    /// </summary>
    [Fact]
    public void TheReplacementStandsWhereTheCharacterStood()
    {
        Assert.Equal(
            OneLine.Replacement + OneLine.Replacement,
            OneLine.Of(string.Empty + CarriageReturn + LineFeed));
    }
}
