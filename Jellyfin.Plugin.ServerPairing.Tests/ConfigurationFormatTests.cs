using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using Jellyfin.Plugin.ServerPairing.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// The plugin configuration's format number: that it is on the file, that it is stamped by the
/// write rather than by the constructor, that an older one is walked up a rung at a time, and
/// that one a newer build wrote is refused in both directions.
///
/// This is the configuration half of issue #55's first done condition. The key store half is
/// <c>StoreFormatTests</c>, and the two are separate because the two files are written by
/// different parties: the store writes its own bytes and stamps as it writes them, while this
/// file is written by the host out of <c>PluginConfiguration</c>.
///
/// The case that carries the most here is the one about a fresh configuration. A fresh
/// installation and a file written before the number existed deserialise to the same object, so
/// both walk the same ladder, and a rung that derives a new setting from an old one would move
/// this build's own defaults if it were written carelessly.
/// </summary>
public class ConfigurationFormatTests
{
    /// <summary>
    /// Every format below the current one, which is what a ladder has to have a rung for.
    /// </summary>
    /// <returns>One row per format that can be carried up.</returns>
    public static IEnumerable<object[]> FormatsBelowTheCurrentOne()
        => Enumerable
            .Range(ConfigurationFormat.Unversioned, ConfigurationFormat.Current - ConfigurationFormat.Unversioned)
            .Select(format => new object[] { format });

    /// <summary>
    /// The floor under everything below. A ladder whose two ends are the same number has no
    /// rung, so every case about walking one would be walking nothing, and
    /// <see cref="FormatsBelowTheCurrentOne"/> would hand out an empty set that a theory reads
    /// as a pass.
    /// </summary>
    [Fact]
    public void ThereIsAtLeastOneRungToWalk()
    {
        Assert.True(ConfigurationFormat.Current > ConfigurationFormat.Unversioned);
        Assert.NotEmpty(FormatsBelowTheCurrentOne());
    }

    /// <summary>
    /// What a configuration file that mentions no format number deserialises to. This is the
    /// reason the constructor does not set the current format: the value it sets is also the
    /// value every file written before the number existed reads as, and those two facts stop
    /// being the same number the day the current format moves.
    /// </summary>
    [Fact]
    public void AConfigurationCarryingNoNumberIsInTheFormatFromBeforeTheNumberExisted()
    {
        var missing = Deserialised("<PluginConfiguration />");

        Assert.Equal(ConfigurationFormat.Unversioned, missing.FormatVersion);
        Assert.Equal(new PluginConfiguration().FormatVersion, missing.FormatVersion);
    }

    /// <summary>
    /// A number the file does carry is the number that is read, or the case above is about a
    /// member nothing can put a value into.
    /// </summary>
    [Fact]
    public void ANumberTheFileCarriesIsTheNumberThatIsRead()
    {
        var declared = Deserialised(
            "<PluginConfiguration><FormatVersion>7</FormatVersion></PluginConfiguration>");

        Assert.Equal(7, declared.FormatVersion);
        Assert.Equal(7, ConfigurationReading.Of(declared).FormatVersion);
    }

    /// <summary>
    /// Walking the ladder ends at the current format, whatever rung it started on.
    /// </summary>
    /// <param name="format">The format the configuration declares.</param>
    [Theory]
    [MemberData(nameof(FormatsBelowTheCurrentOne))]
    public void CarryingUpEndsAtTheCurrentFormat(int format)
    {
        var configuration = new PluginConfiguration { FormatVersion = format };

        ConfigurationFormat.CarryUp(configuration);

        Assert.Equal(ConfigurationFormat.Current, configuration.FormatVersion);
    }

    /// <summary>
    /// Every format below the current one has a rung away from it. This is what a build that
    /// raises the current format without writing the migration meets: the ladder's default arm
    /// fails rather than leaving the configuration where it was and stamping the new number over
    /// it, which would be a file claiming a shape it is not in.
    /// </summary>
    /// <param name="format">The format the configuration declares.</param>
    [Theory]
    [MemberData(nameof(FormatsBelowTheCurrentOne))]
    public void EveryFormatBelowTheCurrentOneHasARungAwayFromIt(int format)
    {
        var configuration = new PluginConfiguration { FormatVersion = format };

        var fault = Record.Exception(() => ConfigurationFormat.CarryUp(configuration));

        Assert.Null(fault);
    }

