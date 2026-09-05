using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// The enrolment window is the only moment this server answers a party it has not
/// authenticated, so every bound on it is a test named after the property it holds rather
/// than a sentence in a document.
/// </summary>
public class EnrolmentWindowTests
{
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The two files in the plugin that may call the method that opens a window: the join that
    /// writes the record, and the administrative action an administrator reaches it through.
    /// </summary>
    private static readonly string[] _theCallers = new[] { "AdministrativePlaneController.cs", "Enrolment.cs" };

    private static readonly DateTimeOffset Noon =
        new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// It opens only when an administrator opens it, never on startup, never on install,
    /// never because a peer asked.
    /// </summary>
    /// <remarks>
    /// THIS CASE EXPECTED NO CALLER AT ALL, THEN EXACTLY ONE, AND NOW EXPECTS TWO. What it first
    /// rested on was that nothing in the plugin called the method, which held while opening a
    /// window wrote no record and was therefore something only the test project ever did. The
    /// join in <see cref="Enrolment"/> was the first caller. The second is the action on the
    /// administrative plane that an administrator opens a window through, which is issue #357:
    /// it sits behind the host's elevation policy and reads the actor off the principal the host
    /// authenticated, so it is the administrator this property names rather than an exception to
    /// it. An assertion of one file would have to be deleted to let it land, which is the shape
    /// of a guard being worked around rather than argued with, and this remark is the argument.
    /// <para>
    /// The property is unchanged and the set is what carries it. The scan matches the text
    /// <c>.Open(</c>, so a file calling <see cref="Enrolment.Open"/> is caught by it exactly as a
    /// file calling <see cref="EnrolmentWindow.Open"/> is: both spellings are an instance call on
    /// that name. So a startup path, a hosted service, anything on the peer plane or a second
    /// controller that reached either one would appear here, and what the equality says is that
    /// none of them does. Of the two files named, the join takes the address a person typed and
    /// the actor who typed it, and the action is the one place that hands it both.
    /// </para>
    /// </remarks>
    [Fact]
    public void ItOpensOnlyWhenAnAdministratorOpensIt()
    {
        var callers = PluginSourceFiles()
            .Where(f => Path.GetFileName(f) != "EnrolmentWindow.cs")
            .Where(f => File.ReadAllText(f).Contains(".Open(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(_theCallers, callers);
    }

    /// <summary>
    /// The scan above passes trivially against an empty walk, which is what a moved project
    /// or a renamed folder produces. This fixes its floor.
    /// </summary>
    [Fact]
    public void TheScanForCallersActuallyReadsFiles()
    {
        var files = PluginSourceFiles();

        Assert.NotEmpty(files);
        Assert.Contains(files, f => Path.GetFileName(f) == "Plugin.cs");
        Assert.Contains(files, f => Path.GetFileName(f) == "PluginServiceRegistrator.cs");
        Assert.Contains(files, f => Path.GetFileName(f) == "EnrolmentWindow.cs");
    }

    /// <summary>
    /// It closes on the first successful use, so a second enrolment against the same window
    /// is refused even where the arriving key verifies.
    /// </summary>
    [Fact]
    public void ItClosesOnTheFirstSuccessfulUse()
    {
        var window = new EnrolmentWindow(new NoPairings());
        var peer = Address("https://peer.example.org");

        Assert.Equal(WindowOpening.Opened, window.Open(peer, Noon));
        Assert.Equal(WindowUse.Accepted, window.Present(peer, VerificationOutcome.Verified, Noon));
        Assert.Equal(WindowUse.Refused, window.Present(peer, VerificationOutcome.Verified, Noon));
    }

    /// <summary>
    /// It closes on a timer whether or not it was used, and the lifetime is measured from
    /// the instant it opened rather than from the last thing that touched it.
    /// </summary>
    [Fact]
    public void ItClosesOnATimerWhetherOrNotItWasUsed()
    {
        var window = new EnrolmentWindow(new NoPairings());
        var peer = Address("https://peer.example.org");

        window.Open(peer, Noon);

        var justBefore = Noon.AddSeconds(EnrolmentWindow.LifetimeSeconds - 1);
        var closed = Noon.AddSeconds(EnrolmentWindow.LifetimeSeconds);

        Assert.True(window.IsOpen(peer, justBefore));
        Assert.False(window.IsOpen(peer, closed));
        Assert.Equal(WindowUse.Refused, window.Present(peer, VerificationOutcome.Verified, closed));
    }

    /// <summary>
    /// It refuses attempts after a small number of failures, and the refusal is per window.
    /// Two windows are two doors: failures against one leave the other where it was, and
    /// nothing here reads the address a request appeared to come from.
    /// </summary>
    [Fact]
    public void ItRefusesAfterASmallNumberOfFailuresPerWindow()
    {
        var window = new EnrolmentWindow(new NoPairings());
        var one = Address("https://one.example.org");
        var other = Address("https://other.example.org");

        window.Open(one, Noon);
        window.Open(other, Noon);

        for (var attempt = 0; attempt < EnrolmentWindow.FailuresAllowed; attempt++)
        {
            Assert.Equal(WindowUse.Refused, window.Present(one, VerificationOutcome.Refused, Noon));
        }

        Assert.False(window.IsOpen(one, Noon));
        Assert.Equal(WindowUse.Refused, window.Present(one, VerificationOutcome.Verified, Noon));

        Assert.True(window.IsOpen(other, Noon));
        Assert.Equal(WindowUse.Accepted, window.Present(other, VerificationOutcome.Verified, Noon));
    }

    /// <summary>
    /// A failure below the bound leaves the window open, so the bound is what closes it
    /// rather than the first mistake an operator makes.
    /// </summary>
    [Fact]
    public void AFailureBelowTheBoundLeavesTheWindowOpen()
    {
        var window = new EnrolmentWindow(new NoPairings());
        var peer = Address("https://peer.example.org");

        window.Open(peer, Noon);

        for (var attempt = 0; attempt < EnrolmentWindow.FailuresAllowed - 1; attempt++)
        {
            Assert.Equal(WindowUse.Refused, window.Present(peer, VerificationOutcome.Refused, Noon));
        }

        Assert.True(window.IsOpen(peer, Noon));
        Assert.Equal(WindowUse.Accepted, window.Present(peer, VerificationOutcome.Verified, Noon));
    }

    /// <summary>
    /// It refuses to open at all when this server already has a pairing with the peer it
    /// names, so a fresh window cannot be used to displace an existing relationship.
    /// </summary>
    [Fact]
    public void ItRefusesToOpenAgainstAPeerAlreadyPaired()
    {
        var peer = Address("https://peer.example.org");
        var window = new EnrolmentWindow(new PairedWith(peer));

        Assert.Equal(WindowOpening.AlreadyPaired, window.Open(peer, Noon));
        Assert.False(window.IsOpen(peer, Noon));
        Assert.Equal(WindowUse.Refused, window.Present(peer, VerificationOutcome.Verified, Noon));
    }

    /// <summary>
    /// The pairing is looked up by the address rather than by a pairing identifier. An
    /// identifier is derived from both public keys, so a peer offering a different key
    /// produces a different one and the live pairing would not be found at all.
    /// </summary>
    [Fact]
    public void ThePairingLookupIsByAddress()
    {
        var paired = Address("https://paired.example.org");
        var fresh = Address("https://fresh.example.org");
        var window = new EnrolmentWindow(new PairedWith(paired));

        Assert.Equal(WindowOpening.AlreadyPaired, window.Open(paired, Noon));
        Assert.Equal(WindowOpening.Opened, window.Open(fresh, Noon));
    }

    /// <summary>
    /// Closing is immediate. A used window and an elapsed one have stopped being open at the
    /// instant that ended them, not at the next sweep, and nothing has to run in between.
    /// </summary>
    [Fact]
    public void ClosingIsImmediateRatherThanOnTheNextTick()
    {
        var window = new EnrolmentWindow(new NoPairings());
        var used = Address("https://used.example.org");
        var elapsed = Address("https://elapsed.example.org");

        window.Open(used, Noon);
        window.Open(elapsed, Noon);

        window.Present(used, VerificationOutcome.Verified, Noon);

        var after = Noon.AddSeconds(EnrolmentWindow.LifetimeSeconds);

        Assert.Equal(Array.Empty<string>(), window.OpenAddresses(after));
        Assert.False(window.IsOpen(used, Noon));
        Assert.False(window.IsOpen(elapsed, after));
    }

    /// <summary>
    /// While it is open the plugin can say so, which is the failure the rest of these bounds
    /// are written against: a window an operator opened and forgot.
    /// </summary>
    [Fact]
    public void WhileItIsOpenTheAddressIsReportable()
    {
        var window = new EnrolmentWindow(new NoPairings());
        var peer = Address("https://peer.example.org");

        Assert.Equal(Array.Empty<string>(), window.OpenAddresses(Noon));

        window.Open(peer, Noon);

        Assert.Equal(new[] { peer.Value }, window.OpenAddresses(Noon));
    }

    /// <summary>
    /// An expired window and a used window produce the same refusal as a window that never
    /// existed, and so does one that has taken its failures. Every cause returns the one
    /// value, so a caller learns nothing about what an administrator here has done.
    /// </summary>
    [Fact]
    public void EveryClosedWindowRefusesAsOneThatNeverExisted()
    {
        var window = new EnrolmentWindow(new NoPairings());
        var never = Address("https://never.example.org");
        var used = Address("https://used.example.org");
        var expired = Address("https://expired.example.org");
        var exhausted = Address("https://exhausted.example.org");

        window.Open(used, Noon);
        window.Open(expired, Noon);
        window.Open(exhausted, Noon);

        window.Present(used, VerificationOutcome.Verified, Noon);

        for (var attempt = 0; attempt < EnrolmentWindow.FailuresAllowed; attempt++)
        {
            window.Present(exhausted, VerificationOutcome.Refused, Noon);
        }

        var after = Noon.AddSeconds(EnrolmentWindow.LifetimeSeconds);

        var refusals = new[] { never, used, expired, exhausted }
            .Select(a => window.Present(a, VerificationOutcome.Verified, after))
            .ToArray();

        Assert.Equal(new[] { WindowUse.Refused, WindowUse.Refused, WindowUse.Refused, WindowUse.Refused }, refusals);
    }

    /// <summary>
    /// A window is not reopened while it is open and its lifetime does not move, because a
    /// window that can be extended is a window that never closes.
    /// </summary>
    [Fact]
    public void AWindowIsNotReopenedOrExtended()
    {
        var window = new EnrolmentWindow(new NoPairings());
        var peer = Address("https://peer.example.org");

        window.Open(peer, Noon);

        var later = Noon.AddSeconds(EnrolmentWindow.LifetimeSeconds - 1);

        Assert.Equal(WindowOpening.AlreadyOpen, window.Open(peer, later));
        Assert.False(window.IsOpen(peer, Noon.AddSeconds(EnrolmentWindow.LifetimeSeconds)));
    }

    /// <summary>
    /// An elapsed window makes room for a fresh one, so an operator whose window ran out
    /// opens another rather than being told one is already open forever.
    /// </summary>
    [Fact]
    public void AnElapsedWindowMakesRoomForAFreshOne()
    {
        var window = new EnrolmentWindow(new NoPairings());
        var peer = Address("https://peer.example.org");

        window.Open(peer, Noon);

        var after = Noon.AddSeconds(EnrolmentWindow.LifetimeSeconds);

        Assert.Equal(WindowOpening.Opened, window.Open(peer, after));
        Assert.True(window.IsOpen(peer, after));
    }

    /// <summary>
    /// An administrator can close a window before anything has used it, and closing one that
    /// is not held says so rather than pretending.
    /// </summary>
    [Fact]
    public void AnAdministratorCanCloseAWindowEarly()
    {
        var window = new EnrolmentWindow(new NoPairings());
        var peer = Address("https://peer.example.org");

        window.Open(peer, Noon);

        Assert.True(window.Close(peer));
        Assert.False(window.Close(peer));
        Assert.Equal(WindowUse.Refused, window.Present(peer, VerificationOutcome.Verified, Noon));
    }

    /// <summary>
    /// The sweep names each elapsed window once, so the half-built record can be destroyed
    /// rather than outliving the window it belongs to, and names nothing that is still open.
    /// </summary>
    [Fact]
    public void TheSweepNamesEachElapsedWindowOnce()
    {
        var window = new EnrolmentWindow(new NoPairings());
        var early = Address("https://early.example.org");
        var late = Address("https://late.example.org");

        window.Open(early, Noon);
        window.Open(late, Noon.AddSeconds(60));

        var after = Noon.AddSeconds(EnrolmentWindow.LifetimeSeconds);

        Assert.Equal(new[] { early.Value }, window.CloseElapsed(after));
        Assert.Equal(Array.Empty<string>(), window.CloseElapsed(after));
        Assert.True(window.IsOpen(late, after));
    }

    /// <summary>
    /// A lifetime outside the bounds is refused rather than clamped silently to something the
    /// operator did not ask for. Both ends are refused: above the maximum, and a window that
    /// would never be open at all. The configuration surface that will carry this value is
    /// issue #50; the bound holds for every caller either way.
    /// </summary>
    [Fact]
    public void ALifetimeOutsideTheBoundsIsRefusedRatherThanClamped()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EnrolmentWindow(new NoPairings(), EnrolmentWindow.MaximumLifetimeSeconds + 1));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EnrolmentWindow(new NoPairings(), 0));
    }

    /// <summary>
    /// The default lifetime is inside the maximum, the maximum is short enough to be the
    /// bound the rule describes rather than a number that refuses nothing, and the failure
    /// allowance leaves an operator at least one attempt.
    /// </summary>
    [Fact]
    public void TheDefaultLifetimeIsShortInsideTheMaximumAndOneAttemptIsAllowed()
    {
        Assert.True(EnrolmentWindow.LifetimeSeconds <= EnrolmentWindow.MaximumLifetimeSeconds);
        Assert.True(EnrolmentWindow.MaximumLifetimeSeconds <= 3600);
        Assert.True(EnrolmentWindow.FailuresAllowed >= 1);
    }

    /// <summary>
    /// A configured lifetime inside the bound is the one that is used, so the constant is a
    /// default rather than the only value the type can hold.
    /// </summary>
    [Fact]
    public void AConfiguredLifetimeInsideTheBoundIsTheOneUsed()
    {
        var window = new EnrolmentWindow(new NoPairings(), 60);
        var peer = Address("https://peer.example.org");

        window.Open(peer, Noon);

        Assert.True(window.IsOpen(peer, Noon.AddSeconds(59)));
        Assert.False(window.IsOpen(peer, Noon.AddSeconds(60)));
    }

    private static PeerAddress Address(string candidate)
    {
        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse(candidate, out var address));
        Assert.NotNull(address);

        return address;
    }

    /// <summary>
    /// Every C# file in the plugin project, skipping the build output.
    /// </summary>
    /// <returns>The paths of the files found.</returns>
    private static string[] PluginSourceFiles()
    {
        var plugin = Path.Join(RepositoryRoot(), "Jellyfin.Plugin.ServerPairing");

        return Directory.EnumerateFiles(plugin, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The repository root, found by walking up from the directory the test assembly was
    /// loaded from until the solution file appears. It is not derived from the path the
    /// compiler recorded for this file, because a deterministic build rewrites that to a
    /// placeholder root that exists on no machine.
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
                $"No directory at or above '{AppContext.BaseDirectory}' holds '{SolutionFileName}', so the source scan has no root to read.")
            : directory.FullName;
    }

    /// <summary>
    /// A server with no pairings, which is what every enrolment starts against.
    /// </summary>
    private sealed class NoPairings : IPairedPeers
    {
        public bool HasPairingWith(PeerAddress address) => false;
    }

    /// <summary>
    /// A server already paired with one peer.
    /// </summary>
    private sealed class PairedWith : IPairedPeers
    {
        private readonly IReadOnlyCollection<string> _addresses;

        public PairedWith(params PeerAddress[] addresses)
        {
            _addresses = addresses.Select(a => a.Value).ToArray();
        }

        public bool HasPairingWith(PeerAddress address) => _addresses.Contains(address.Value);
    }
}
