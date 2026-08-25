using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.ServerPairing.Configuration;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// Refuses key material anywhere on the object the host serialises to disk.
///
/// The host writes a plugin's configuration object out with the XML serialiser and
/// serves it back to the dashboard, so anything reachable from that object is plaintext
/// on the filesystem, in every backup, and readable by anyone who can open the plugin's
/// settings page. The store that holds a pairing key is a separate thing for that reason.
///
/// This walks the compiled type graph rather than the source text, so it sees what the
/// serialiser will see rather than how a declaration was spelt, and it walks inherited
/// members as well as declared ones, because a key hidden on a base class is written out
/// exactly like one hidden on the leaf.
/// </summary>
public class ConfigurationKeyMaterialTests
{
    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// A member whose type is a run of bytes, in any of the shapes this plugin passes a
    /// pairing key around in. The key type arrived with issue #30 and is named beside
    /// these in <see cref="CarriesKeyMaterial"/>; the runs of bytes stay, because a key
    /// still crosses this plugin as <c>byte[]</c>, <c>ReadOnlyMemory&lt;byte&gt;</c> and
    /// <c>ReadOnlySpan&lt;byte&gt;</c> wherever the cryptography consumes one.
    /// </summary>
    private static bool IsARunOfBytes(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType() == typeof(byte);
        }

        if (type.IsGenericType)
        {
            return type.GetGenericArguments().Length == 1
                && type.GetGenericArguments()[0] == typeof(byte);
        }

        return false;
    }

    /// <summary>
    /// A member whose type comes from the cryptography namespace holds or produces key
    /// material whatever it is called, so it is refused by where it lives rather than by
    /// a list of class names that a new algorithm walks past.
    /// </summary>
    private static bool IsACryptographicType(Type type) =>
        type.Namespace is not null
        && type.Namespace.StartsWith("System.Security.Cryptography", StringComparison.Ordinal);

    /// <summary>
    /// The type key material travels in. It is refused by name, which is the whole reason
    /// issue #30 gave key material a type of its own: a run of bytes is refused by shape
    /// and a shape has to be guessed at, while a named type cannot be reached from the
    /// configuration by any spelling.
    /// </summary>
    private static bool IsTheKeyType(Type type) =>
        type == typeof(KeyMaterial) || type == typeof(PairingKeys);

    private static bool CarriesKeyMaterial(Type type) =>
        IsARunOfBytes(type) || IsACryptographicType(type) || IsTheKeyType(type);

    /// <summary>
    /// The first done condition of issue #30. Every member the serialiser can reach from
    /// the configuration object, declared or inherited, at any depth.
    /// </summary>
    [Fact]
    public void NoMemberReachableFromThePluginConfigurationCanHoldKeyMaterial()
    {
        var offenders = ReachableMembers()
            .Where(member => CarriesKeyMaterial(member.Type))
            .Select(member => member.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), offenders);
    }

    /// <summary>
    /// The floor under the assertion above. A walk that reached nothing prints the same
    /// empty result as a configuration with nothing wrong on it, and the difference is
    /// the whole value of the guard.
    ///
    /// The expected set is read straight off the type rather than written out here, so it
    /// follows the settings issue #50 replaces the template's four with rather than
    /// reddening on the day they change.
    /// </summary>
    [Fact]
    public void TheWalkReachesEverySettingOnTheConfiguration()
    {
        var walked = ReachableMembers()
            .Select(member => member.Path)
            .ToHashSet(StringComparer.Ordinal);

        var settings = typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => string.Concat(nameof(PluginConfiguration), ".", property.Name))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(settings);
        Assert.Equal(Array.Empty<string>(), settings.Where(s => !walked.Contains(s)).ToArray());
    }

    /// <summary>
    /// The second floor. The assertion above is empty either because nothing on the
    /// configuration carries key material or because the decision that refuses it stopped
    /// refusing anything, and those two are indistinguishable from the result.
    /// </summary>
    [Fact]
    public void EveryShapeAKeyIsPassedAroundInIsRefused()
    {
        Assert.True(CarriesKeyMaterial(typeof(byte[])));
        Assert.True(CarriesKeyMaterial(typeof(ReadOnlyMemory<byte>)));
        Assert.True(CarriesKeyMaterial(typeof(ReadOnlySpan<byte>)));
        Assert.True(CarriesKeyMaterial(typeof(Memory<byte>)));
        Assert.True(CarriesKeyMaterial(typeof(IReadOnlyList<byte>)));
        Assert.True(CarriesKeyMaterial(typeof(List<byte>)));
        Assert.True(CarriesKeyMaterial(typeof(System.Security.Cryptography.HMACSHA256)));
        Assert.True(CarriesKeyMaterial(typeof(KeyMaterial)));
        Assert.True(CarriesKeyMaterial(typeof(PairingKeys)));

        Assert.False(CarriesKeyMaterial(typeof(int)));
        Assert.False(CarriesKeyMaterial(typeof(string)));
        Assert.False(CarriesKeyMaterial(typeof(SomeOptions)));
    }

    /// <summary>
    /// Every instance member reachable from the configuration object, with the path a
    /// reader needs to find it. A type is visited once, so a graph that refers back to
    /// itself terminates.
    /// </summary>
    private static List<(string Path, Type Type)> ReachableMembers()
    {
        var found = new List<(string Path, Type Type)>();
        var visited = new HashSet<Type>();

        Visit(typeof(PluginConfiguration), typeof(PluginConfiguration).Name);
        return found;

        void Visit(Type type, string path)
        {
            if (!visited.Add(type))
            {
                return;
            }

            var members = type.GetProperties(Members)
                .Select(property => (property.Name, Type: property.PropertyType))
                .Concat(type.GetFields(Members)
                    .Where(field => !field.IsLiteral)
                    .Select(field => (field.Name, Type: field.FieldType)));

            foreach (var (name, memberType) in members)
            {
                var memberPath = string.Concat(path, ".", name);

                // Every type the member's own type contains counts as reached, not only the
                // declared one, for the same reason Inside is transitive: a member declared
                // as a list of keys is a key on the configuration, and recording only the
                // list walked past it.
                foreach (var reached in Inside(memberType))
                {
                    found.Add((memberPath, reached));
                }

                foreach (var next in Inside(memberType).Where(WorthWalking))
                {
                    Visit(next, memberPath);
                }
            }
        }
    }

    /// <summary>
    /// The member's own type and everything it contains, to the bottom.
    ///
    /// TRANSITIVE, AND IT WAS NOT. This unwrapped one level, so a member declared as a
    /// list of lists of something reached the inner list and stopped there. Found while
    /// building the same walk over the endpoints for issue #32, by planting an endpoint
    /// answering with a task of a list of a type holding a key and watching that guard
    /// stay green.
    /// </summary>
    private static IEnumerable<Type> Inside(Type type)
    {
        yield return type;

        if (type.IsArray && type.GetElementType() is { } element)
        {
            foreach (var inside in Inside(element))
            {
                yield return inside;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var inside in Inside(argument))
                {
                    yield return inside;
                }
            }
        }
    }

    /// <summary>
    /// Where the walk stops. A framework type is not somewhere this repository can put a
    /// key, and walking into one reaches the whole base class library.
    /// </summary>
    private static bool WorthWalking(Type type) =>
        !type.IsPrimitive
        && !type.IsEnum
        && !type.IsPointer
        && type != typeof(string)
        && type != typeof(object)
        && !CarriesKeyMaterial(type)
        && type.Namespace is not null
        && !type.Namespace.StartsWith("System", StringComparison.Ordinal);
}
