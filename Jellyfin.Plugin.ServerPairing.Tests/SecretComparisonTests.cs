using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// A signature, a key or a fingerprint compared with <c>==</c> leaks how many leading bytes
/// were right, one call at a time, to whoever is allowed to keep asking. The comparison that
/// does not is <c>CryptographicOperations.FixedTimeEquals</c>, and <c>docs/crypto.md</c> pins
/// it as the only one this plugin uses on secret material.
/// <para>
/// This guard refuses the wrong comparison in the plugin source by reading the names being
/// compared. It is written before the first comparison exists, because the day it is written
/// afterwards is the day somebody has to find the ones already there.
/// </para>
/// <para>
/// What it cannot do is read a type. It judges an identifier by its words, so secret material
/// held in a variable named <c>a</c> walks straight through it, and so does a comparison
/// assembled across two lines. That bound is real, it is not narrowed by any assertion here,
/// and it is why this is a floor rather than a proof.
/// </para>
/// <para>
/// The scan reads the plugin project and not the suite. The fixtures below are the lines the
/// guard has to refuse, held as text in this file, so a scan covering the suite would refuse
/// the fixtures that prove the guard works.
/// </para>
/// </summary>
public class SecretComparisonTests
{
    /// <summary>
    /// The file that marks the repository root. It is tracked and it is at the top of the
    /// tree, so a walk upwards from the build output finds it on any machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The one comparison allowed on secret material. It is named here so that the guard and
    /// the document pin the same call.
    /// </summary>
    private const string ConstantTimeCall = "CryptographicOperations.FixedTimeEquals";

    /// <summary>
    /// The words that mark an identifier as carrying secret or authenticating material.
    /// </summary>
    private static readonly string[] SecretWords =
    {
        "key",
        "keys",
        "secret",
        "signature",
        "mac",
        "nonce",
        "fingerprint",
        "digest",
        "token",
        "tag",
    };

    /// <summary>
    /// The words that turn an identifier carrying one of the words above into a measurement of
    /// that material rather than the material itself. A length, a count and an identifier are
    /// all things a plugin has to compare, and refusing them would make the guard something
    /// people delete rather than something they satisfy.
    /// </summary>
    private static readonly string[] MeasurementWords =
    {
        "length",
        "count",
        "size",
        "id",
        "index",
        "name",
        "path",
        "kind",
        "type",
        "state",
        "version",
        "address",
    };

    /// <summary>
    /// The comparison operators that are not constant time.
    /// </summary>
    private static readonly string[] Operators =
    {
        "==",
        "!=",
    };

    /// <summary>
    /// The comparison methods that are not constant time. <c>FixedTimeEquals</c> matches
    /// neither of them: the characters before <c>Equals(</c> in its name are <c>FixedTime</c>
    /// rather than a dot.
    /// </summary>
    private static readonly string[] Methods =
    {
        ".Equals(",
        ".SequenceEqual(",
    };

