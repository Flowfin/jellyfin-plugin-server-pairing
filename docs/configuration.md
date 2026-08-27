# The plugin configuration

Every setting this plugin has, what each one defaults to, and what a value
outside its range does.

The configuration is the one thing in this plugin an operator can edit by hand.
The host writes it out with the XML serialiser, serves it back to the dashboard
through its own configuration endpoint, and hands the deserialised object to the
plugin. So a setting here is plaintext on the server's filesystem, in every
backup of that filesystem, and readable by anybody who can open the plugin's
settings page.

Nothing secret is on it, and that is refused rather than intended.
[`docs/keystore.md`](keystore.md) is where a pairing's key material lives and
why it lives there instead:

    git grep -n 'NoMemberReachableFromThePluginConfigurationCanHoldKeyMaterial' -- Jellyfin.Plugin.ServerPairing.Tests/

## Every setting

| Setting | Type | Default | Range |
| --- | --- | --- | --- |
| `AcknowledgeCleartextTransport` | `bool` | `false` | `true` or `false`; `true` also permits an `http` peer address |
| `EnrolmentWindowSeconds` | `int` | `600` | 1 to 1800 |
| `PeerPlaneArrivalsPerEnrolment` | `int` | `6` | 1 to 3600, and never larger than `PeerPlaneArrivalsPerPairing` |
| `PeerPlaneArrivalsPerPairing` | `int` | `60` | 1 to 3600 |
| `PeerPlaneWindowSeconds` | `int` | `60` | 1 to 3600 |
| `PeerAddress` | `string` | `(empty)` | empty, or an absolute `https` URI of at most 255 characters with no user information, no path, no query and no fragment, whose host is a plain ASCII domain name, an IPv4 literal or a bracketed IPv6 literal; `http` where the acknowledgement above is set |
| `TrueFalseSetting` | `bool` | `true` | `true` or `false` |
| `AnInteger` | `int` | `2` | any whole number the serialiser accepts; the page's own input carries a lower bound of zero and nothing carries an upper one |
| `AString` | `string` | `string` | any text |
| `Options` | `SomeOptions` | `AnotherOption` | `OneOption` or `AnotherOption` |

The defaults in that table are not restated from the type. `ConfigurationDocumentTests`
constructs the configuration the way the host does and compares each documented
default against the value that construction produces, so a default changed in one
place and not the other is a red suite rather than a document nobody re-read.

`(empty)` is how an empty string is written in the default column, because a blank
cell documents nothing while looking like documentation. The guard renders a
default the same way before it compares.

## The settings that configure something

`PeerAddress` is the one address this server will send a pairing request to. A
fresh installation has none, which is a server that pairs with nobody rather than
a server that is misconfigured. The forms that are accepted are the ones
[`docs/protocol.md`](protocol.md) fixes for the field, and they are read out of
the type that decides them rather than restated here:

    git grep -n 'return PeerAddressOutcome' -- Jellyfin.Plugin.ServerPairing/Protocol/PeerAddress.cs

`AcknowledgeCleartextTransport` is the operator acknowledgement that decision 3 on
issue #1 settles the shape of. Its safe value is `false`, and `false` is also what
a missing element deserialises to, so a configuration file that never mentions it
is a server that refuses a cleartext address. Setting it to `true` permits an
`http` peer address, and what that gives up is that request and response bodies,
the mapping table among them, are readable by anything on the path between the
two servers. The plugin writes that sentence to the log at Warning on every start
where the setting is on, so an operator who ticked it months ago meets it again
rather than only once.

`EnrolmentWindowSeconds` is how long an enrolment window stays open. That window is
the only moment this server answers a party it has not authenticated, so its
length is the size of the one opening a stranger gets, and a value above the
maximum is refused rather than shortened to it. The default and the maximum are
argued at the constants:

    git grep -nE 'public const int (LifetimeSeconds|MaximumLifetimeSeconds)' -- Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs

NOTHING IN THIS PLUGIN BUILDS A WINDOW YET, so that setting is refused out of range
and handed to nothing. A window is opened by an administrator and by nobody else,
which the suite refuses a second route to, so the thing that builds one is the
administrative surface in issue #49.

The three `PeerPlane` settings are the arrival allowance the peer plane runs on:
how long an allowance is counted over, how many requests one pairing identifier
may put on the plane inside that span, and how many may arrive claiming the
enrolment identifier or claiming nothing the protocol can read an identifier out
of. The third is the harder of the two allowances because it is the one a stranger
reaches without knowing anything, and it is refused where it is set above the
second rather than quietly becoming the softer limit. The defaults and the bounds
are argued at the constants rather than here:

    git grep -nE 'public const int (WindowSeconds|ArrivalsPerPairing|ArrivalsPerEnrolment|MaximumWindowSeconds|MaximumArrivals)' -- Jellyfin.Plugin.ServerPairing/Api/ArrivalLimit.cs

