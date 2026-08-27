using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using Jellyfin.Plugin.ServerPairing.Configuration;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// What the plugin does with the configuration it is handed: which values it accepts, which
/// it refuses with the setting named, and what a refusal leaves the plugin able to do.
///
/// The three conditions issue #50 states beyond the document guard are here. Nothing is
/// clamped, a refusal names its setting, and a configuration file with every element missing
/// produces the safe value for each one.
/// </summary>
public class ConfigurationReadingTests
{
    /// <summary>
    /// An address that parses, in the one spelling the address type produces.
    /// </summary>
    private const string GoodAddress = "https://peer.example";

    /// <summary>
    /// The third done condition, in the direction that costs an operator an evening: a
    /// configuration file that mentions nothing at all. This is what the host's own
    /// serialiser produces for it, rather than what the constructor produces, because those
    /// two are different objects and the file on a server is the first one.
    /// </summary>
    [Fact]
    public void AConfigurationWithEveryElementMissingProducesTheSafeDefaults()
    {
        var configuration = Deserialised("<PluginConfiguration />");

        Assert.False(configuration.AcknowledgeCleartextTransport);
        Assert.Equal(string.Empty, configuration.PeerAddress ?? string.Empty);

        var reading = ConfigurationReading.Of(configuration);

        Assert.Empty(reading.Refusals);
        Assert.True(reading.MayPair);
        Assert.Null(reading.Peer);
        Assert.False(reading.CleartextAcknowledged);
    }

    /// <summary>
    /// The half of that condition the issue names on its own: the cleartext acknowledgement
    /// is one of the safe defaults. A missing element deserialises to the type's own default,
    /// so this asserts against the serialiser rather than against the constructor - a
    /// constructor that set it to true would still leave a file with no element reading false,
    /// and the reverse is the case worth refusing.
    /// </summary>
    [Fact]
    public void TheCleartextAcknowledgementIsOffOnAConfigurationThatDoesNotMentionIt()
    {
        Assert.False(Deserialised("<PluginConfiguration />").AcknowledgeCleartextTransport);
        Assert.False(new PluginConfiguration().AcknowledgeCleartextTransport);
    }

