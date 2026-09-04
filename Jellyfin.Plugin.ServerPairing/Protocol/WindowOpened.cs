namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What opening an enrolment window produced: the window's own answer, and the pairing the
/// record was written under where one was.
/// </summary>
/// <param name="Opening">What <see cref="EnrolmentWindow"/> answered.</param>
/// <param name="PairingId">
/// The provisional identifier the record was written under, or null where no window opened and
/// therefore no record was written.
/// </param>
/// <remarks>
/// Two members rather than a nullable identifier on its own, because a caller has to be able to
/// tell <see cref="WindowOpening.AlreadyOpen"/> from <see cref="WindowOpening.AlreadyPaired"/>
/// and an identifier that is absent says only that neither happened.
/// <para>
/// The identifier is present exactly when <see cref="Opening"/> is
/// <see cref="WindowOpening.Opened"/>. That is a property of <see cref="Enrolment.Open"/> rather
/// than of this type, which holds what it is given.
/// </para>
/// </remarks>
public readonly record struct WindowOpened(WindowOpening Opening, string? PairingId);
