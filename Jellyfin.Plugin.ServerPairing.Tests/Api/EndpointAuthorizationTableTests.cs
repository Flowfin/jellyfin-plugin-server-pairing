using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Protocol;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// The endpoint authorization table in <c>docs/endpoints.md</c>, asserted against what the
/// host would serve rather than against what the source looks like.
/// </summary>
/// <remarks>
/// The failure this exists for is a controller method that reaches the server without the
/// authorization the table names. Reading the attributes cannot catch it, because the case
/// that hurts is a method carrying no attribute at all: a public instance method declared on
/// a controller is an action whether or not it says so, and it then inherits whatever the
/// host's default is. So the served side here is produced by the host's own action discovery,
/// through the same <see cref="IActionDescriptorCollectionProvider"/> the server resolves a
/// request with, and the declared side is parsed out of the document.
/// <para>
/// That difference was not academic when this landed. Discovery returned six actions against
/// a document and a suite that both said five, and the sixth was
/// <c>PeerPlaneController.Arriving</c>: routed at <c>/ServerPairing</c>, under no HTTP method
/// constraint, on a class carrying <see cref="AllowAnonymousAttribute"/>. What a request there
/// would have been answered with is not measured by this file and is not claimed anywhere; the
/// discovery is asked in this process rather than on a server.
/// </para>
/// </remarks>
public class EndpointAuthorizationTableTests
{
    /// <summary>
    /// The file that marks the repository root. It is tracked and it is at the top of the
    /// tree, so a walk upwards from the build output finds it on any machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The heading the table sits under. The rows are read from between this heading and the
    /// next one at the same level, so the other tables in that document are not read as
    /// endpoints.
    /// </summary>
    private const string TableHeading = "## The table";

    /// <summary>
    /// The host is asked for no credential of its own. The action resolves to
    /// <see cref="AllowAnonymousAttribute"/> and carries no <see cref="AuthorizeAttribute"/>.
    /// </summary>
    private const string Anonymous = "anonymous";

    /// <summary>
    /// The action carries <see cref="AuthorizeAttribute"/> naming the host's elevation policy.
    /// </summary>
    private const string Elevation = "elevation";

    /// <summary>
    /// The method column of a row the host serves under no method constraint at all, which is
    /// what a controller method carrying no HTTP attribute resolves to.
    /// </summary>
    private const string AnyMethod = "any";

    /// <summary>
    /// The character the document sets a literal cell in.
    /// </summary>
    private const char Backtick = '`';

    /// <summary>
    /// The plane a peer reaches, and the two words that go with it.
    /// </summary>
    private static readonly string[] PeerPlaneRow = { "peer", Anonymous, "the pairing signature" };

    /// <summary>
    /// The plane an administrator reaches, and the two words that go with it. No row carries
    /// it yet; it is here so that the first one is judged rather than believed.
    /// </summary>
    private static readonly string[] AdministrativePlaneRow = { "administrative", Elevation, "the host's elevation policy" };