What is NOT a setting on that plane is how many identifiers are counted at once.
That is a bound on memory rather than a rate an operator tunes, an operator who
raises it buys a larger table for a flood to fill, and it stays a constant with
its argument at the constant.

None of these is on the dashboard page. The page binds to the template's four
settings, reads the whole configuration object before it saves and writes it back
whole, so a setting it does not bind to survives a save rather than being reset -
but an operator cannot yet type any of these there. The page is issue #49.

A configuration file written before a setting existed does not mention it, and
what that produces is the value the constructor set rather than the value the type
would have on its own. The serialiser builds the object through the parameterless
constructor and assigns only the members the document carries, which is measured
rather than assumed:

    git grep -n 'AMissingElementKeepsTheValueTheConstructorSet' -- Jellyfin.Plugin.ServerPairing.Tests/

That is why a count's default is written in the constructor even though a count
has a type default already. Zero is not a small allowance; it is a plane that
refuses everything.

## THESE FOUR ARE THE TEMPLATE'S AND THEY CONFIGURE NOTHING

They arrived with the official plugin template and no code in this plugin reads
any of them. The only things that name them are the type that declares them and
the page that binds to three of the four:

    git grep -lE 'TrueFalseSetting|AnInteger|AString|SomeOptions' -- Jellyfin.Plugin.ServerPairing/

So the ranges above bound a value nothing consumes, and setting any of them to
anything changes no behaviour on a running server. They are documented anyway,
because the row is what the guard below requires and a type carrying an
undocumented setting is the state this document exists to refuse - not because
these four are worth an operator's attention. They are not.

## What is not here yet

The peer address, the cleartext acknowledgement, the enrolment window's lifetime
and the peer plane's arrival allowance are on the type. The timestamp window, the
rotation overlap, the nonce store and the switches that turn behaviour off are
not.

Those timings exist already, as constants with their reason argued at each
constant rather than as values somebody picked:

    git grep -nE 'public const int (FailuresAllowed|WindowSeconds|RememberedSeconds|NoncesPerPairing|MaximumOverlapSeconds)' -- Jellyfin.Plugin.ServerPairing/Protocol/

A constant is a stronger position than a setting for as long as nothing needs to
change it on a running server, and the move from one to the other is not free:
the template's four settings are what the dashboard page binds to, so removing
them from the type leaves a page whose controls save into properties that are
gone. The type and the page therefore move together, and each timing moves under
the issue that fixed its own default and maximum rather than under one change
that moves all four. Issue #49 owns the page.

## What a bad value does

It is refused with the setting named, and nothing is clamped:

    git grep -n 'new SettingRefusal(' -- Jellyfin.Plugin.ServerPairing/Configuration/ConfigurationReading.cs

A clamp is what this refuses to do. An operator who sets a value outside its range
and gets a working server running on a different value has no reason to look for
one, and the value they typed is still in the file. So the value stays where they
put it, the setting is named, and the server does not pair.

WHAT A REFUSED PEER-PLANE ALLOWANCE FALLS BACK TO IS NOT NOTHING, and it is worth
separating from the sentence above. A plane whose limit was refused is not a plane
with no limit, so it runs on the allowance a server nobody configured runs on: the
default, not the boundary the operator's value crossed. The difference from a clamp
is that a clamp answers a request for a day with an hour and says nothing, where
this names the setting at Error, leaves the operator's value in the file, and puts
the plane on a number somebody argued.

Nothing throws. A setter that threw would be a setter the host's deserialiser
throws out of, which takes the plugin out at load - and the repair for that is a
text editor on the server's filesystem, which is what leaving the plugin loaded
exists to spare the operator. So the plugin loads, serves its page, and
`MayPair` is false.

    git grep -n 'public bool MayPair' -- Jellyfin.Plugin.ServerPairing/Configuration/ConfigurationReading.cs

WHAT "REFUSING TO PAIR" REACHES TODAY IS SMALLER THAN THE SENTENCE SOUNDS, and it
is a bound rather than an assurance. No administrative endpoint in this tree opens
an enrolment window, so there is no live pairing path for the reading to stop; what
it does stop is the peer address existing at all, since a refused address produces
none and an enrolment window is opened against one. The endpoint is issue #49, and
whoever builds it reads `MayPair` there.

The refusals are written to the log at Error when the server starts, one line per
setting, and the plugin keeps running:

    git grep -n 'class ConfigurationAtStartup' -- Jellyfin.Plugin.ServerPairing/Configuration/

## What is refused

That every setting on the type has a row above, and that the row's default is the
default the type produces. `ConfigurationDocumentTests` reads this file and walks
the configuration type by reflection, in both directions: a setting with no row
fails, and a row naming no setting fails.

    git grep -n 'public void' -- Jellyfin.Plugin.ServerPairing.Tests/Configuration/ConfigurationDocumentTests.cs

It judges the table and nothing else. Whether a documented range is the right
range, and whether a default is the safe one, are judgements no reading of this
tree makes.
