using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Mapping;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Jellyfin.Plugin.ServerPairing.Tests.Mapping;
using Jellyfin.Plugin.ServerPairing.Tests.Protocol;
using Jellyfin.Plugin.ServerPairing.Wording;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// The wording read: the page gets every sentence an operator reads from the registers that
/// hold them, through one action, rather than carrying a copy.
/// </summary>
/// <remarks>
/// <c>CeremonyWordingTests.NoMarkupInThePluginCarriesASentence</c> is the other half of this.
/// It refuses a page that pastes a sentence; what it cannot see is whether the page can get the
/// sentence any other way, and a page that can neither carry a sentence nor ask for one shows
/// nothing. This is the read that lets it ask.
/// <para>
/// What is compared is the answer against the registers themselves, walked by reflection here
/// exactly as the action walks them, so a constant added to either register is in both sides
/// of the comparison without anybody listing it. That makes the comparison unable to catch a
/// constant the action skips by name, because the action skips nothing by name: it walks the
/// register. What it does catch is the action serving the wrong register under a member, a
/// member missing, a value that is not the constant, and a third member nobody asked for.
/// </para>
/// </remarks>
public class WordingTests
{
    /// <summary>
    /// Every sentence of both registers is in the answer, under the name of its constant and
    /// with the constant's value, so a page asking by name gets the sentence the guide quotes.
    /// </summary>
    [Fact]
    public void EverySentenceOfBothRegistersIsServedUnderItsName()
    {
        var served = Answered();

        Assert.Equal(Sentences(typeof(CeremonyWording)), served[WordingAnswer.CeremonyMember]);
        Assert.Equal(Sentences(typeof(DestructiveWording)), served[WordingAnswer.DestructiveMember]);
    }

    /// <summary>
    /// Nothing but the two registers is served. A third member would be a sentence living
    /// somewhere the guide's cases do not read.
    /// </summary>
    [Fact]
    public void NothingButTheTwoRegistersIsServed()
    {
        Assert.Equal(
            new[] { WordingAnswer.CeremonyMember, WordingAnswer.DestructiveMember },
            Answered().Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The sentence the page shows beside the removal of a mapping is in the answer under the
    /// name the page asks for. This is the one member the page reads today, named here so the
    /// page and the action cannot drift on its spelling without one of them reddening.
    /// </summary>
    [Fact]
    public void TheRemovalSentenceIsServedUnderTheNameThePageAsksFor()
    {
        Assert.Equal(
            DestructiveWording.RemoveMapping,
            Answered()[WordingAnswer.DestructiveMember][nameof(DestructiveWording.RemoveMapping)]);
    }

    /// <summary>
    /// The answer is what an administrator's browser expects of this plane: a 200 carrying
    /// JSON, the same as every other read on it.
    /// </summary>
    [Fact]
    public void TheAnswerIsJsonAtTwoHundred()
    {
        var answered = Assert.IsType<ContentResult>(Controller().Wording());

        Assert.Equal(200, answered.StatusCode);
        Assert.Equal("application/json", answered.ContentType);
    }

    /// <summary>
    /// The floor under the comparison above. Both sides of it are reflection walks, and two
    /// empty walks are equal, so each register has to be found holding something.
    /// </summary>
    [Fact]
    public void BothRegistersAreFoundHoldingSentences()
    {
        Assert.NotEmpty(Sentences(typeof(CeremonyWording)));
        Assert.NotEmpty(Sentences(typeof(DestructiveWording)));

        var served = Answered();

        Assert.NotEmpty(served[WordingAnswer.CeremonyMember]);
        Assert.NotEmpty(served[WordingAnswer.DestructiveMember]);
    }

    /// <summary>
    /// Every public string constant a register declares, under its name, read the way the
    /// wording cases read it.
    /// </summary>
    /// <param name="register">The register.</param>
    /// <returns>Name to sentence, in ordinal order of name.</returns>
    private static SortedDictionary<string, string> Sentences(Type register)
        => new SortedDictionary<string, string>(
            register
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal),
            StringComparer.Ordinal);

    /// <summary>
    /// What the action answers with, deserialised, so the cases read what an administrator's
    /// page receives rather than what the type was asked.
    /// </summary>
    /// <returns>Register name to its sentences.</returns>
    private static Dictionary<string, SortedDictionary<string, string>> Answered()
    {
        var answered = Assert.IsType<ContentResult>(Controller().Wording());

        Assert.Equal(200, answered.StatusCode);

        var served = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(answered.Content ?? "{}");

        Assert.NotNull(served);

        return served.ToDictionary(
            register => register.Key,
            register => new SortedDictionary<string, string>(register.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A controller over empty stores, because the read under test touches none of them.
    /// </summary>
    /// <returns>The controller.</returns>
    private static AdministrativePlaneController Controller()
        => Controller(new InMemoryPairingRecords());

    /// <summary>
    /// A controller over one record store, which the enrolment is joined over as well.
    /// </summary>
    /// <param name="records">The record store.</param>
    /// <returns>The controller.</returns>
    private static AdministrativePlaneController Controller(InMemoryPairingRecords records)
        => new AdministrativePlaneController(
            new InMemoryPairingKeyStore(),
            records,
            new RefusalCounters(),
            new ArrivalLimit(),
            new HeldAboutUser(new InMemoryUserMappings(), NullLogger<HeldAboutUser>.Instance),
            new UserMappings(new InMemoryUserMappings(), new PairingStateMachine(new InMemoryPairingRecords(), new InMemoryUserMappings()), NullLogger<UserMappings>.Instance),
            new InMemoryLocalUsers(),
            PlaneDependencies.EnrolmentOver(records),
            PlaneDependencies.NothingEntered(),
            PlaneDependencies.StoppedClock(),
            NullLogger<AdministrativePlaneController>.Instance);
}
