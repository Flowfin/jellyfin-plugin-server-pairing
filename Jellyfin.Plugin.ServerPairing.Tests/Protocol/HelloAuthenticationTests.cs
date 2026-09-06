using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// Nothing authenticates a <c>hello</c>. <c>docs/protocol.md</c> said one was signed with the
/// private half of the key it offers while <c>docs/crypto.md</c> pinned no primitive that
/// could produce such a signature, and the first message of every pairing rested on a
/// construction neither document had chosen. Issue #364 is where that was found and where the
/// answer was taken: no primitive is added, and what carries the weight is the enrolment
/// window, the comparison the two operators perform, and the first message after
/// <c>hello</c>, which is signed with a key derived from an agreement only the holder of the
/// private half can compute.
/// <para>
/// This holds that answer in two directions. Over the plugin source it refuses the asymmetric
/// signature primitives the answer rules out, so the construction that was written down and
/// never chosen cannot arrive quietly in code. Over the two documents it requires them to
/// carry the same answer and the same statement of what it costs, so one of them cannot drift
/// back to the sentence the other could not supply.
/// </para>
/// <para>
/// What it cannot do is read a type or judge a meaning. The source half matches names, so a
/// primitive reached through a variable typed elsewhere and named nothing like itself walks
/// through it, and so does one assembled from parts. The document half matches sentences, so a
/// document keeping both sentences and contradicting them in a third passes. Both are floors,
/// neither is narrowed by any assertion here, and both are stated in the same words in the
/// documents they read.
/// </para>
/// <para>
/// The scan reads the plugin project and not the suite. The fixtures below are the lines the
/// guard has to refuse, held as text in this file, so a scan covering the suite would refuse
/// the fixtures that prove the guard works.
/// </para>
/// </summary>
public class HelloAuthenticationTests
{
    /// <summary>
    /// The file that marks the repository root. It is tracked and it is at the top of the
    /// tree, so a walk upwards from the build output finds it on any machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The answer, in the words both documents carry. A document that stops carrying it has
    /// either dropped the answer or replaced it, and either way the two no longer agree.
    /// </summary>
    private const string TheAnswer = "Nothing authenticates a `hello`.";

    /// <summary>
    /// What the answer costs, in the words both documents carry. It is asserted separately
    /// from the answer because it is the half an edit tidies away first: a document can go on
    /// saying a <c>hello</c> is unauthenticated while losing the sentence that says what an
    /// unauthenticated <c>hello</c> lets anybody do.
    /// </summary>
    private const string TheCost = "public key of their own choosing";

    /// <summary>
    /// The asymmetric signature primitives this answer rules out. Every one of them is in the
    /// base class library, so none is refused here for being unavailable; they are refused
    /// because <c>docs/crypto.md</c> pins the primitives this plugin uses and none of these is
    /// among them.
    /// </summary>
    private static readonly string[] SignaturePrimitives =
    {
        "ECDsa",
        "ECDsaCng",
        "ECDsaOpenSsl",
        "DSA",
        "DSACng",
        "RSA",
        "RSACng",
        "RSAOpenSsl",
        "SignData",
        "SignHash",
        "TrySignData",
        "TrySignHash",
        "VerifyData",
        "VerifyHash",
    };

    /// <summary>
    /// The documents that have to agree, inside the documents directory.
    /// </summary>
    private static readonly string[] Documents =
    {
        "protocol.md",
        "crypto.md",
    };

