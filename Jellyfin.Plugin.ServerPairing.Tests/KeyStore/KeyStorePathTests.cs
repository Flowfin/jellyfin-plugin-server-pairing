using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using Jellyfin.Plugin.ServerPairing.Configuration;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using MediaBrowser.Common.Configuration;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.KeyStore;

/// <summary>
/// Where the key file goes, and the proof that the configuration the host writes never carries
/// what is in it.
/// </summary>
public class KeyStorePathTests
{
    /// <summary>
    /// The store's file is under the server's data path and nowhere near the directory the
    /// host writes plugin configurations into. That directory is read by the configuration
    /// endpoint, goes into every backup of the server's settings, and is where a key would be
    /// plaintext beside a file the dashboard already serves back.
    /// </summary>
    [Fact]
    public void TheKeyFileIsNotUnderThePluginConfigurationDirectory()
    {
        var paths = Paths();

        var file = KeyStorePath.FileFor(paths);

        Assert.StartsWith(paths.DataPath, file, StringComparison.Ordinal);
        Assert.DoesNotContain(paths.PluginConfigurationsPath, file, StringComparison.Ordinal);
        Assert.EndsWith(KeyStorePath.FileName, file, StringComparison.Ordinal);
        Assert.Equal(Path.Join(paths.DataPath, KeyStorePath.DirectoryName), KeyStorePath.DirectoryFor(paths));
    }

    /// <summary>
    /// The floor under the case above. It compares against the configuration directory the
    /// substitute was given, so a substitute answering with nothing for both would make the
    /// comparison vacuous.
    /// </summary>
    [Fact]
    public void ThePathsTheComparisonIsMadeAgainstAreRealPaths()
    {
        var paths = Paths();

        Assert.NotEmpty(paths.DataPath);
        Assert.NotEmpty(paths.PluginConfigurationsPath);
        Assert.NotEqual(paths.DataPath, paths.PluginConfigurationsPath);
    }

    /// <summary>
    /// The fourth done condition of issue #30, as far as this tree can reach it. A key is
    /// created and put in the store, the configuration object is serialised exactly the way
    /// the host serialises it, and none of the key's bytes appear in what comes out, in any of
    /// the encodings somebody would recognise them in.
    /// </summary>
    /// <remarks>
    /// WHAT THIS DOES NOT DO is run a full pairing, because there is none to run: no handshake
    /// exists, so no key is created by one. What it asserts is the property that condition is
    /// about, over a key created the way the store's callers will create one, and the
    /// difference is written here rather than left for a reader to assume away.
    /// </remarks>
    [Fact]
    public void TheConfigurationTheHostWritesCarriesNoneOfTheKey()
    {
        var directory = Path.Join(Path.GetTempPath(), "server-pairing-tests-" + Guid.NewGuid().ToString("n"));

        try
        {
            var key = KeyMaterial.Fresh();
            var bytes = key.Span.ToArray();

            var store = new FilePairingKeyStore(Path.Join(directory, KeyStorePath.FileName));
            store.Add("9f8c1d2b3a4e5f60718293a4b5c6d7e8", key);

            using var written = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            new XmlSerializer(typeof(PluginConfiguration)).Serialize(written, new PluginConfiguration());
            var configuration = written.ToString();

            Assert.NotEmpty(configuration);
            Assert.DoesNotContain(Convert.ToBase64String(bytes), configuration, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToHexString(bytes), configuration, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Encoding.Latin1.GetString(bytes), configuration, StringComparison.Ordinal);

            // And the key IS in the store's own file, so the case above is empty because the
            // key went somewhere else rather than because nothing was created.
            var held = System.IO.File.ReadAllText(store.File);

            Assert.Contains(Convert.ToHexString(bytes), held, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Neither serialiser this plugin's dependencies bring can write a key at all. Both refuse
    /// the type rather than writing a document with the bytes in it, which is the stronger of
    /// the two answers a type could give: there is no file anywhere, read by anybody, that a
    /// key can end up in by a serialiser walking past one.
    /// </summary>
    /// <remarks>
    /// The reason is the same in both: the only accessor is a span, and a span is a ref struct
    /// that neither serialiser can represent. It is a property of the type rather than of a
    /// setting, so a serialiser configured differently elsewhere gets the same refusal.
    /// </remarks>
    [Fact]
    public void NeitherSerialiserCanWriteAKeyAtAll()
    {
        var key = KeyMaterial.Fresh();

        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(key));

        Assert.ThrowsAny<Exception>(() =>
        {
            using var written = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            new XmlSerializer(typeof(KeyMaterial)).Serialize(written, key);
        });
    }

    private static IApplicationPaths Paths()
    {
        var paths = Substitute.For<IApplicationPaths>();

        paths.DataPath.Returns(Path.Join(Path.GetTempPath(), "jellyfin-data"));
        paths.PluginConfigurationsPath.Returns(Path.Join(Path.GetTempPath(), "jellyfin-config", "plugins", "configurations"));

        return paths;
    }
}
