using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.KeyStore;

/// <summary>
/// Refuses key material anywhere an endpoint of this plugin can answer with.
/// </summary>
/// <remarks>
/// A key that is only in the store is safe from most of the ways keys leak. A key that has
/// been copied into something an endpoint returns is not, and an endpoint answers an
/// administrator's browser over the same origin as the dashboard.
/// <para>
/// This walks the compiled type graph out of every action method on every controller in the
/// plugin assembly, so it sees what the framework can serialise rather than how a declaration
/// was spelt, and it finds a controller by its attributes rather than by a list of names, so a
/// controller added tomorrow is walked without anybody remembering to add it here.
/// </para>
/// <para>
/// The walk stops at types outside this plugin's own namespace. A framework type is not
/// somewhere this repository can put a key, and walking into one reaches the whole base class
/// library. The bound that matters is written down rather than left implicit: an endpoint
/// returning a framework container of a plugin type IS walked, because the container's type
/// arguments are walked; an endpoint returning a framework type that reaches a key by some
/// route of its own is NOT, and no such route exists to reach one, because the key type is
/// this plugin's.
/// </para>
/// </remarks>
public class EndpointKeyMaterialTests
{
    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private const string OwnNamespace = "Jellyfin.Plugin.ServerPairing";

    /// <summary>
    /// The first done condition of issue #32. Every type an endpoint of this plugin can answer
    /// with, walked to the bottom, with anything that can hold key material refused.
    /// </summary>
    [Fact]
    public void NoTypeAnEndpointReturnsCanReachKeyMaterial()
    {
        var offenders = ReachableFromEveryEndpoint()
            .Where(member => CarriesKeyMaterial(member.Type))
            .Select(member => member.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), offenders);
    }

    /// <summary>
    /// The floor under the assertion above. A walk that found no endpoints prints the same
    /// empty result as a plugin whose endpoints are all clean, and the difference is the whole
    /// value of the guard. The count is not written down here; what is asserted is that the
    /// walk found every path this plugin serves, on both planes.
    /// </summary>
    /// <remarks>
    /// Two derived sets rather than one written down. The specification's six paths are what
    /// the peer plane owes and are read out of <see cref="PeerPlane.PathFor"/>; everything the
    /// host would route is read out of its own action discovery, which is what widens with a
    /// second plane without anybody editing a list here. The two together say that the walk
    /// covers the specification and covers whatever else is served, and the second half is the
    /// one that catches an action reaching the server with no HTTP attribute for this walk to
    /// find it by.
    /// </remarks>
    [Fact]
    public void TheWalkReachesEveryEndpointThisPluginServes()
    {
        var routed = Endpoints()
            .SelectMany(action => action.GetCustomAttributes<HttpMethodAttribute>().Select(http => http.Template))
            .OrderBy(template => template, StringComparer.Ordinal)
            .ToArray();

        var specified = Enum.GetValues<Jellyfin.Plugin.ServerPairing.Protocol.PairingMessage>()
            .Select(message => PeerPlane.PathFor(message).Split('/')[^1])
            .OrderBy(template => template, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(routed);

        foreach (var template in specified)
        {
            Assert.Contains(template, routed, StringComparer.Ordinal);
        }

        Assert.Equal(LastSegmentOfEveryRoutedTemplate(), routed);
    }

    /// <summary>
    /// The last path segment of every template the host would route out of this assembly,
    /// asked of the host's own action discovery rather than of the attributes.
    /// </summary>
    /// <returns>The segments, ordered.</returns>
    private static string[] LastSegmentOfEveryRoutedTemplate()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mvc = services.AddControllers();
        mvc.PartManager.ApplicationParts.Clear();
        mvc.PartManager.ApplicationParts.Add(new AssemblyPart(typeof(PeerPlane).Assembly));

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors
            .Items
            .OfType<ControllerActionDescriptor>()
            .Select(descriptor => (descriptor.AttributeRouteInfo?.Template ?? string.Empty).Split('/')[^1])
            .OrderBy(segment => segment, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The same question asked of what an endpoint ACTUALLY answers with, rather than of what
    /// it declares. Each action is invoked and the object it returned is walked by its runtime
    /// type, which is the half a declared-type walk cannot see.
    /// </summary>
    /// <remarks>
    /// This case exists because the declared half is weak here and saying so is not enough.
    /// Every action on this plugin's one controller returns <see cref="IActionResult"/>, which
    /// is a framework type the walk stops at, so the case above walks two types and finds
    /// nothing to refuse. An action that answered with a key would be refused by this case and
    /// not by that one.
    /// </remarks>
    /// <returns>A task.</returns>
    [Fact]
    public async Task NothingAnEndpointActuallyAnswersWithReachesKeyMaterial()
    {
        var answered = new List<object>();

        foreach (var message in Enum.GetValues<PairingMessage>())
        {
            var controller = Controller(PeerPlane.PathFor(message));

            answered.Add(await Invoke(controller, message).ConfigureAwait(true));
        }

        Assert.Equal(Enum.GetValues<PairingMessage>().Length, answered.Count);

        var offenders = new List<string>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var answer in answered)
        {
            WalkValue(answer, answer.GetType().Name, offenders, visited);
        }

        Assert.Equal(Array.Empty<string>(), offenders.OrderBy(path => path, StringComparer.Ordinal).ToArray());

        // The floor: a walk that visited nothing would print the same empty result.
        Assert.NotEmpty(visited);
    }

    /// <summary>
    /// The second floor. The assertion above is empty either because no endpoint can reach key
    /// material or because the decision that refuses it stopped refusing anything, and those
    /// two are indistinguishable from the result.
    /// </summary>
    [Fact]
    public void EveryShapeAKeyIsPassedAroundInIsRefused()
    {
        Assert.True(CarriesKeyMaterial(typeof(KeyMaterial)));
        Assert.True(CarriesKeyMaterial(typeof(PairingKeys)));
        Assert.True(CarriesKeyMaterial(typeof(byte[])));
        Assert.True(CarriesKeyMaterial(typeof(ReadOnlyMemory<byte>)));
        Assert.True(CarriesKeyMaterial(typeof(System.Security.Cryptography.HMACSHA256)));

        Assert.False(CarriesKeyMaterial(typeof(int)));
        Assert.False(CarriesKeyMaterial(typeof(string)));
        Assert.False(CarriesKeyMaterial(typeof(RefusalCode)));
    }

    private static void WalkValue(object? value, string path, List<string> offenders, HashSet<object> visited)
    {
        if (value is null || !visited.Add(value))
        {
            return;
        }

        var type = value.GetType();

        if (CarriesKeyMaterial(type))
        {
            offenders.Add(path + " : " + type.Name);

            return;
        }

        if (value is System.Collections.IEnumerable sequence and not string)
        {
            var index = 0;

            foreach (var item in sequence)
            {
                WalkValue(item, path + "[" + index++ + "]", offenders, visited);
            }
        }

        // Into this plugin's own types, and into the framework's result types, because an
        // action answers with one of those and what it carries is the plugin's. Everything
        // else is where a walk over live objects reaches the whole runtime.
        var namespaceOf = type.Namespace ?? string.Empty;

        if (!namespaceOf.StartsWith(OwnNamespace, StringComparison.Ordinal)
            && !namespaceOf.StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? held;

            try
            {
                held = property.GetValue(value);
            }
            catch (TargetInvocationException)
            {
                continue;
            }

            WalkValue(held, path + "." + property.Name, offenders, visited);
        }
    }

    private static Task<IActionResult> Invoke(PeerPlaneController controller, PairingMessage message) => message switch
    {
        PairingMessage.Hello => controller.Hello(),
        PairingMessage.Confirm => controller.Confirm(),
        PairingMessage.Rotate => controller.Rotate(),
        PairingMessage.Revoke => controller.Revoke(),
        PairingMessage.Exchange => controller.Exchange(),
        PairingMessage.Unpair => controller.Unpair(),
        _ => throw new ArgumentOutOfRangeException(nameof(message)),
    };

    private static PeerPlaneController Controller(string path)
    {
        var context = new DefaultHttpContext();

        context.Request.Method = PeerPlane.Method;
        context.Request.Path = path;
        context.Request.Body = new MemoryStream(Array.Empty<byte>());

        var feature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestFeature>();

        if (feature is not null)
        {
            feature.RawTarget = path;
        }

        return new PeerPlaneController(new PeerPlane(new RequestAuthenticator(new StoreBackedKeys(new InMemoryPairingKeyStore())), new ArrivalLimit(), new FreshnessWindow()), TimeProvider.System, NullLogger<PeerPlaneController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    /// <summary>
    /// Every action method on every controller in the plugin assembly, found by the attributes
    /// the framework itself routes on.
    /// </summary>
    private static IEnumerable<MethodInfo> Endpoints()
        => typeof(PeerPlane).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any());

    /// <summary>
    /// Every member reachable from what any endpoint returns, with the path a reader needs to
    /// find it. A type is visited once, so a graph that refers back to itself terminates.
    /// </summary>
    private static List<(string Path, Type Type)> ReachableFromEveryEndpoint()
    {
        var found = new List<(string Path, Type Type)>();
        var visited = new HashSet<Type>();

        foreach (var action in Endpoints())
        {
            Record(action.DeclaringType!.Name + "." + action.Name + "()", action.ReturnType);
        }

        return found;

        // Every type a declared one contains counts as reached, not only the declared one.
        // A task of a key and a list of keys are both an endpoint answering with a key, and
        // recording only the outer type would walk past both.
        void Record(string path, Type declared)
        {
            foreach (var type in Inside(declared))
            {
                found.Add((path, type));
            }

            foreach (var type in Inside(declared).Where(WorthWalking))
            {
                Visit(type, path);
            }
        }

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
                Record(string.Concat(path, ".", name), memberType);
            }
        }
    }

    /// <summary>
    /// The member's own type and everything it contains, to the bottom.
    /// </summary>
    /// <remarks>
    /// TRANSITIVE, AND THAT IS THE POINT. A task of a list of views is three framework types
    /// deep before the plugin's own type appears, and a version of this that unwrapped one
    /// level stopped at the list and never saw the view. Found by planting exactly that
    /// endpoint and watching this guard stay green.
    /// </remarks>
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
    /// A member whose type is a run of bytes, in any of the shapes a key crosses this plugin
    /// in.
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
    /// material whatever it is called.
    /// </summary>
    private static bool IsACryptographicType(Type type) =>
        type.Namespace is not null
        && type.Namespace.StartsWith("System.Security.Cryptography", StringComparison.Ordinal);

    private static bool CarriesKeyMaterial(Type type) =>
        type == typeof(KeyMaterial)
        || type == typeof(PairingKeys)
        || IsARunOfBytes(type)
        || IsACryptographicType(type);

    /// <summary>
    /// Where the walk stops. Only this plugin's own types are walked into: a framework type is
    /// not somewhere this repository can put a key, and walking into one reaches the whole
    /// base class library.
    /// </summary>
    private static bool WorthWalking(Type type) =>
        !type.IsPrimitive
        && !type.IsEnum
        && !type.IsPointer
        && !CarriesKeyMaterial(type)
        && type.Namespace is not null
        && type.Namespace.StartsWith(OwnNamespace, StringComparison.Ordinal);
}
