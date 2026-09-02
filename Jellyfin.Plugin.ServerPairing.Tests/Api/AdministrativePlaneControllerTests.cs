using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Jellyfin.Plugin.ServerPairing;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Jellyfin.Plugin.ServerPairing.Tests.Protocol;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// The administrative plane: what it asks the host for, what it answers, and what it does with
/// a store it cannot read.
/// </summary>
/// <remarks>
/// The elevation cases are asked of the host's own action discovery rather than of the
/// attribute, for the reason <c>EndpointAuthorizationTableTests</c> gives: what reaches the
/// server is the resolved metadata, and a method that ended up with the server's default is
/// exactly the failure that reading the attribute walks past.
/// <para>
/// WHAT NONE OF THESE CASES DOES IS WATCH THE HOST REFUSE. That is the server's authorization
/// middleware rather than this plugin's code, and a case that stood that pipeline up would be
/// judging the framework. <c>docs/testing.md</c> refuses the neighbouring apparatus and does
/// not name this case. What is asserted here is that the declaration is present and is the
/// right one.
/// </para>
/// </remarks>
public class AdministrativePlaneControllerTests
{
    /// <summary>
    /// Every endpoint the host would serve out of the plugin assembly, as its action name and
    /// the policies the authorization middleware would read.
    /// </summary>
    /// <returns>One entry per action.</returns>
    private static (string Action, string[] Policies)[] Served()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mvc = services.AddControllers();
        mvc.PartManager.ApplicationParts.Clear();
        mvc.PartManager.ApplicationParts.Add(new AssemblyPart(typeof(AdministrativePlaneController).Assembly));

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors
            .Items
            .OfType<ControllerActionDescriptor>()
            .Select(descriptor => (
                Action: descriptor.ControllerTypeInfo.Name + "." + descriptor.MethodInfo.Name,
                Policies: descriptor.EndpointMetadata
                    .OfType<IAuthorizeData>()
                    .Select(data => data.Policy ?? "(no policy)")
                    .ToArray()))
            .ToArray();
    }

    /// <summary>
    /// Every action the host would serve out of this controller asks for the host's elevation
    /// policy and for nothing else. This is issue #289's second condition, and it holds
    /// whatever <c>docs/endpoints.md</c> says: an action that lost the policy fails here even
    /// where somebody edited the table to match it.
    /// </summary>
    [Fact]
    public void EveryActionOnThisPlaneAsksTheHostForElevation()
    {
        var administrative = Served()
            .Where(endpoint => endpoint.Action.StartsWith(
                nameof(AdministrativePlaneController) + ".", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(administrative);

        foreach (var endpoint in administrative)
        {
            Assert.Equal(new[] { Policies.RequiresElevation }, endpoint.Policies);
        }
    }

    /// <summary>
    /// The floor under the case above. The walk has to be reading a real set of actions, and
    /// the other plane has to still be in it, so a discovery that returned only this
    /// controller cannot pass by finding nothing to disagree with.
    /// </summary>
    [Fact]
    public void TheWalkSeesBothPlanes()
    {
        var served = Served();

        Assert.Contains(served, endpoint => endpoint.Action == "AdministrativePlaneController.Pairings");
        Assert.Contains(served, endpoint => endpoint.Action == "PeerPlaneController.Hello");
    }

    /// <summary>
    /// The action answers with the identifiers the store holds.
    /// </summary>
    [Fact]
    public void ThePairingsAnsweredAreTheOnesTheStoreHolds()
    {
        var store = new InMemoryPairingKeyStore();

        store.Add("9f8c1d2b3a4e5f60718293a4b5c6d7e8", KeyMaterial.Fresh());
        store.Add("0011223344556677889900aabbccddee", KeyMaterial.Fresh());

        var answer = Assert.IsType<ContentResult>(Controller(store).Pairings());

        Assert.Equal(200, answer.StatusCode);
        Assert.Contains("9f8c1d2b3a4e5f60718293a4b5c6d7e8", answer.Content, StringComparison.Ordinal);
        Assert.Contains("0011223344556677889900aabbccddee", answer.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// A server holding no pairing answers an empty list rather than a problem. An operator
    /// asking the question before pairing anything is not meeting a fault.
    /// </summary>
    [Fact]
    public void AServerHoldingNothingAnswersAnEmptyList()
    {
        var answer = Assert.IsType<ContentResult>(Controller(new InMemoryPairingKeyStore()).Pairings());

        Assert.Equal(200, answer.StatusCode);
        Assert.Equal("[]", answer.Content);
    }

    /// <summary>
    /// The answer carries the identifier and none of the key. The assertion is an absence, so
    /// it is made beside a presence: the same answer is required to hold the identifier, which
    /// is what stops an empty answer from passing this by being empty.
    /// </summary>
    /// <remarks>
    /// The bound is the one <c>docs/logging.md</c> already states for the same shape: an
    /// absence can only be asserted over the encodings somebody enumerated, and a key escaping
    /// through a fourth passes. What holds more widely than this case is
    /// <c>EndpointKeyMaterialTests</c>, which walks the type graph out of every action rather
    /// than the text of one answer.
    /// </remarks>
    [Fact]
    public void TheAnswerCarriesTheIdentifierAndNoneOfTheKey()
    {
        var store = new InMemoryPairingKeyStore();
        var bytes = RandomNumberGenerator.GetBytes(KeyMaterial.Length);

        store.Add("9f8c1d2b3a4e5f60718293a4b5c6d7e8", KeyMaterial.From(bytes));

        var answer = Assert.IsType<ContentResult>(Controller(store).Pairings());
        var content = answer.Content ?? string.Empty;

        Assert.Contains("9f8c1d2b3a4e5f60718293a4b5c6d7e8", content, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(bytes), content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(bytes), content, StringComparison.Ordinal);
    }

    /// <summary>
    /// A store that cannot be read is named rather than thrown. An exception leaving an action
    /// reaches the host's own pipeline, and what an administrator then gets is whatever that
    /// pipeline produces, which on a server with the developer page turned on is a stack trace
    /// instead of the one sentence they can act on.
    /// </summary>
    [Fact]
    public void AStoreThatCannotBeReadIsNamedRatherThanThrown()
    {
        var answer = Assert.IsType<ContentResult>(Controller(new UnreadableStore()).Pairings());

        Assert.Equal(AdministrativeAnswer.ProblemStatus, answer.StatusCode);
        Assert.Equal("{\"problem\":\"key-store-unreadable\"}", answer.Content);
    }

    /// <summary>
    /// The named problem carries nothing of the fault. What the operator is told is the one
    /// word; the exception's own text goes to the log, which is where
    /// <c>docs/logging.md</c> bounds what may appear.
    /// </summary>
    [Fact]
    public void TheNamedProblemCarriesNothingOfTheFault()
    {
        var answer = Assert.IsType<ContentResult>(Controller(new UnreadableStore()).Pairings());
        var content = answer.Content ?? string.Empty;

        Assert.DoesNotContain(UnreadableStore.Detail, content, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two planes answer a problem in different shapes, which is the difference issue #289
    /// asks to be made rather than assumed. A stranger on the peer plane gets one code whatever
    /// happened; an administrator who has already passed the host's policy gets the cause.
    /// </summary>
    [Fact]
    public void TheTwoPlanesDoNotAnswerAProblemTheSameWay()
    {
        var administrative = Assert.IsType<ContentResult>(Controller(new UnreadableStore()).Pairings());

        Assert.NotEqual(Refusal.Status, administrative.StatusCode);
        Assert.NotEqual(Refusal.Body(RefusalCode.Refused), administrative.Content);
    }

    /// <summary>
    /// Every spelling of a problem is a word rather than a sentence, and no two problems share
    /// one. A vocabulary with a duplicate in it is one an operator cannot search for.
    /// </summary>
    [Fact]
    public void EveryProblemHasItsOwnWireSpelling()
    {
        var spellings = Enum.GetValues<AdministrativeProblem>()
            .Select(AdministrativeAnswer.Wire)
            .ToArray();

        Assert.NotEmpty(spellings);
        Assert.Equal(spellings.Length, spellings.Distinct(StringComparer.Ordinal).Count());
        Assert.All(spellings, spelling => Assert.DoesNotContain(' ', spelling));
    }

    /// <summary>
    /// A problem this vocabulary has no word for is refused rather than written into an answer
    /// as a number. The failure that catches is a member added to the enumeration and not to
    /// the spelling, which would otherwise reach an operator as an integer.
    /// </summary>
    [Fact]
    public void AProblemOutsideTheVocabularyIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AdministrativeAnswer.Wire((AdministrativeProblem)7));
    }

    /// <summary>
    /// The host can build this controller out of what the registrator adds. A controller the
    /// container cannot construct answers with a server error rather than with anything this
    /// plugin decided.
    /// </summary>
    /// <remarks>
    /// What is substituted is what the server supplies and the registrator does not: the
    /// logging services the generic host installs, and the application paths the key store
    /// derives its file from.
    /// </remarks>
    [Fact]
    public void TheHostCanBuildThisControllerFromWhatTheRegistratorAdds()
    {
        var services = new ServiceCollection();

        services.AddLogging();

        var paths = Substitute.For<IApplicationPaths>();
        paths.DataPath.Returns(Path.GetTempPath());
        services.AddSingleton(paths);

        new PluginServiceRegistrator().RegisterServices(services, Substitute.For<IServerApplicationHost>());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(ActivatorUtilities.CreateInstance<AdministrativePlaneController>(scope.ServiceProvider));
    }

    /// <summary>
    /// A controller over a given store, with the logger going nowhere. The counters and the
    /// arrival limit are fresh ones nothing has written into, because no case in this file is
    /// about the diagnostics action; that one is `RefusalCountersTests`.
    /// </summary>
    /// <param name="keys">The store.</param>
    /// <returns>The controller.</returns>
    private static AdministrativePlaneController Controller(IPairingKeyStore keys)
        => Controller(keys, new InMemoryPairingRecords());

    /// <summary>
    /// A controller over a given key store and a given record store, with the logger going
    /// nowhere.
    /// </summary>
    /// <param name="keys">The key store.</param>
    /// <param name="records">The record store.</param>
    /// <returns>The controller.</returns>
    private static AdministrativePlaneController Controller(
        IPairingKeyStore keys,
        IPairingRecordStore records)
        => new AdministrativePlaneController(
            keys,
            records,
            new RefusalCounters(),
            new ArrivalLimit(),
            NullLogger<AdministrativePlaneController>.Instance);

    /// <summary>
    /// A key store whose read throws, which is what a file that does not parse does today.
    /// </summary>
    private sealed class UnreadableStore : IPairingKeyStore
    {
        /// <summary>
        /// Text that occurs nowhere else, so a case can assert it did not reach the caller.
        /// </summary>
        public const string Detail = "the-store-file-did-not-parse";

        public KeyMaterial? Live(string pairingId, DateTimeOffset at) => throw new InvalidOperationException(Detail);

        public PairingKeys? Both(string pairingId, DateTimeOffset at) => throw new InvalidOperationException(Detail);

        public void Add(string pairingId, KeyMaterial current) => throw new InvalidOperationException(Detail);

        public void Replace(string pairingId, KeyMaterial replacement, DateTimeOffset supersededStopsAt)
            => throw new InvalidOperationException(Detail);

        public void Destroy(string pairingId) => throw new InvalidOperationException(Detail);

        public IReadOnlyList<string> Pairings() => throw new InvalidOperationException(Detail);
    }
}
