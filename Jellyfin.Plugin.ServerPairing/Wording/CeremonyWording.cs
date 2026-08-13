namespace Jellyfin.Plugin.ServerPairing.Wording;

/// <summary>
/// Every sentence an operator reads while pairing two servers.
/// </summary>
/// <remarks>
/// This and <see cref="DestructiveWording"/> are the whole of what an operator is told, and
/// they are two files rather than one only because the analyzers refuse a second type beside a
/// class. Nothing else in this plugin carries one of these sentences, which is asserted rather
/// than intended, so the wording can be read as prose and argued over as prose. The mechanism this
/// protocol rests on is a person comparing two values, which <c>docs/crypto.md</c> names as the
/// one mechanism in the design that is not cryptography, so a sentence that misleads is a
/// defect of the same kind as a key of the wrong length.
/// <para>
/// Nothing here is formatted, interpolated or assembled. A sentence built out of fragments is
/// a sentence nobody reviewed, and the fragments are what a translation and a screen reader
/// both break on. A value an operator reads, the fingerprint or an address, is placed beside
/// these sentences rather than inside them.
/// </para>
/// <para>
/// The page does not hold a second copy. What renders these is issue #49 and the endpoint that
/// serves them is issue #53; until those exist nothing reads this type, and the assertion that
/// no markup in this plugin carries these sentences is what keeps the single copy single.
/// </para>
/// </remarks>
public static class CeremonyWording
{
    /// <summary>
    /// Opening the window, which is the step an administrator takes first.
    /// </summary>
    public const string OpenTheWindow =
        "Enter the address of the other server and open the window. Until it closes, this " +
        "server will answer that address and no other.";

    /// <summary>
    /// While the window is open and nothing has arrived.
    /// </summary>
    public const string NothingHasArrivedYet =
        "Nothing has arrived from that address yet. The other operator has to open a window " +
        "on their server against this one.";

    /// <summary>
    /// What the value on the screen is, said without claiming more than it proves.
    /// </summary>
    public const string WhatTheValueIs =
        "The value below is worked out from both servers' keys. Two servers talking to each " +
        "other, and to nobody in between, work out the same value.";

    /// <summary>
    /// How to compare it, in the grouping <c>docs/crypto.md</c> pins as part of the value
    /// rather than as presentation.
    /// </summary>
    public const string HowToCompare =
        "Read the eight groups below out to the other operator and have them read back the " +
        "eight groups on their screen. Compare all eight.";

    /// <summary>
    /// What the comparison is for. This is the sentence the whole ceremony exists to deliver.
    /// </summary>
    public const string TheComparisonIsTheTrust =
        "Comparing is what makes this a pairing with that server rather than with whoever " +
        "answered. Confirming without comparing establishes nothing.";

    /// <summary>
    /// The confirmation itself.
    /// </summary>
    public const string ConfirmOnlyIfTheyMatch =
        "Confirm only if all eight groups are the same as the ones the other operator read out.";

    /// <summary>
    /// What to do when the two values differ. It says stop, and it does not say try again.
    /// </summary>
    public const string WhenTheyDiffer =
        "Stop. Do not confirm, and do not open another window. Values that differ mean the two " +
        "servers are not talking only to each other. Find out why before pairing them.";

    /// <summary>
    /// After confirming here and before the peer confirms.
    /// </summary>
    public const string WaitingForTheOtherOperator =
        "You have confirmed. The pairing starts working once the other operator confirms as " +
        "well. Nothing moves between the servers until then.";

    /// <summary>
    /// Both sides have confirmed.
    /// </summary>
    public const string Paired =
        "Both operators have confirmed. These two servers are paired.";

    /// <summary>
    /// The window closed before anything finished.
    /// </summary>
    public const string TheWindowClosed =
        "The window closed and nothing was kept from an enrolment that did not finish. Open a " +
        "new one to start again.";
}
