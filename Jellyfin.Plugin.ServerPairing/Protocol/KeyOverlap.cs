using System;
using System.Security.Cryptography;
using System.Threading;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The keys one pairing verifies arriving requests with, and the bounded overlap a rotation
/// opens between two of them.
/// </summary>
/// <remarks>
/// A key that never changes is a key that is eventually copied, so rotation is here from the
/// start rather than bolted on to a version already in the field. The overlap is the whole
/// reason it works while one side is offline: the side that rotates starts signing with the
/// replacement immediately and goes on accepting the superseded key until the overlap closes,
/// so a peer that was switched off when the rotation started is still understood when it comes
/// back.
/// <para>
/// One instance holds one pairing. Two keys live at once is the maximum by construction rather
/// than by a check, because the superseded key has one slot and a second rotation inside an
/// open overlap is refused.
/// </para>
/// <para>
/// Nothing here reads a clock. The instant arrives as an argument, so an overlap that has run
/// out is testable without waiting for one, and which clock supplies it is issue #26.
/// </para>
/// <para>
/// Nothing here decides what a key reaches. That is the state a pairing is in, which is
/// <see cref="PairingStateMachine"/>, and a rotation cannot move it: a pairing that answers
/// nothing before a rotation answers nothing after one, because the only state a rotation
/// moves is <c>Active</c> to <c>Rotating</c> and both of those already answered.
/// </para>
/// </remarks>
public sealed class KeyOverlap
{
    /// <summary>
    /// The longest an overlap may run, in seconds.
    /// </summary>
    /// <remarks>
    /// A day, because the case the overlap exists for is a home server that was switched off
    /// overnight and comes back the next morning. Past that the superseded key has stopped
    /// being a grace period and has become a second live key nobody is watching, which is what
    /// rotation is for getting rid of. A rotation asking for longer is refused rather than
    /// shortened, so a caller that wanted more finds out instead of being quietly given less.
    /// </remarks>
    public const int MaximumOverlapSeconds = 86400;

    /// <summary>
    /// The length of a key, from the HKDF output length <c>docs/crypto.md</c> fixes.
    /// </summary>
    public const int KeyLength = 32;

    private readonly Lock _gate = new Lock();

    /// <summary>
    /// Stands in for the superseded key when there is none, so that judging a request costs
    /// the same work whether or not a rotation is open. Without it, the time an answer takes
    /// says whether this pairing is mid-rotation to a caller who cannot authenticate at all.
    /// </summary>
    private readonly byte[] _standIn = RandomNumberGenerator.GetBytes(KeyLength);

    private byte[] _current;
    private byte[]? _superseded;
    private DateTimeOffset _supersededStopsAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyOverlap"/> class.
    /// </summary>
    /// <param name="key">The key this pairing starts on.</param>
    /// <exception cref="ArgumentException">The key is not the length of a key.</exception>
    public KeyOverlap(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeyLength)
        {
            throw new ArgumentException(
                "A pairing cannot start on something that is not the length of a key.",
                nameof(key));
        }

