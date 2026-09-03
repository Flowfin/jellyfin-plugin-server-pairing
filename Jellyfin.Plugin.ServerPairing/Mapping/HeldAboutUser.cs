using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ServerPairing.Mapping;

/// <summary>
/// What this plugin holds about one local user, across every pairing, for an administrator
/// who was asked.
/// </summary>
/// <remarks>
/// This is the report half of issue #60. The removal half is not built, and this type does not
/// pretend to it: it changes nothing in the table, and what it writes is one audit entry.
/// <para>
/// THE PAIRINGS ARE HANDED IN RATHER THAN CHOSEN HERE, and which enumeration they come from is
/// the whole of what makes the report complete or silently short. The mapping store is readable
/// per pairing and by nothing else, so a report for a person is a walk over pairings, and there
/// are two walks in this plugin. The key store enumerates every pairing holding key material,
/// which a pairing that has not finished enrolling does not; the record store enumerates every
/// pairing a record exists for, which is every pairing a mapping may be made under. A report
/// walked over the key store returns nothing for a user mapped only under a half-built pairing
/// and looks exactly like a report for a user mapped nowhere, which is the failure #60's own
/// notes warn against by name. The caller on the administrative plane hands in the record
/// store's walk and a case there refuses the other one.
/// </para>
/// <para>
/// THE AUDIT ENTRY NAMES WHO ASKED AND NEITHER USER. A report of what is held about a person is
/// itself an act worth being able to find later, which is why <c>docs/data.md</c> asks for an
/// entry for the report as well as for the removal. What the entry may hold is decided by the
/// same rule as the mapping-change entry: the peer identity is the first thing on the never-log
/// list in <c>docs/logging.md</c>, and the local identifier is not a field its row names. So the
/// trail says that a report was made, by whom, and how far it looked, and the table itself is
/// where an operator entitled to see it reads who was mapped to whom.
/// </para>
/// </remarks>
public sealed class HeldAboutUser
{
    private readonly IUserMappingStore _mappings;
    private readonly ILogger<HeldAboutUser> _log;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeldAboutUser"/> class.
    /// </summary>
    /// <param name="mappings">Where the mappings are kept.</param>
    /// <param name="log">Where the audit entry for a report goes.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <remarks>
    /// The log is required rather than optional, for the reason it is required on
    /// <see cref="UserMappings"/>: an overload without it would build a report nobody can find
    /// afterwards, silently.
    /// </remarks>
    public HeldAboutUser(IUserMappingStore mappings, ILogger<HeldAboutUser> log)
    {
        _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Every mapping held for one local user under the pairings given.
    /// </summary>
    /// <param name="pairings">The pairings to look under, which the caller enumerates from the record store.</param>
    /// <param name="localUserId">The user on this server the report is about.</param>
    /// <param name="administrator">Who asked, for the audit entry.</param>
    /// <returns>The mappings found, in the order the pairings were given, empty where the user is mapped nowhere.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    /// One audit entry per report, whether or not anything was found. A report that found
    /// nothing is still a report somebody made about a person, and an entry only for the
    /// reports that found something would leave the trail unable to say that the question was
    /// asked.
    /// </remarks>
    public IReadOnlyList<UserMapping> Report(IReadOnlyList<string> pairings, string localUserId, string administrator)
    {
        ArgumentNullException.ThrowIfNull(pairings);
        ArgumentNullException.ThrowIfNull(localUserId);
        ArgumentNullException.ThrowIfNull(administrator);

        var held = new List<UserMapping>();

        foreach (var pairingId in pairings)
        {
            foreach (var mapping in _mappings.For(pairingId))
            {
                if (string.Equals(mapping.LocalUserId, localUserId, StringComparison.Ordinal))
                {
                    held.Add(mapping);
                }
            }
        }

        if (_log.IsEnabled(LogLevel.Information))
        {
            _log.LogInformation(
                "What is held about one user was reported to an administrator. Administrator: {Administrator}, pairings looked under: {Pairings}, mappings found: {Mappings}",
                administrator,
                pairings.Count,
                held.Count);
        }

        return held;
    }
}
