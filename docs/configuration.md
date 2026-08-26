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
| `TrueFalseSetting` | `bool` | `true` | `true` or `false` |
| `AnInteger` | `int` | `2` | any whole number the serialiser accepts; the page's own input carries a lower bound of zero and nothing carries an upper one |
| `AString` | `string` | `string` | any text |
| `Options` | `SomeOptions` | `AnotherOption` | `OneOption` or `AnotherOption` |

The defaults in that table are not restated from the type. `ConfigurationDocumentTests`
constructs the configuration the way the host does and compares each documented
default against the value that construction produces, so a default changed in one
place and not the other is a red suite rather than a document nobody re-read.

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

The settings this plugin will actually have are the peer address, the
acknowledgement that turns cleartext transport on, the timings that have
defaults, and the switches that turn behaviour off. None of them is on the type.

The timings exist already, as constants with their reason argued at each
constant rather than as values somebody picked:

    git grep -nE 'public const int (LifetimeSeconds|MaximumLifetimeSeconds|FailuresAllowed|WindowSeconds|RememberedSeconds|NoncesPerPairing)' -- Jellyfin.Plugin.ServerPairing/Protocol/

A constant is a stronger position than a setting for as long as nothing needs to
change it on a running server, and the move from one to the other is not free:
the four settings above are what the dashboard page binds to, so removing them
from the type leaves a page whose controls save into properties that are gone.
The type and the page therefore move together. Issue #50 owns the settings and
issue #49 owns the page.

## Nothing refuses a value at load

An out-of-range value is not refused today, and no message names a setting.
There is no load-time validation step anywhere in this plugin:

    git grep -nE 'IValidateOptions|ConfigurationChanged|OnConfigurationChanged' -- Jellyfin.Plugin.ServerPairing/

What issue #50 asks for is that such a value be refused with the setting named,
rather than clamped silently to something the operator did not ask for, and that
a configuration that fails validation leave the plugin loaded and refusing to
pair, so that it can be repaired from the dashboard rather than from the
filesystem. That is owed and is not written here as though it were done.

## What is refused

That every setting on the type has a row above, and that the row's default is the
default the type produces. `ConfigurationDocumentTests` reads this file and walks
the configuration type by reflection, in both directions: a setting with no row
fails, and a row naming no setting fails.

    git grep -n 'public void' -- Jellyfin.Plugin.ServerPairing.Tests/Configuration/ConfigurationDocumentTests.cs

It judges the table and nothing else. Whether a documented range is the right
range, and whether a default is the safe one, are judgements no reading of this
tree makes.