        _current = key.ToArray();
    }

    /// <summary>
    /// Gets a value indicating whether a superseded key is still holding its slot.
    /// </summary>
    /// <remarks>
    /// True from the moment a rotation is accepted until the overlap closes, whether it closes
    /// by running out, by the peer proving it has the replacement, or by being abandoned.
    /// </remarks>
    public bool IsRotating
    {
        get
        {
            lock (_gate)
            {
                return _superseded is not null;
            }
        }
    }

    /// <summary>
    /// Gets the instant the superseded key stops verifying.
    /// </summary>
    /// <remarks>
    /// Meaningful only while <see cref="IsRotating"/> is true. It is here so that a test and
    /// the diagnostics surface in issue #51 can read the bound rather than infer it.
    /// </remarks>
    public DateTimeOffset OverlapEndsAt
    {
        get
        {
            lock (_gate)
            {
                return _supersededStopsAt;
            }
        }
    }

    /// <summary>
    /// How many keys verify an arriving request at an instant.
    /// </summary>
    /// <param name="at">The instant to count at.</param>
    /// <returns>One, or two while an overlap is open and has not run out.</returns>
    public int LiveKeys(DateTimeOffset at)
    {
        lock (_gate)
        {
            return SupersededIsLive(at) ? 2 : 1;
        }
    }

    /// <summary>
    /// Proposes a replacement key, opening an overlap in which both it and the key it
    /// supersedes verify what arrives.
    /// </summary>
    /// <param name="replacement">The replacement key.</param>
    /// <param name="at">When the rotation starts.</param>
    /// <param name="supersededStopsAt">When the superseded key stops verifying.</param>
    /// <returns>The outcome, which is the only thing that says whether anything moved.</returns>
    /// <remarks>
    /// Every refusal leaves the pairing on the key it was already using. That is the property
    /// worth stating in both directions: there is no path here that removes a key without
    /// putting one in its place, so a rotation that fails costs a rotation and never a
    /// pairing.
    /// </remarks>
    public RotationOutcome Rotate(ReadOnlySpan<byte> replacement, DateTimeOffset at, DateTimeOffset supersededStopsAt)
    {
        if (replacement.Length != KeyLength)
        {
            return RotationOutcome.Malformed;
        }

        var overlap = supersededStopsAt - at;

        if (overlap <= TimeSpan.Zero || overlap > TimeSpan.FromSeconds(MaximumOverlapSeconds))
        {
            return RotationOutcome.OutsideTheMaximum;
        }

        lock (_gate)
        {
            if (SupersededIsLive(at))
            {
                return RotationOutcome.AlreadyRotating;
            }

            if (_superseded is not null)
            {
                // The previous overlap ran out and nothing has closed it yet. Dropping it here
                // rather than writing over it is what keeps the superseded slot at one key and
                // leaves nothing of the old one in memory.
                DropSuperseded();
            }

            if (CryptographicOperations.FixedTimeEquals(replacement, _current))
            {
                return RotationOutcome.NotAReplacement;
            }

            _superseded = _current;
            _supersededStopsAt = supersededStopsAt;
            _current = replacement.ToArray();

            return RotationOutcome.Rotated;
        }
    }

    /// <summary>
    /// Gives up on a rotation, putting the pairing back on the superseded key.
    /// </summary>
    /// <returns>True where a rotation was given up, false where there was none to give up.</returns>
    /// <remarks>
    /// Both sides end on the key they were both already using, which is the only key the peer
    /// is known to hold. Abandoning is therefore the safe direction and is what a rotation
    /// nobody completed has to do, rather than leaving the pairing on a replacement the peer
    /// may never have received.
    /// </remarks>
    public bool Abandon()
    {
        lock (_gate)
        {
            if (_superseded is null)
            {
                return false;
            }

            CryptographicOperations.ZeroMemory(_current);
            _current = _superseded;
            _superseded = null;
            _supersededStopsAt = default;

            return true;
        }
    }

    /// <summary>
    /// Closes an overlap that has run out.
    /// </summary>
    /// <param name="at">The instant to judge against.</param>
    /// <returns>True where an overlap was open and has now closed.</returns>
    /// <remarks>
    /// This is what the local event in <c>docs/protocol.md</c> that takes a pairing from
    /// <c>Rotating</c> back to <c>Active</c> is driven by. It is not required for correctness,
    /// because <see cref="Verify"/> refuses a superseded key that has run out whether or not
    /// anybody called this; it exists so that the key material is dropped on a timer rather
    /// than on the next request, and so that the state the operator sees follows the same
    /// timer.
    /// </remarks>
    public bool CloseIfElapsed(DateTimeOffset at)
    {
        lock (_gate)
        {
            if (_superseded is null || SupersededIsLive(at))
            {
                return false;
            }

            DropSuperseded();

            return true;
        }
    }

    /// <summary>
    /// Which of this pairing's live keys verifies a request.
    /// </summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="presentedSignature">The value of the signature header, as base64.</param>
    /// <param name="at">This server's clock.</param>
    /// <returns>The key that verified it, or <see cref="KeyInUse.None"/>.</returns>
    /// <remarks>
    /// Both candidates are always computed, and where there is no superseded key the second is
    /// computed against a stand-in and discarded. A caller who holds neither key learns nothing
    /// from how long the answer took, including whether a rotation is open.
    /// <para>
    /// A request that verifies on the replacement closes the overlap there and then. The peer
    /// signing with it is the proof that both sides hold it, which is the other half of the
    /// rule the overlap carries: the superseded key stops being accepted when the overlap ends
    /// or when both sides have used the replacement, whichever comes first.
    /// </para>
    /// </remarks>
    public KeyInUse Verify(PairingRequest request, string? presentedSignature, DateTimeOffset at)
    {
        if (request is null
            || !FieldShape.IsWellFormed(request)
            || presentedSignature is null
            || !RequestAuthenticator.TryDecodeSignature(presentedSignature, out var presented))
        {
            return KeyInUse.None;
        }

        lock (_gate)
        {
            var supersededIsLive = SupersededIsLive(at);
            var second = supersededIsLive ? _superseded! : _standIn;

            var onCurrent = RequestAuthenticator.Matches(request, presented, _current);
            var onSuperseded = RequestAuthenticator.Matches(request, presented, second);

            if (onCurrent)
            {
                if (_superseded is not null)
                {
                    DropSuperseded();
                }

                return KeyInUse.Current;
            }

            return supersededIsLive && onSuperseded ? KeyInUse.Superseded : KeyInUse.None;
        }
    }

    /// <summary>
    /// The signature this side puts on what it sends, which is always over the current key.
    /// </summary>
    /// <param name="request">The request to sign.</param>
    /// <returns>The signature.</returns>
    /// <remarks>
    /// The side that rotates starts using the replacement immediately. Signing with the
    /// superseded key during the overlap would leave a peer that has caught up unable to
    /// verify anything, which is the failure the overlap exists to prevent, pointed the other
    /// way.
    /// </remarks>
    public string Sign(PairingRequest request)
    {
        lock (_gate)
        {
            return RequestAuthenticator.Sign(request, _current);
        }
    }

    /// <summary>
    /// Whether a superseded key exists and has not run out.
    /// </summary>
    /// <param name="at">The instant to judge against.</param>
    /// <returns>True where it still verifies.</returns>
    private bool SupersededIsLive(DateTimeOffset at) => _superseded is not null && at < _supersededStopsAt;

    /// <summary>
    /// Drops the superseded key, wiping what it held.
    /// </summary>
    private void DropSuperseded()
    {
        CryptographicOperations.ZeroMemory(_superseded!);
        _superseded = null;
        _supersededStopsAt = default;
    }
}
