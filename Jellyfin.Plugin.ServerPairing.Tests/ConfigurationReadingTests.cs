using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using Jellyfin.Plugin.ServerPairing.Api;
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
    /// What a configuration file written before a setting existed produces. The serialiser
    /// builds the object through the parameterless constructor and assigns only the members
    /// the document carries, so a missing element keeps what the constructor set rather than
    /// falling to what the type would produce on its own.
    ///
    /// This is measured rather than reasoned about because it was reasoned about wrongly: the
    /// remark on the configuration type asserted the opposite until this was run. It decides
    /// whether an upgrade is quiet or whether every server that has ever saved a settings page
    /// comes up on zeroes, and a count of zero is not a small allowance, it is a plane that
    /// refuses everything.
    /// </summary>
    [Fact]
    public void AMissingElementKeepsTheValueTheConstructorSet()
    {
        var missing = Deserialised("<PluginConfiguration />");
        var constructed = new PluginConfiguration();

        Assert.Equal(constructed.AnInteger, missing.AnInteger);
        Assert.NotEqual(0, missing.AnInteger);

        Assert.Equal(ArrivalLimit.WindowSeconds, missing.PeerPlaneWindowSeconds);
        Assert.Equal(ArrivalLimit.ArrivalsPerPairing, missing.PeerPlaneArrivalsPerPairing);
        Assert.Equal(ArrivalLimit.ArrivalsPerEnrolment, missing.PeerPlaneArrivalsPerEnrolment);
    }

    /// <summary>
    /// A value the file does carry is the one that is used, or the setting configures nothing
    /// and the assertion above is about a document nobody can affect.
    /// </summary>
    [Fact]
    public void AnElementThatIsThereIsTheValueThatIsUsed()
    {
        var configured = Deserialised(
            "<PluginConfiguration><PeerPlaneArrivalsPerPairing>17</PeerPlaneArrivalsPerPairing></PluginConfiguration>");

        Assert.Equal(17, configured.PeerPlaneArrivalsPerPairing);
        Assert.Equal(17, ConfigurationReading.Of(configured).PeerPlaneArrivalsPerPairing);
    }

    /// <summary>
    /// The second done condition of issue #18. The window's lifetime is a setting, it has a
    /// maximum the document carries, and a value above that maximum is refused with the
    /// setting named rather than shortened to the maximum.
    ///
    /// Shortening is the outcome worth refusing rather than the obvious convenience: an
    /// operator who asked for a long window and silently got half an hour has a window that
    /// closes while they are still reading an address out, and nothing tells them why.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(EnrolmentWindow.MaximumLifetimeSeconds + 1)]
    public void AWindowLifetimeOutsideItsBoundsIsRefusedRatherThanShortened(int seconds)
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration { EnrolmentWindowSeconds = seconds });

        var refusal = Assert.Single(reading.Refusals);

        Assert.Equal(nameof(PluginConfiguration.EnrolmentWindowSeconds), refusal.Setting);
        Assert.False(reading.MayPair);
        Assert.NotEqual(EnrolmentWindow.MaximumLifetimeSeconds, seconds);
        Assert.Equal(EnrolmentWindow.LifetimeSeconds, reading.EnrolmentWindowSeconds);
    }

    /// <summary>
    /// A lifetime inside the bounds is the one that is used, and it reaches the window rather
    /// than stopping at the reading. Without this the setting is a number the server refuses
    /// out of range and nothing else.
    /// </summary>
    [Fact]
    public void AWindowLifetimeInsideItsBoundsReachesTheWindow()
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration { EnrolmentWindowSeconds = 90 });

        Assert.Empty(reading.Refusals);
        Assert.Equal(90, reading.EnrolmentWindowSeconds);

        var window = reading.NewEnrolmentWindow(new NobodyIsPaired());
        var opened = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(WindowOpening.Opened, window.Open(Address("https://peer.example"), opened));
        Assert.True(window.IsOpen(Address("https://peer.example"), opened.AddSeconds(89)));
        Assert.False(window.IsOpen(Address("https://peer.example"), opened.AddSeconds(90)));
    }

    /// <summary>
    /// The default lifetime is the one the constant argues, so a fresh installation and the
    /// type cannot drift apart.
    /// </summary>
    [Fact]
    public void TheDefaultLifetimeIsTheOneTheConstantArgues()
    {
        Assert.Equal(EnrolmentWindow.LifetimeSeconds, new PluginConfiguration().EnrolmentWindowSeconds);
        Assert.Equal(
            EnrolmentWindow.LifetimeSeconds,
            Deserialised("<PluginConfiguration />").EnrolmentWindowSeconds);
    }

    /// <summary>
    /// The third done condition of issue #26. The tolerated skew is a setting with a
    /// documented maximum, and a value outside it is refused with the setting named rather
    /// than narrowed to the maximum.
    ///
    /// Narrowing is the outcome worth refusing here rather than the safer-looking one: an
    /// operator who widened the window because one of their servers has no time source, and
    /// whose pairing then fails on a clock anyway, has been given a number they did not
    /// choose and has no reason to look for it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(FreshnessWindow.MaximumWindowSeconds + 1)]
    public void ASkewOutsideItsBoundsIsRefusedRatherThanNarrowed(int seconds)
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration { TimestampWindowSeconds = seconds });

        var refusal = Assert.Single(reading.Refusals);

        Assert.Equal(nameof(PluginConfiguration.TimestampWindowSeconds), refusal.Setting);
        Assert.False(reading.MayPair);
        Assert.Equal(FreshnessWindow.WindowSeconds, reading.TimestampWindowSeconds);
    }

    /// <summary>
    /// A skew inside the bounds reaches the window and decides what it refuses, or the setting
    /// is a number the server refuses out of range and nothing else.
    /// </summary>
    [Fact]
    public void ASkewInsideItsBoundsReachesTheWindow()
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration { TimestampWindowSeconds = 30 });

        Assert.Empty(reading.Refusals);
        Assert.Equal(30, reading.TimestampWindowSeconds);

        var window = reading.NewFreshnessWindow();

        Assert.Equal(30, window.AcceptedSkewSeconds);

        var now = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            FreshnessOutcome.Fresh,
            window.Judge("a", new string('1', 32), Stamp(now.AddSeconds(-30)), now));

        Assert.Equal(
            FreshnessOutcome.OutsideTheWindow,
            window.Judge("a", new string('2', 32), Stamp(now.AddSeconds(-31)), now));
    }

    /// <summary>
    /// The default skew is the one the constant argues, so a fresh installation and the type
    /// cannot drift apart.
    /// </summary>
    [Fact]
    public void TheDefaultSkewIsTheOneTheConstantArgues()
    {
        Assert.Equal(FreshnessWindow.WindowSeconds, new PluginConfiguration().TimestampWindowSeconds);
        Assert.Equal(
            FreshnessWindow.WindowSeconds,
            Deserialised("<PluginConfiguration />").TimestampWindowSeconds);
    }

    /// <summary>
    /// An allowance outside its bounds is refused with the setting named, and the plane runs
    /// on the allowance a server nobody configured runs on rather than on the boundary the
    /// value crossed. A plane whose limit was refused is not a plane with no limit.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ArrivalLimit.MaximumWindowSeconds + 1)]
    public void AWindowOutsideItsBoundsIsRefusedAndThePlaneRunsOnTheDefault(int seconds)
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration { PeerPlaneWindowSeconds = seconds });

        var refusal = Assert.Single(reading.Refusals);

        Assert.Equal(nameof(PluginConfiguration.PeerPlaneWindowSeconds), refusal.Setting);
        Assert.False(reading.MayPair);
        Assert.Equal(ArrivalLimit.WindowSeconds, reading.PeerPlaneWindowSeconds);
    }

    /// <summary>
    /// The same for the two allowances, and the number in the sentence is the one the plane is
    /// actually running on, because a refusal that names a different number than the one in
    /// force sends an operator looking for a behaviour they do not have.
    /// </summary>
    [Fact]
    public void AnAllowanceOutsideItsBoundsIsRefusedAndNamesWhatThePlaneRunsOn()
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration
        {
            PeerPlaneArrivalsPerPairing = ArrivalLimit.MaximumArrivals + 1,
            PeerPlaneArrivalsPerEnrolment = 0
        });

        Assert.Equal(2, reading.Refusals.Count);
        Assert.Equal(ArrivalLimit.ArrivalsPerPairing, reading.PeerPlaneArrivalsPerPairing);
        Assert.Equal(ArrivalLimit.ArrivalsPerEnrolment, reading.PeerPlaneArrivalsPerEnrolment);

        Assert.All(
            reading.Refusals,
            refusal => Assert.Contains(
                refusal.Setting == nameof(PluginConfiguration.PeerPlaneArrivalsPerPairing)
                    ? ArrivalLimit.ArrivalsPerPairing.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : ArrivalLimit.ArrivalsPerEnrolment.ToString(System.Globalization.CultureInfo.InvariantCulture),
                refusal.Reason,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// The enrolment allowance is the harder of the two, so an operator who raises it above
    /// the other is refused rather than quietly given a plane on which the limit a stranger
    /// reaches is the softer one. Both fall back, because a pair where one half was argued and
    /// the other was not is not a pair anybody argued.
    /// </summary>
    [Fact]
    public void AnEnrolmentAllowanceAboveThePairingAllowanceIsRefusedAndBothFallBack()
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration
        {
            PeerPlaneArrivalsPerPairing = 10,
            PeerPlaneArrivalsPerEnrolment = 11
        });

        var refusal = Assert.Single(reading.Refusals);

        Assert.Equal(nameof(PluginConfiguration.PeerPlaneArrivalsPerEnrolment), refusal.Setting);
        Assert.Contains(nameof(PluginConfiguration.PeerPlaneArrivalsPerPairing), refusal.Reason, StringComparison.Ordinal);

        Assert.Equal(ArrivalLimit.ArrivalsPerPairing, reading.PeerPlaneArrivalsPerPairing);
        Assert.Equal(ArrivalLimit.ArrivalsPerEnrolment, reading.PeerPlaneArrivalsPerEnrolment);
    }

    /// <summary>
    /// Equal allowances are accepted. The rule is that the enrolment allowance is never the
    /// softer one, not that it is strictly harder, and a rule refusing equality would refuse a
    /// server an operator deliberately set flat.
    /// </summary>
    [Fact]
    public void EqualAllowancesAreAccepted()
    {
        var reading = ConfigurationReading.Of(new PluginConfiguration
        {
            PeerPlaneArrivalsPerPairing = 12,
            PeerPlaneArrivalsPerEnrolment = 12
        });

        Assert.Empty(reading.Refusals);
        Assert.Equal(12, reading.PeerPlaneArrivalsPerEnrolment);
    }

    /// <summary>
    /// The limit the plane is given carries the allowances that were read, or the settings
    /// above are three numbers nothing consults.
    /// </summary>
    [Fact]
    public void TheLimitTheReadingBuildsCarriesTheAllowancesThatWereRead()
    {
        var limit = ConfigurationReading.Of(new PluginConfiguration
        {
            PeerPlaneWindowSeconds = 30,
            PeerPlaneArrivalsPerPairing = 9,
            PeerPlaneArrivalsPerEnrolment = 3
        }).NewArrivalLimit();

        Assert.Equal(30, limit.CountedOverSeconds);
        Assert.Equal(9, limit.PerPairing);
        Assert.Equal(3, limit.PerEnrolment);
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
    /// A timestamp in the spelling the protocol fixes, which is seconds since the epoch.
    /// </summary>
    /// <param name="at">The instant.</param>
    /// <returns>The timestamp as it travels.</returns>
    private static string Stamp(DateTimeOffset at)
        => at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// An address, for the cases that need one and are not about parsing it.
    /// </summary>
    /// <param name="value">The address.</param>
    /// <returns>The parsed address.</returns>
    private static PeerAddress Address(string value)
    {
        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse(value, out var address));

        return address!;
    }

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
    /// A server that is paired with nobody, which is what a window is opened against.
    /// </summary>
    private sealed class NobodyIsPaired : IPairedPeers
    {
        /// <inheritdoc />
        public bool HasPairingWith(PeerAddress address) => false;
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