    /// <summary>
    /// A value outside its range is refused and the setting is named. Naming it is the whole
    /// point: an operator told that something is wrong retypes the same value.
    /// </summary>
    [Fact]
    public void AnOutOfRangeValueIsRefusedWithTheSettingNamed()
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration { PeerAddress = "https://peer.example/a/path" });

        var refusal = Assert.Single(reading.Refusals);

        Assert.Equal(nameof(PluginConfiguration.PeerAddress), refusal.Setting);
        Assert.Contains(nameof(PluginConfiguration.PeerAddress), refusal.Message, StringComparison.Ordinal);
        Assert.Contains("path", refusal.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing is clamped. The failure issue #50 is written against is an operator setting a
    /// timeout to zero meaning unlimited and getting a working server on a number they did
    /// not choose, so what is asserted here is that the value in the configuration object is
    /// still the one that was set after the reading has refused it.
    /// </summary>
    [Fact]
    public void ARefusedValueIsLeftExactlyWhereTheOperatorPutIt()
    {
        const string Typed = "http://peer.example";
        var configuration = new PluginConfiguration { PeerAddress = Typed };

        var reading = ConfigurationReading.Of(configuration);

        Assert.NotEmpty(reading.Refusals);
        Assert.Equal(Typed, configuration.PeerAddress);
        Assert.Null(reading.Peer);
    }

    /// <summary>
    /// A refused configuration leaves the plugin loaded and not pairing. Loaded is the part
    /// that is easy to lose: a validation step that threw would be one the host's
    /// deserialiser throws out of, and the repair for that is a text editor on the server's
    /// filesystem rather than the page this plugin serves.
    /// </summary>
    [Fact]
    public void ARefusedConfigurationLeavesThePluginLoadedAndNotPairing()
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration { PeerAddress = "https://peer.example?who=me" });

        Assert.False(reading.MayPair);
        Assert.Null(reading.Peer);

        // Nothing threw on the way here, and the type the server discovers the plugin by is
        // still the one it discovers: the page is served from the assembly rather than from
        // anything the configuration decides.
        Assert.Contains(
            "Jellyfin.Plugin.ServerPairing.Configuration.configPage.html",
            typeof(Plugin).Assembly.GetManifestResourceNames());
    }

    /// <summary>
    /// A configuration with nothing wrong on it and no peer entered is a server that pairs
    /// with nobody, which is a different state from a server that is misconfigured. Both are
    /// unable to pair and only one of them is an operator's mistake.
    /// </summary>
    [Fact]
    public void AnEmptyAddressIsAcceptedAndNamesNoPeer()
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration { PeerAddress = string.Empty });

        Assert.Empty(reading.Refusals);
        Assert.True(reading.MayPair);
        Assert.Null(reading.Peer);
    }

    /// <summary>
    /// An address that parses is carried through as an address rather than as the text it
    /// was typed as, so what a caller gets is the canonical spelling two addresses are
    /// compared as.
    /// </summary>
    [Fact]
    public void AnAcceptedAddressIsCarriedThroughCanonicalised()
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration { PeerAddress = "HTTPS://Peer.Example:443/" });

        Assert.Empty(reading.Refusals);
        Assert.NotNull(reading.Peer);
        Assert.Equal(GoodAddress, reading.Peer!.Value);
    }

    /// <summary>
    /// The acknowledgement is what permits cleartext and the only thing that does. Both
    /// directions are asserted, because a setting that permits something the plugin already
    /// permitted configures nothing, which is what the template's four settings do.
    /// </summary>
    [Fact]
    public void TheAcknowledgementIsWhatPermitsACleartextAddress()
    {
        var refused = ConfigurationReading.Of(new PluginConfiguration
        {
            PeerAddress = "http://peer.example",
            AcknowledgeCleartextTransport = false
        });

        var accepted = ConfigurationReading.Of(new PluginConfiguration
        {
            PeerAddress = "http://peer.example",
            AcknowledgeCleartextTransport = true
        });

        Assert.NotEmpty(refused.Refusals);
        Assert.Null(refused.Peer);

        Assert.Empty(accepted.Refusals);
        Assert.NotNull(accepted.Peer);
        Assert.Equal("http://peer.example", accepted.Peer!.Value);
    }

    /// <summary>
    /// The refusal for a cleartext address names the setting that would permit it and says
    /// what permitting it costs, because an operator who is refused and not told which switch
    /// is theirs to throw goes looking in the wrong place.
    /// </summary>
    [Fact]
    public void TheCleartextRefusalNamesTheAcknowledgementAndWhatItCosts()
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration { PeerAddress = "http://peer.example" });

        var refusal = Assert.Single(reading.Refusals);

        Assert.Contains(
            nameof(PluginConfiguration.AcknowledgeCleartextTransport),
            refusal.Reason,
            StringComparison.Ordinal);
        Assert.Contains("readable", refusal.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The acknowledgement widens the scheme rule and nothing else. A scheme that is neither
    /// of the two is refused whatever the operator acknowledged, so the setting cannot be
    /// read as one that turns address checking off.
    /// </summary>
    [Theory]
    [InlineData("ftp://peer.example")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://peer.example/path")]
    [InlineData("https://user@peer.example")]
    public void TheAcknowledgementWidensTheSchemeRuleAndNothingElse(string address)
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration
        {
            PeerAddress = address,
            AcknowledgeCleartextTransport = true
        });

        Assert.NotEmpty(reading.Refusals);
        Assert.Null(reading.Peer);
    }

    /// <summary>
    /// Every refusal names a setting that is on the configuration object. A refusal naming
    /// something an operator cannot find is worse than one naming nothing, because they go
    /// looking for it.
    /// </summary>
    [Fact]
    public void EveryRefusalNamesASettingThatExists()
    {
        var settings = typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var refusals = EveryRefusalThisTypeCanProduce();

        Assert.NotEmpty(refusals);
        Assert.All(refusals, refusal => Assert.Contains(refusal.Setting, settings));
    }

    /// <summary>
    /// A refusal says which rule refused it rather than reporting one invalid-address answer
    /// for every cause, which is the property the address type's own outcome enumeration
    /// exists for and which a reading that collapsed them would quietly undo.
    ///
    /// Grouped by the rule that refused rather than by the address, because several addresses
    /// are refused by one rule and those are meant to read alike.
    /// </summary>
    [Fact]
    public void EachRuleThatRefusesHasASentenceOfItsOwn()
    {
        var byRule = new Dictionary<PeerAddressOutcome, HashSet<string>>();

        foreach (var address in RefusedAddresses())
        {
            var outcome = PeerAddress.Parse(address, false, out _);

            Assert.NotEqual(PeerAddressOutcome.Accepted, outcome);

            if (!byRule.TryGetValue(outcome, out var reasons))
            {
                reasons = new HashSet<string>(StringComparer.Ordinal);
                byRule[outcome] = reasons;
            }

            reasons.Add(ConfigurationReading.Of(new PluginConfiguration { PeerAddress = address })
                .Refusals.Single().Reason);
        }

        // More than one rule is reached, or the two assertions under this are about one
        // sentence and prove nothing about telling two apart.
        Assert.True(byRule.Count > 1);
        Assert.All(byRule.Values, reasons => Assert.Single(reasons));

        var sentences = byRule.Values.Select(reasons => reasons.Single()).ToArray();

        Assert.Equal(sentences.Length, sentences.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The refusals reach an operator. One line per refused setting, at Error, and the
    /// service returns rather than throwing: a hosted service whose start throws stops the
    /// host, and a bad value in a configuration file taking a whole media server down at boot
    /// is the outcome the refusal is meant to replace.
    /// </summary>
    [Fact]
    public void EveryRefusalIsWrittenToTheLogAtStartupAndTheHostIsLeftRunning()
    {
        var log = new CapturedLog();
        var reading = ConfigurationReading.Of(new PluginConfiguration { PeerAddress = "https://peer.example/path" });

        var startup = new ConfigurationAtStartup(reading, log);

        Assert.True(startup.StartAsync(CancellationToken.None).IsCompletedSuccessfully);

        var errors = log.Entries.Where(entry => entry.Level == LogLevel.Error).ToArray();

        Assert.Equal(reading.Refusals.Count, errors.Length);
        Assert.All(errors, entry => Assert.Contains(nameof(PluginConfiguration.PeerAddress), entry.Message, StringComparison.Ordinal));
    }

    /// <summary>
    /// A configuration with nothing wrong on it writes nothing. Silence is the ordinary case
    /// and a line for it is a line an operator learns to skip.
    /// </summary>
    [Fact]
    public void AnAcceptedConfigurationWritesNothingAtStartup()
    {
        var log = new CapturedLog();

        new ConfigurationAtStartup(ConfigurationReading.Of(new PluginConfiguration { PeerAddress = GoodAddress }), log)
            .StartAsync(CancellationToken.None);

        Assert.Empty(log.Entries);
    }

    /// <summary>
    /// A server running cleartext says so on every start. The setting is ticked once and read
    /// months later, and an operator reading a log about a pairing that leaks is owed the
    /// line naming the setting that made it cleartext.
    /// </summary>
    [Fact]
    public void ACleartextServerSaysSoOnEveryStart()
    {
        var log = new CapturedLog();
        var reading = ConfigurationReading.Of(new PluginConfiguration
        {
            PeerAddress = "http://peer.example",
            AcknowledgeCleartextTransport = true
        });

        new ConfigurationAtStartup(reading, log).StartAsync(CancellationToken.None);

        var warning = Assert.Single(log.Entries, entry => entry.Level == LogLevel.Warning);

        Assert.Contains(nameof(PluginConfiguration.AcknowledgeCleartextTransport), warning.Message, StringComparison.Ordinal);
        Assert.Contains("readable", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing to read is not a thing to read. A null configuration is a caller's mistake
    /// rather than a server with no settings, and the second is what an empty object is.
    /// </summary>
    [Fact]
    public void AMissingConfigurationObjectIsACallerError()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigurationReading.Of(null!));
    }

    /// <summary>
    /// Addresses that are refused, spread across the rules that refuse them, so the
    /// assertions above are over a population rather than over one case.
    /// </summary>
    /// <returns>One address per rule, and a second for one rule so that grouping is
    /// exercised rather than assumed.</returns>
    private static string[] RefusedAddresses() => new[]
    {
        new string('h', 300),
        "not an address",
        "https://[not-a-literal",
        "ftp://peer.example",
        "http://peer.example",
        "https://user:pass@peer.example",
        "https://peer..example",
        "https://peer.example/path",
        "https://peer.example?query=1",
        "https://peer.example#fragment",
    };

    /// <summary>
    /// The refusals those addresses produce.
    /// </summary>
    /// <returns>One refusal per address, since each of them is refused by exactly one rule.</returns>
    private static SettingRefusal[] EveryRefusalThisTypeCanProduce()
        => RefusedAddresses()
            .SelectMany(address => ConfigurationReading.Of(new PluginConfiguration { PeerAddress = address }).Refusals)
            .ToArray();

    /// <summary>
    /// The configuration as the host produces it from a file, rather than as the constructor
    /// produces it. The host serves this type through its own configuration endpoint and
    /// writes it back with the XML serialiser, so this is the shape a running server has.
    /// </summary>
    /// <param name="document">The file's contents.</param>
    /// <returns>The deserialised configuration.</returns>
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
    /// A logger that keeps what it was given, so an assertion is about the line an operator
    /// reads rather than about a call having happened.
    /// </summary>
    private sealed class CapturedLog : ILogger<ConfigurationAtStartup>
    {
        /// <summary>
        /// Gets what was written, in the order it was written.
        /// </summary>
        public List<Entry> Entries { get; } = new List<Entry>();

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Entries.Add(new Entry(logLevel, formatter(state, exception)));
        }

        /// <summary>
        /// One line that was written.
        /// </summary>
        /// <param name="Level">How loud it was.</param>
        /// <param name="Message">What it said, after the placeholders were filled.</param>
        internal sealed record Entry(LogLevel Level, string Message);
    }
}
