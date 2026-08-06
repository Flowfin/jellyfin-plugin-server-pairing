using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// Holds the plugin assembly to one piece of static state, the plugin instance the base
/// class sets and the configuration page needs.
/// </summary>
public class StaticStateTests
{
    /// <summary>
    /// The compiler-generated backing field behind <c>Plugin.Instance</c>. It is the one
    /// exception, and it is named here rather than pattern-matched so that a second static
    /// cannot arrive under a name that happens to look like it.
    /// </summary>
    private const string PluginInstanceBackingField = "<Instance>k__BackingField";

    /// <summary>
    /// A static holding state is a service a test cannot replace and a second pairing
    /// cannot have its own of. This reads the compiled assembly rather than the source
    /// text, so it sees what the compiler emitted and cannot be fooled by how a
    /// declaration is spelt or wrapped.
    /// </summary>
    [Fact]
    public void ThePluginInstanceIsTheOnlyStaticFieldInTheAssembly()
    {
        var offenders = typeof(Plugin).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .SelectMany(t => t.GetFields(
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly))
            .Where(f => !f.IsLiteral)
            .Where(f => !(f.DeclaringType == typeof(Plugin) && f.Name == PluginInstanceBackingField))
            .Select(f => string.Concat(f.DeclaringType!.FullName, ".", f.Name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), offenders);
    }

    /// <summary>
    /// The exception the test above carves out has to still be there, or the test is
    /// carving out nothing and would pass on an assembly where the instance was removed
    /// and three other statics added.
    /// </summary>
    [Fact]
    public void ThePluginInstanceStaticIsStillThereForTheExceptionToBeAbout()
    {
        var backingField = typeof(Plugin).GetField(
            PluginInstanceBackingField,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        Assert.NotNull(backingField);
    }
}