    /// <summary>
    /// The rule every rung written from here on has to hold. A fresh installation constructs the
    /// configuration at the unversioned format and is carried up the same ladder an old file is,
    /// so a rung that derives a new setting from an old one moves this build's own defaults
    /// unless it is written not to.
    ///
    /// Compared setting by setting through reflection rather than field by field, so a setting
    /// added later is covered on the day it is added rather than on the day somebody remembers
    /// this case.
    /// </summary>
    [Fact]
    public void CarryingAFreshConfigurationUpMovesNoSetting()
    {
        var carried = new PluginConfiguration();
        var untouched = new PluginConfiguration();

        ConfigurationFormat.CarryUp(carried);

        var settings = typeof(PluginConfiguration)
            .GetProperties()
            .Where(property => property.CanRead && property.CanWrite)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => !string.Equals(property.Name, nameof(PluginConfiguration.FormatVersion), StringComparison.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(settings);

        var moved = settings
            .Where(property => !Equals(property.GetValue(carried), property.GetValue(untouched)))
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), moved);
    }

    /// <summary>
    /// A configuration a newer build wrote is not carried up. The host has already deserialised
    /// it into this build's type by the time anything here sees it, so every member that format
    /// added is gone, and walking a ladder over what is left would produce a configuration
    /// claiming to be in a shape nobody wrote.
    /// </summary>
    [Fact]
    public void AConfigurationFromANewerBuildIsRefusedRatherThanCarriedUp()
    {
        var configuration = new PluginConfiguration { FormatVersion = ConfigurationFormat.Current + 1 };

        var refused = Assert.Throws<ConfigurationFormatRefusedException>(
            () => ConfigurationFormat.CarryUp(configuration));

        Assert.Equal(ConfigurationFormat.Current + 1, refused.Found);
        Assert.Equal(ConfigurationFormat.Current, refused.Understood);
        Assert.Equal(ConfigurationFormat.Current + 1, configuration.FormatVersion);
    }

    /// <summary>
    /// Issue #55's first done condition in the words it uses. The host writes the configuration
    /// file on its own the first time, before an operator has saved anything, and that write
    /// goes through the same method a dashboard save does. So the first file a fresh
    /// installation leaves on disk carries the number rather than acquiring one later.
    /// </summary>
    [Fact]
    public void TheFirstWriteOfAFreshConfigurationCarriesTheCurrentFormat()
    {
        using var host = new HostWritingToATemporaryDirectory();

        var fresh = new PluginConfiguration();
        Assert.Equal(ConfigurationFormat.Unversioned, fresh.FormatVersion);

        host.Plugin.SaveConfiguration(fresh);

        Assert.Equal(ConfigurationFormat.Current, host.Written().FormatVersion);
    }

    /// <summary>
    /// The other direction, and the one the number exists for. An operator installs a newer
    /// plugin, configures it, and rolls the plugin back. Writing this build's reading of that
    /// file back over it would put the truncation on disk, so nothing is written at all.
    ///
    /// Asserting the serialiser was never reached is the half a byte comparison would miss: a
    /// write that happened and then restored the file is not the same as a write that did not
    /// happen, and only one of them survives the process being killed in between.
    /// </summary>
    [Fact]
    public void AConfigurationFromANewerBuildIsNotWrittenAtAll()
    {
        using var host = new HostWritingToATemporaryDirectory();

        var newer = new PluginConfiguration { FormatVersion = ConfigurationFormat.Current + 1 };

        Assert.Throws<ConfigurationFormatRefusedException>(() => host.Plugin.SaveConfiguration(newer));

        Assert.Empty(host.Writes);
    }

    /// <summary>
    /// The read path refuses the same configuration and does not throw doing it. A reading that
    /// threw would be one the host's load path throws out of, which takes the plugin off the
    /// server, and the repair for that is a text editor on the server's filesystem. So the
    /// plugin stays loaded, names the member, and does not pair.
    /// </summary>
    [Fact]
    public void TheReadingRefusesAConfigurationFromANewerBuildAndTheServerDoesNotPair()
    {
        var reading = ConfigurationReading.Of(
            new PluginConfiguration { FormatVersion = ConfigurationFormat.Current + 1 });

        var refusal = Assert.Single(reading.Refusals);

        Assert.Equal(nameof(PluginConfiguration.FormatVersion), refusal.Setting);
        Assert.False(reading.MayPair);
    }

    /// <summary>
    /// The near-miss under the case above. The current format and every format below it are
    /// accepted, or the refusal is refusing every configuration rather than the ones from the
    /// future.
    /// </summary>
    /// <param name="format">The format the configuration declares.</param>
    [Theory]
    [MemberData(nameof(FormatsBelowTheCurrentOne))]
    public void AConfigurationAtOrBelowTheCurrentFormatIsNotRefusedForIt(int format)
    {
        Assert.Empty(ConfigurationReading.Of(new PluginConfiguration { FormatVersion = format }).Refusals);
        Assert.Empty(ConfigurationReading.Of(new PluginConfiguration { FormatVersion = ConfigurationFormat.Current }).Refusals);
    }

    /// <summary>
    /// The reading answers the number that was on disk rather than the number the file would be
    /// carried up to. The two are different questions and the refusal above is about the first.
    /// </summary>
    [Fact]
    public void TheReadingAnswersTheFormatThatWasOnDiskRatherThanTheOneItWouldBecome()
    {
        Assert.Equal(
            ConfigurationFormat.Unversioned,
            ConfigurationReading.Of(new PluginConfiguration()).FormatVersion);
    }

    /// <summary>
    /// The fixture rule this issue is really about. A migration is worth something only when it
    /// is run against bytes an older build actually wrote, because a case that builds the old
    /// shape out of the current types is a case about the current types and would go on passing
    /// after the shape it was written for stopped being what an older build produces.
    ///
    /// <c>configuration.format-0.xml</c> was produced by serialising this plugin's
    /// configuration at <c>7ae0f19</c>, the commit before the format number existed, with values
    /// an operator would have set rather than the defaults, so what it proves is that an
    /// operator's own settings survive the walk rather than that two sets of defaults match.
    /// </summary>
    [Fact]
    public void TheCommittedFixtureIsCarriedUpEveryRungToTheCurrentFormat()
    {
        var written = Deserialised(File.ReadAllText(Fixture()));

        Assert.Equal(ConfigurationFormat.Unversioned, written.FormatVersion);

        ConfigurationFormat.CarryUp(written);

        Assert.Equal(ConfigurationFormat.Current, written.FormatVersion);
        Assert.Equal("https://peer.example:8920", written.PeerAddress);
        Assert.False(written.AcknowledgeCleartextTransport);
        Assert.Equal(900, written.EnrolmentWindowSeconds);
        Assert.Equal(120, written.TimestampWindowSeconds);
        Assert.Equal(90, written.PeerPlaneWindowSeconds);
        Assert.Equal(40, written.PeerPlaneArrivalsPerPairing);
        Assert.Equal(4, written.PeerPlaneArrivalsPerEnrolment);
    }

    /// <summary>
    /// The floor under the case above, and it is two separate floors. A fixture that carries the
    /// number would not be a format 0 file at all, and a fixture holding this build's own
    /// defaults would let the assertions above pass without anything having been carried across.
    /// </summary>
    [Fact]
    public void TheFixtureIsAFileFromBeforeTheNumberAndHoldsSettingsNobodyDefaultedTo()
    {
        var bytes = File.ReadAllText(Fixture());
        var defaults = new PluginConfiguration();

        Assert.DoesNotContain(nameof(PluginConfiguration.FormatVersion), bytes, StringComparison.Ordinal);

        var written = Deserialised(bytes);

        Assert.NotEqual(defaults.PeerAddress, written.PeerAddress);
        Assert.NotEqual(defaults.EnrolmentWindowSeconds, written.EnrolmentWindowSeconds);
        Assert.NotEqual(defaults.TimestampWindowSeconds, written.TimestampWindowSeconds);
        Assert.NotEqual(defaults.PeerPlaneWindowSeconds, written.PeerPlaneWindowSeconds);
        Assert.NotEqual(defaults.PeerPlaneArrivalsPerPairing, written.PeerPlaneArrivalsPerPairing);
        Assert.NotEqual(defaults.PeerPlaneArrivalsPerEnrolment, written.PeerPlaneArrivalsPerEnrolment);
    }

    /// <summary>
    /// The committed configuration a previous build wrote.
    /// </summary>
    /// <returns>The path of the fixture.</returns>
    private static string Fixture() => Path.Combine(
        RepositoryRoot(),
        "Jellyfin.Plugin.ServerPairing.Tests",
        "Configuration",
        "Fixtures",
        "configuration.format-0.xml");

    /// <summary>
    /// The repository root, found by walking up from the directory the test assembly was loaded
    /// from until the solution file appears. A deterministic build rewrites the path the
    /// compiler recorded for this file, so a compiler-supplied path is a real directory on one
    /// machine and on no build machine.
    /// </summary>
    /// <returns>The absolute path of the repository root.</returns>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Jellyfin.Plugin.ServerPairing.sln")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException(
                "No directory at or above '" + AppContext.BaseDirectory
                + "' holds the solution file, so the configuration fixture has no root to be read from.")
            : directory.FullName;
    }

    /// <summary>
    /// A configuration deserialised the way the host deserialises one.
    /// </summary>
    /// <param name="document">The XML.</param>
    /// <returns>The object the serialiser produced.</returns>
    private static PluginConfiguration Deserialised(string document)
    {
        using var text = new StringReader(document);

        // The overload taking a reader, with document type definitions off and no resolver,
        // because the analyzers refuse the one taking a TextReader and they are right to: a
        // fixture is a document from outside this process like any other.
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

        return (PluginConfiguration)new XmlSerializer(typeof(PluginConfiguration)).Deserialize(reader)!;
    }

    /// <summary>
    /// A plugin instance with the two host services it needs, writing into a directory this
    /// process owns and remembering what it was handed instead of serialising it.
    /// </summary>
    /// <remarks>
    /// The base class's constructor reads the plugins path and the save path reads the plugin
    /// configurations path, so both are real directories here rather than substituted strings:
    /// the base class calls <c>Directory.CreateDirectory</c> on the second before it serialises.
    /// Nothing here starts a server and nothing needs elevation.
    /// </remarks>
    private sealed class HostWritingToATemporaryDirectory : IDisposable
    {
        private readonly string _root;

        /// <summary>
        /// Initializes a new instance of the <see cref="HostWritingToATemporaryDirectory"/> class.
        /// </summary>
        public HostWritingToATemporaryDirectory()
        {
            _root = Path.Combine(Path.GetTempPath(), "pairing-configuration-format-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "plugins"));
            Directory.CreateDirectory(Path.Combine(_root, "configurations"));

            var paths = Substitute.For<IApplicationPaths>();
            paths.PluginsPath.Returns(Path.Combine(_root, "plugins"));
            paths.PluginConfigurationsPath.Returns(Path.Combine(_root, "configurations"));

            var serialiser = Substitute.For<IXmlSerializer>();
            serialiser
                .When(x => x.SerializeToFile(Arg.Any<object>(), Arg.Any<string>()))
                .Do(call => Writes.Add((PluginConfiguration)call.Arg<object>()));

            Plugin = new Plugin(paths, serialiser);
        }

        /// <summary>
        /// Gets the plugin under test.
        /// </summary>
        public Plugin Plugin { get; }

        /// <summary>
        /// Gets what the host was asked to serialise, in the order it was asked.
        /// </summary>
        public List<PluginConfiguration> Writes { get; } = new List<PluginConfiguration>();

        /// <summary>
        /// The single configuration that was written.
        /// </summary>
        /// <returns>What the serialiser was handed.</returns>
        public PluginConfiguration Written() => Assert.Single(Writes);

        /// <summary>
        /// Removes the directory this instance made.
        /// </summary>
        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
                // A temporary directory that outlives the run costs nothing and is not what
                // this case is about.
            }
        }
    }
}