    /// <summary>
    /// Nothing in the plugin signs with an asymmetric key. This reads the tree as it is today,
    /// so it is a statement about the tree on the day it runs. What it says about the guard
    /// itself is nothing; the fixtures below are what say that.
    /// </summary>
    [Fact]
    public void NoPluginSourceFileNamesASignaturePrimitive()
    {
        var offences = new List<string>();

        foreach (var file in SourceFiles(PluginSourceDirectory()))
        {
            offences.AddRange(Offences(Path.GetFileName(file), File.ReadAllText(file)));
        }

        Assert.Equal(Array.Empty<string>(), offences.OrderBy(o => o, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The guard refuses the construction the answer rules out, in the spellings somebody
    /// reaches for when they set out to sign a <c>hello</c>.
    /// </summary>
    /// <param name="line">A line the guard has to refuse.</param>
    [Theory]
    [InlineData("using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);")]
    [InlineData("return signer.SignData(bytes, HashAlgorithmName.SHA256);")]
    [InlineData("return signer.VerifyData(bytes, offered, HashAlgorithmName.SHA256);")]
    [InlineData("using var pair = RSA.Create();")]
    [InlineData("return pair.SignHash(digest);")]
    public void TheGuardRefusesTheConstructionTheAnswerRulesOut(string line)
    {
        Assert.NotEmpty(Offences("fixture.cs", line));
    }

    /// <summary>
    /// The near miss worth spending the effort on. Everything this plugin already does with a
    /// key is one word away from the thing above: the agreement is made with a type whose name
    /// starts the same way, the tag is produced by a method called <c>Sign</c>, and the length
    /// of a tag is a constant with <c>Signature</c> in its name. A guard that refuses those is
    /// a guard somebody turns off.
    /// </summary>
    /// <param name="line">A line the guard has to allow.</param>
    [Theory]
    [InlineData("using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);")]
    [InlineData("return Convert.ToBase64String(HMACSHA256.HashData(key, CanonicalForm.ForRequest(request)));")]
    [InlineData("public static string Sign(PairingRequest request, ReadOnlySpan<byte> key)")]
    [InlineData("public const int SignatureLength = 32;")]
    [InlineData("var outcome = _authenticator.Verify(request, presentedSignature, at);")]
    [InlineData("var agreement = local.DeriveRawSecretAgreement(peer);")]
    public void TheGuardAllowsWhatThisPluginAlreadyDoes(string line)
    {
        Assert.Empty(Offences("fixture.cs", line));
    }

    /// <summary>
    /// A primitive named inside a string literal or a comment is text rather than code, and
    /// refusing it would make the documents' own argument unwritable in the source.
    /// </summary>
    /// <param name="line">A line the guard has to allow.</param>
    [Theory]
    [InlineData("// ECDsa is refused here, and docs/crypto.md says why")]
    [InlineData("var reason = \"ECDsa signs nothing this plugin sends\";")]
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
        var offences = Offences("Hello.cs", "ok();\nvar signer = ECDsa.Create();\n");

        Assert.Single(offences);
        Assert.Equal("Hello.cs:2: ECDsa", offences[0]);
    }

    /// <summary>
    /// Both documents carry the answer, in the same words. The whole of issue #364 is that
    /// they disagreed, so a guard holding one of them and not the other would hold nothing.
    /// </summary>
    /// <param name="document">The document to read, inside the documents directory.</param>
    [Theory]
    [InlineData("protocol.md")]
    [InlineData("crypto.md")]
    public void EachDocumentCarriesTheAnswer(string document)
    {
        Assert.Contains(TheAnswer, Document(document), StringComparison.Ordinal);
    }

    /// <summary>
    /// Both documents carry what the answer costs. An unauthenticated <c>hello</c> lets
    /// anybody who can reach the endpoint put a key of their own choosing in front of an
    /// operator, and that admission survives every edit rather than being tidied into the
    /// answer above.
    /// </summary>
    /// <param name="document">The document to read, inside the documents directory.</param>
    [Theory]
    [InlineData("protocol.md")]
    [InlineData("crypto.md")]
    public void EachDocumentCarriesWhatTheAnswerCosts(string document)
    {
        Assert.Contains(TheCost, Document(document), StringComparison.Ordinal);
    }

    /// <summary>
    /// The scan reads a real set of files. The first assertion above passes trivially over an
    /// empty set, which is what happens the day somebody moves or renames a project.
    /// </summary>
    [Fact]
    public void TheScanReadsRealFiles()
    {
        var files = SourceFiles(PluginSourceDirectory());

        Assert.NotEmpty(files);
        Assert.Contains(files, f => Path.GetFileName(f) == "RequestAuthenticator.cs");
    }

    /// <summary>
    /// The vocabularies are not empty, so an edit that empties one turns the guard off in a
    /// way that would otherwise stay green.
    /// </summary>
    [Fact]
    public void EveryVocabularyHasEntries()
    {
        Assert.NotEmpty(SignaturePrimitives);
        Assert.NotEmpty(Documents);
        Assert.NotEmpty(TheAnswer);
        Assert.NotEmpty(TheCost);
    }

    /// <summary>
    /// Every naming of a signature primitive on a line, as the name that made it one.
    /// </summary>
    /// <param name="fileName">The name to report an offence against.</param>
    /// <param name="text">The source text to read.</param>
    /// <returns>One entry per offence, as file, line number and the name.</returns>
    private static string[] Offences(string fileName, string text)
    {
        var found = new List<string>();
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = WithoutTextAndComments(lines[i]);

            foreach (var name in Names(line).Where(n => SignaturePrimitives.Contains(n, StringComparer.Ordinal)))
            {
                found.Add(fileName + ":" + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + name);
            }
        }

        return found.OrderBy(f => f, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// The line with string literals and the trailing line comment blanked out, so that the
    /// name search reads code and not prose.
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
    /// Every name on a line, as the parts a qualified name is written in. A name is matched
    /// whole rather than as a substring, so <c>ECDiffieHellman</c> is not <c>ECDsa</c> and
    /// <c>SignatureLength</c> is not <c>SignData</c>.
    /// </summary>
    /// <param name="line">The line to read, already reduced to code.</param>
    /// <returns>The names found, in the order they appear.</returns>
    private static List<string> Names(string line)
    {
        var names = new List<string>();
        var current = string.Empty;

        foreach (var c in line)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                current += c;
                continue;
            }

            if (current.Length > 0)
            {
                names.Add(current);
                current = string.Empty;
            }
        }

        if (current.Length > 0)
        {
            names.Add(current);
        }

        return names;
    }

    /// <summary>
    /// The text of one of the documents this answer lives in.
    /// </summary>
    /// <param name="fileName">The file name, inside the documents directory.</param>
    /// <returns>The document text.</returns>
    private static string Document(string fileName)
        => File.ReadAllText(Path.Join(RepositoryRoot(), "docs", fileName));

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
        => Path.Join(RepositoryRoot(), "Jellyfin.Plugin.ServerPairing");

    /// <summary>
    /// The repository root, found by walking up from the directory the test assembly was
    /// loaded from until the solution file appears.
    /// </summary>
    /// <returns>The absolute path of the repository root.</returns>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Join(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException(
                $"No directory at or above '{AppContext.BaseDirectory}' holds '{SolutionFileName}', so the source scan has no root to read.")
            : directory.FullName;
    }
}