    /// <summary>
    /// Every endpoint the host would serve out of the plugin assembly, in the terms the
    /// document describes one.
    /// </summary>
    /// <returns>One entry per action, ordered by action name.</returns>
    private static Endpoint[] Served()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mvc = services.AddControllers();
        mvc.PartManager.ApplicationParts.Clear();
        mvc.PartManager.ApplicationParts.Add(new AssemblyPart(typeof(PeerPlaneController).Assembly));

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors
            .Items
            .OfType<ControllerActionDescriptor>()
            .Select(descriptor => new Endpoint(
                descriptor.ControllerTypeInfo.Name + "." + descriptor.MethodInfo.Name,
                MethodOf(descriptor),
                "/" + (descriptor.AttributeRouteInfo?.Template ?? string.Empty),
                HostAuthorizationOf(descriptor)))
            .OrderBy(endpoint => endpoint.Action, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Every row of the table in the document, as the four columns reflection can answer for.
    /// </summary>
    /// <returns>One entry per row, ordered by action name.</returns>
    private static Endpoint[] Declared()
        => Rows()
            .Select(cells => new Endpoint(Bare(cells[0]), Bare(cells[1]), Bare(cells[2]), Bare(cells[4])))
            .OrderBy(endpoint => endpoint.Action, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The document names every endpoint the host serves, with the method, the path and the
    /// authorization each one actually carries. This is the assertion the file exists for, and
    /// it fails in both directions.
    /// </summary>
    [Fact]
    public void TheTableIsWhatTheHostServes()
    {
        Assert.Empty(Offences(Served(), Declared()));
    }

    /// <summary>
    /// The floor under the assertion above. Two empty sequences agree, so a renamed heading, a
    /// changed table shape or an assembly that produced no controller would pass it while
    /// reading nothing. This fixes both sides as non-empty and names what has to be in them.
    /// </summary>
    [Fact]
    public void BothSidesActuallyParse()
    {
        var served = Served();
        var declared = Declared();

        Assert.NotEmpty(served);
        Assert.NotEmpty(declared);
        Assert.Equal(served.Length, declared.Length);

        foreach (var path in Enum.GetValues<PairingMessage>().Select(PeerPlane.PathFor))
        {
            Assert.Contains(served, endpoint => string.Equals(endpoint.Path, path, StringComparison.Ordinal));
            Assert.Contains(declared, endpoint => string.Equals(endpoint.Path, path, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Every row spells a whole plane rather than half of one. The plane, the host
    /// authorization and what decides the request are three words for one decision, and a row
    /// is the only place they could be made to disagree.
    /// </summary>
    [Fact]
    public void EveryRowSpellsOneWholePlane()
    {
        foreach (var cells in Rows())
        {
            var triple = new[] { Bare(cells[3]), Bare(cells[4]), Bare(cells[5]) };

            Assert.True(
                triple.SequenceEqual(PeerPlaneRow) || triple.SequenceEqual(AdministrativePlaneRow),
                "Row '" + Bare(cells[0]) + "' spells " + string.Join(" / ", triple)
                + ", which is neither plane the document defines.");
        }
    }

    /// <summary>
    /// The guard bites on an endpoint the document does not name, which is the case issue #27
    /// is written about: a method added to a controller later, reaching the server with
    /// whatever the host's default is rather than with what the table says. The endpoint used
    /// here is the real one that was found, at the shape it was served at.
    /// </summary>
    [Fact]
    public void AnEndpointAbsentFromTheTableIsAnOffence()
    {
        var declared = Declared();
        var served = declared
            .Append(new Endpoint("PeerPlaneController.Arriving", AnyMethod, "/ServerPairing", Anonymous))
            .ToArray();

        var offences = Offences(served, declared);

        Assert.Single(offences);
        Assert.Contains("PeerPlaneController.Arriving", offences[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard bites in the other direction too. A row left behind by an endpoint that was
    /// removed or renamed is a table describing something no request can reach, which is the
    /// same drift arriving from the other side.
    /// </summary>
    [Fact]
    public void ARowNamingNothingTheHostServesIsAnOffence()
    {
        var served = Served();
        var declared = served
            .Append(new Endpoint("AdminController.ListPairings", "GET", "/ServerPairing/pairings", Elevation))
            .ToArray();

        var offences = Offences(served, declared);

        Assert.Single(offences);
        Assert.Contains("AdminController.ListPairings", offences[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// The three columns reflection answers for are each compared, not only the action name. A
    /// row naming an endpoint correctly and its authorization wrongly is the failure this table
    /// exists to refuse, and it is the one a comparison of names alone walks past.
    /// </summary>
    /// <param name="method">The method the row is made to claim.</param>
    /// <param name="path">The path the row is made to claim.</param>
    /// <param name="authorization">The authorization the row is made to claim.</param>
    [Theory]
    [InlineData("GET", "/ServerPairing/hello", Anonymous)]
    [InlineData("POST", "/ServerPairing/hallo", Anonymous)]
    [InlineData("POST", "/ServerPairing/hello", Elevation)]
    public void AColumnThatDisagreesWithTheActionIsAnOffence(string method, string path, string authorization)
    {
        var served = Served();
        var declared = served
            .Select(endpoint => string.Equals(endpoint.Action, "PeerPlaneController.Hello", StringComparison.Ordinal)
                ? new Endpoint(endpoint.Action, method, path, authorization)
                : endpoint)
            .ToArray();

        var offences = Offences(served, declared);

        Assert.Single(offences);
        Assert.Contains("PeerPlaneController.Hello", offences[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// A near miss that has to stay green, so the four cases above are read as the guard biting
    /// rather than as it refusing whatever it is handed.
    /// </summary>
    [Fact]
    public void TheServedSetComparedAgainstItselfIsClean()
    {
        Assert.Empty(Offences(Served(), Served()));
    }

    /// <summary>
    /// One endpoint, in the four terms the document and the host's discovery can both answer
    /// for.
    /// </summary>
    /// <param name="Action">The declaring type's name and the method's name, joined by a dot.</param>
    /// <param name="Method">The HTTP method, or <c>any</c> where the action constrains none.</param>
    /// <param name="Path">The absolute path the action is routed at.</param>
    /// <param name="HostAuthorization">What the host is asked for: <c>anonymous</c> or <c>elevation</c>.</param>
    private sealed record Endpoint(string Action, string Method, string Path, string HostAuthorization);

    /// <summary>
    /// Where the served set and the declared set disagree, one line per disagreement.
    /// </summary>
    /// <param name="served">What the host would serve.</param>
    /// <param name="declared">What the document names.</param>
    /// <returns>One entry per offence, ordered.</returns>
    private static string[] Offences(IEnumerable<Endpoint> served, IEnumerable<Endpoint> declared)
    {
        ArgumentNullException.ThrowIfNull(served);
        ArgumentNullException.ThrowIfNull(declared);

        var byAction = declared.ToDictionary(endpoint => endpoint.Action, StringComparer.Ordinal);
        var found = new List<string>();

        foreach (var endpoint in served)
        {
            if (!byAction.Remove(endpoint.Action, out var row))
            {
                found.Add(endpoint.Action + ": the host serves it and docs/endpoints.md names no row for it");
                continue;
            }

            if (row != endpoint)
            {
                found.Add(endpoint.Action + ": the row says " + Describe(row) + " and the host serves " + Describe(endpoint));
            }
        }

        found.AddRange(byAction.Keys.Select(
            action => action + ": docs/endpoints.md names it and the host serves nothing by that name"));

        return found.OrderBy(offence => offence, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// An endpoint's three compared columns, for an offence line.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <returns>The method, the path and the authorization.</returns>
    private static string Describe(Endpoint endpoint)
        => endpoint.Method + " " + endpoint.Path + " (" + endpoint.HostAuthorization + ")";

    /// <summary>
    /// The HTTP method an action is constrained to, read from the metadata routing itself
    /// consults. An action constraining none is not an error here: it is a shape that has to be
    /// describable, because it is what a method carrying no HTTP attribute produces.
    /// </summary>
    /// <param name="descriptor">The action.</param>
    /// <returns>The methods, joined, or <c>any</c>.</returns>
    private static string MethodOf(ControllerActionDescriptor descriptor)
    {
        var methods = descriptor.EndpointMetadata
            .OfType<HttpMethodMetadata>()
            .SelectMany(metadata => metadata.HttpMethods)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(method => method, StringComparer.Ordinal)
            .ToArray();

        return methods.Length == 0 ? AnyMethod : string.Join("+", methods);
    }

    /// <summary>
    /// What the host is asked for, read from the metadata the authorization middleware reads.
    /// </summary>
    /// <param name="descriptor">The action.</param>
    /// <returns>The word the table's authorization column carries.</returns>
    /// <exception cref="InvalidOperationException">The action carries a shape the table has no word for.</exception>
    private static string HostAuthorizationOf(ControllerActionDescriptor descriptor)
    {
        var name = descriptor.ControllerTypeInfo.Name + "." + descriptor.MethodInfo.Name;
        var authorize = descriptor.EndpointMetadata.OfType<IAuthorizeData>().ToArray();

        if (authorize.Length == 0)
        {
            return descriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()
                ? Anonymous
                : throw new InvalidOperationException(
                    name + " asks the host for neither an authorization nor anonymity, so it takes the"
                    + " server's default and docs/endpoints.md has no word for what it requires.");
        }

        return Array.TrueForAll(authorize, data => string.Equals(data.Policy, Policies.RequiresElevation, StringComparison.Ordinal))
            ? Elevation
            : throw new InvalidOperationException(
                name + " carries an authorization naming '"
                + string.Join(", ", authorize.Select(data => data.Policy ?? "(no policy)"))
                + "', which is not the host's elevation policy and is not a word this table defines.");
    }

    /// <summary>
    /// The cells of every row of the table, the header and its underline excluded.
    /// </summary>
    /// <returns>One array of six cells per row.</returns>
    /// <exception cref="InvalidOperationException">The section or the table is not where this expects it.</exception>
    private static string[][] Rows()
    {
        var document = File.ReadAllText(Path.Join(RepositoryRoot(), "docs", "endpoints.md"))
            .Replace("\r", string.Empty, StringComparison.Ordinal);

        var start = document.IndexOf(TableHeading + "\n", StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException(
                "docs/endpoints.md carries no '" + TableHeading
                + "' heading, so the endpoint table has no section to be read from.");
        }

        var body = document[(start + TableHeading.Length)..];
        var end = body.IndexOf("\n## ", StringComparison.Ordinal);
        var section = end < 0 ? body : body[..end];

        var rows = section.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|'))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(cells => !cells[0].StartsWith("---", StringComparison.Ordinal))
            .Skip(1)
            .ToArray();

        foreach (var cells in rows.Where(row => row.Length != 6))
        {
            throw new InvalidOperationException(
                "A row of the endpoint table carries " + cells.Length.ToString(CultureInfo.InvariantCulture)
                + " cells rather than six: " + string.Join(" | ", cells));
        }

        return rows;
    }

    /// <summary>
    /// A table cell without the backticks the document sets a literal in.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The cell's text.</returns>
    private static string Bare(string cell) => cell.Trim(Backtick);

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
                $"No directory at or above '{AppContext.BaseDirectory}' holds '{SolutionFileName}', so the endpoint table has no root to be read from.")
            : directory.FullName;
    }
}