    /// <summary>
    /// Nothing in the plugin compares secret material with anything but the constant-time
    /// call. This reads the tree as it is today, so it is a statement about the tree on the
    /// day it runs. What it says about the guard itself is nothing; the fixtures below are
    /// what say that.
    /// </summary>
    [Fact]
    public void NoPluginSourceFileComparesSecretMaterialTheWrongWay()
    {
        var offences = new List<string>();

        foreach (var file in SourceFiles(PluginSourceDirectory()))
        {
            offences.AddRange(Offences(Path.GetFileName(file), File.ReadAllText(file)));
        }

        Assert.Equal(Array.Empty<string>(), offences.OrderBy(o => o, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The guard refuses the thing it exists to refuse, in every spelling of it that a person
    /// writes without thinking about timing.
    /// </summary>
    /// <param name="line">A line the guard has to refuse.</param>
    [Theory]
    [InlineData("if (signature == expected) { return true; }")]
    [InlineData("if (expected != peerSignature) { return false; }")]
    [InlineData("return computedMac.SequenceEqual(givenMac);")]
    [InlineData("return sessionKey.Equals(other);")]
    [InlineData("return string.Equals(fingerprint, shown, StringComparison.Ordinal);")]
    [InlineData("var same = pairing.Nonce == arriving;")]
    public void TheGuardRefusesTheComparisonItExistsToRefuse(string line)
    {
        Assert.NotEmpty(Offences("fixture.cs", line));
    }

    /// <summary>
    /// The same comparisons written through the constant-time call are not refused. Without
    /// this, a guard that refused every line in the file would pass the theory above and
    /// prove nothing.
    /// </summary>
    /// <param name="line">A line the guard has to allow.</param>
    [Theory]
    [InlineData("if (CryptographicOperations.FixedTimeEquals(signature, expected)) { return true; }")]
    [InlineData("return CryptographicOperations.FixedTimeEquals(computedMac, givenMac);")]
    public void TheGuardAllowsTheConstantTimeCall(string line)
    {
        Assert.Empty(Offences("fixture.cs", line));
    }

    /// <summary>
    /// The near miss worth spending the effort on. Comparing the length of a signature is
    /// ordinary and necessary, and it is one word away from comparing the signature. A guard
    /// that refuses both is a guard somebody turns off.
    /// </summary>
    /// <param name="line">A line the guard has to allow.</param>
    [Theory]
    [InlineData("if (signatureLength == 64) { return false; }")]
    [InlineData("if (nonceCount == 0) { return false; }")]
    [InlineData("if (pairing.KeyId != known) { return false; }")]
    [InlineData("if (keyStorePath == other) { return false; }")]
    public void TheGuardAllowsAMeasurementOfSecretMaterial(string line)
    {
        Assert.Empty(Offences("fixture.cs", line));
    }

    /// <summary>
    /// A comparison inside a string literal or a comment is text rather than code, and
    /// refusing it would make this document's own examples unwritable in the source.
    /// </summary>
    /// <param name="line">A line the guard has to allow.</param>
    [Theory]
    [InlineData("// never write signature == expected here")]
    [InlineData("var message = \"signature == expected\";")]
    public void TheGuardReadsCodeRatherThanText(string line)
    {
        Assert.Empty(Offences("fixture.cs", line));
    }

    /// <summary>
    /// The offence names the file and the line, because a guard that says only that something
    /// is wrong somewhere costs more to satisfy than it is worth.
    /// </summary>
    [Fact]
    public void AnOffenceNamesWhereItIs()
    {
        var offences = Offences("Verifier.cs", "ok();\nif (signature == expected)\n{\n}\n");

        Assert.Single(offences);
        Assert.Equal("Verifier.cs:2: signature", offences[0]);
    }

    /// <summary>
    /// The scan reads a real set of files. Both assertions above pass trivially over an empty
    /// set, which is what happens the day somebody moves or renames a project.
    /// </summary>
    [Fact]
    public void TheScanReadsRealFiles()
    {
        var files = SourceFiles(PluginSourceDirectory());

        Assert.NotEmpty(files);
        Assert.Contains(files, f => Path.GetFileName(f) == "Plugin.cs");
    }

    /// <summary>
    /// The vocabularies are not empty, so an edit that empties one turns the guard off in a
    /// way that would otherwise stay green.
    /// </summary>
    [Fact]
    public void EveryVocabularyHasEntries()
    {
        Assert.NotEmpty(SecretWords);
        Assert.NotEmpty(MeasurementWords);
        Assert.NotEmpty(Operators);
        Assert.NotEmpty(Methods);
    }

    /// <summary>
    /// The call the guard requires is the one that decides equality, and a wrong byte at the
    /// front and a wrong byte at the back are decided by that same call rather than by two
    /// different paths.
    /// <para>
    /// This asserts the call and not the time it took. A timing assertion on a shared build
    /// machine is red on some later day for reasons that have nothing to do with the code,
    /// and the first response to a flaky test is to delete it, which is how a guard like this
    /// ends up not existing. Issue #16 asks for the call to be asserted rather than measured
    /// for exactly that reason.
    /// </para>
    /// </summary>
    [Fact]
    public void AWrongFirstByteAndAWrongLastByteAreDecidedByTheSameCall()
    {
        var expected = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var wrongFirst = expected.ToArray();
        wrongFirst[0] ^= 0xFF;

        var wrongLast = expected.ToArray();
        wrongLast[^1] ^= 0xFF;

        Assert.False(CryptographicOperations.FixedTimeEquals(expected, wrongFirst));
        Assert.False(CryptographicOperations.FixedTimeEquals(expected, wrongLast));
        Assert.True(CryptographicOperations.FixedTimeEquals(expected, expected.ToArray()));
    }

    /// <summary>
    /// A length mismatch is answered false rather than thrown, so a caller does not need a
    /// length check in front of the call, and a length check written in front of it does not
    /// become the place the timing leak moves to.
    /// </summary>
    [Fact]
    public void ADifferentLengthIsAnsweredRatherThanThrown()
    {
        var eight = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var seven = new byte[] { 1, 2, 3, 4, 5, 6, 7 };

        Assert.False(CryptographicOperations.FixedTimeEquals(eight, seven));
    }

    /// <summary>
    /// The document and the guard name one call rather than two.
    /// </summary>
    [Fact]
    public void TheGuardNamesTheCallTheDocumentPins()
    {
        var document = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "crypto.md"));

        Assert.Contains(ConstantTimeCall, document, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every comparison of secret material on a line, as the name of the operand that made it
    /// one.
    /// </summary>
    /// <param name="fileName">The name to report an offence against.</param>
    /// <param name="text">The source text to read.</param>
    /// <returns>One entry per offence, as file, line number and the operand.</returns>
    private static string[] Offences(string fileName, string text)
    {
        var found = new List<string>();
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = WithoutTextAndComments(lines[i]);

            foreach (var operand in ComparedOperands(line))
            {
                if (CarriesSecretMaterial(operand))
                {
                    found.Add(fileName + ":" + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + operand);
                }
            }
        }

        return found.OrderBy(f => f, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// The line with string literals and the trailing line comment blanked out, so that the
    /// operand search reads code and not prose.
    /// </summary>
    /// <param name="line">The line to reduce.</param>
    /// <returns>The line with every non-code character replaced by a space.</returns>
    private static string WithoutTextAndComments(string line)
    {
        var kept = new char[line.Length];
        var inText = false;

        for (var i = 0; i < line.Length; i++)
        {
            if (!inText && line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                for (var j = i; j < line.Length; j++)
                {
                    kept[j] = ' ';
                }

                break;
            }

            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inText = !inText;
                kept[i] = ' ';
                continue;
            }

            kept[i] = inText ? ' ' : line[i];
        }

        return new string(kept);
    }

    /// <summary>
    /// Every identifier on a line that is an operand of a comparison that is not constant
    /// time.
    /// </summary>
    /// <param name="line">The line to read, already reduced to code.</param>
    /// <returns>The operands found, in the order they appear.</returns>
    private static IEnumerable<string> ComparedOperands(string line)
    {
        var operands = new List<string>();

        foreach (var op in Operators)
        {
            for (var at = line.IndexOf(op, StringComparison.Ordinal); at >= 0; at = line.IndexOf(op, at + op.Length, StringComparison.Ordinal))
            {
                operands.Add(IdentifierEndingAt(line, at - 1));
                operands.Add(IdentifierStartingAt(line, at + op.Length));
            }
        }

        foreach (var method in Methods)
        {
            for (var at = line.IndexOf(method, StringComparison.Ordinal); at >= 0; at = line.IndexOf(method, at + method.Length, StringComparison.Ordinal))
            {
                operands.Add(IdentifierEndingAt(line, at - 1));
                operands.Add(IdentifierStartingAt(line, at + method.Length));
            }
        }

        return operands.Where(o => o.Length > 0);
    }

    /// <summary>
    /// The identifier whose last character sits at or before the given index, skipping the
    /// spaces in between.
    /// </summary>
    /// <param name="line">The line to read.</param>
    /// <param name="index">The index to read back from.</param>
    /// <returns>The identifier, or an empty string where there is none.</returns>
    private static string IdentifierEndingAt(string line, int index)
    {
        var end = index;
        while (end >= 0 && line[end] == ' ')
        {
            end--;
        }

        var start = end;
        while (start >= 0 && IsIdentifierCharacter(line[start]))
        {
            start--;
        }

        return end <= start ? string.Empty : line[(start + 1)..(end + 1)];
    }

    /// <summary>
    /// The identifier whose first character sits at or after the given index, skipping the
    /// spaces in between.
    /// </summary>
    /// <param name="line">The line to read.</param>
    /// <param name="index">The index to read forward from.</param>
    /// <returns>The identifier, or an empty string where there is none.</returns>
    private static string IdentifierStartingAt(string line, int index)
    {
        var start = index;
        while (start < line.Length && line[start] == ' ')
        {
            start++;
        }

        var end = start;
        while (end < line.Length && IsIdentifierCharacter(line[end]))
        {
            end++;
        }

        return line[start..end];
    }

    /// <summary>
    /// Whether a character can appear inside the qualified name of an operand.
    /// </summary>
    /// <param name="c">The character to judge.</param>
    /// <returns>True where it can.</returns>
    private static bool IsIdentifierCharacter(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c == '.';

    /// <summary>
    /// Whether an operand's name says it carries secret material. The last word decides: a
    /// name whose last word measures the material describes the measurement, and every other
    /// name carrying a secret word describes the material.
    /// </summary>
    /// <param name="operand">The operand name, possibly qualified.</param>
    /// <returns>True where it carries secret material.</returns>
    private static bool CarriesSecretMaterial(string operand)
    {
        var words = Words(operand.Split('.')[^1]);

        if (words.Count == 0)
        {
            return false;
        }

        return words.Any(w => SecretWords.Contains(w, StringComparer.Ordinal))
            && !MeasurementWords.Contains(words[^1], StringComparer.Ordinal);
    }

    /// <summary>
    /// An identifier split into its words, lowercased, on camel case boundaries and on
    /// underscores.
    /// </summary>
    /// <param name="identifier">The identifier to split.</param>
    /// <returns>The words, in order.</returns>
    private static List<string> Words(string identifier)
    {
        var words = new List<string>();
        var current = string.Empty;

        foreach (var c in identifier)
        {
            if (c == '_' || char.IsUpper(c))
            {
                if (current.Length > 0)
                {
                    words.Add(current);
                }

                current = c == '_' ? string.Empty : char.ToLowerInvariant(c).ToString();
                continue;
            }

            current += c;
        }

        if (current.Length > 0)
        {
            words.Add(current);
        }

        return words;
    }

    /// <summary>
    /// Every C# file under a directory, skipping the build output.
    /// </summary>
    /// <param name="directory">The directory to walk.</param>
    /// <returns>The paths of the files found.</returns>
    private static string[] SourceFiles(string directory)
        => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Segment("obj"), StringComparison.Ordinal))
            .Where(f => !f.Contains(Segment("bin"), StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// A path segment with a separator on both sides, so that a directory named obj is
    /// skipped and a file named something-obj-something is not.
    /// </summary>
    /// <param name="name">The directory name.</param>
    /// <returns>The name wrapped in directory separators.</returns>
    private static string Segment(string name)
        => $"{Path.DirectorySeparatorChar}{name}{Path.DirectorySeparatorChar}";

    /// <summary>
    /// The plugin project directory, derived from where the test assembly sits rather than
    /// from the working directory of the test run.
    /// </summary>
    /// <returns>The absolute path of the plugin project directory.</returns>
    private static string PluginSourceDirectory()
        => Path.Combine(RepositoryRoot(), "Jellyfin.Plugin.ServerPairing");

    /// <summary>
    /// The repository root, found by walking up from the directory the test assembly was
    /// loaded from until the solution file appears.
    /// </summary>
    /// <returns>The absolute path of the repository root.</returns>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException(
                $"No directory at or above '{AppContext.BaseDirectory}' holds '{SolutionFileName}', so the source scan has no root to read.")
            : directory.FullName;
    }
}
