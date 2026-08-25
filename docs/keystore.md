# The key store

Where a pairing's key material is kept, why it is not kept where a plugin's
settings are kept, and what protecting it does and does not mean.

## Why it is not the plugin configuration

The host writes a plugin's configuration object to disk with the XML serialiser
and serves it back to the dashboard through its own configuration endpoint. That
is what a configuration is for and there is nothing wrong with it; it is simply
not somewhere a key can go. A key on that object would be plaintext in a file,
in every backup of the server's settings, and readable by anyone who can open
the plugin's own settings page.

So the key store is a separate thing, with its own interface, its own file and
its own rules. The interface is what the suite drives, so it is defined
independently of the file format:

    git grep -n 'interface IPairingKeyStore' -- Jellyfin.Plugin.ServerPairing/KeyStore/

That nothing on the configuration can reach key material is refused rather than
stated. `ConfigurationKeyMaterialTests` walks the compiled type graph of
`PluginConfiguration`, declared and inherited members at any depth, and fails on
any member whose type is a run of bytes, comes from the cryptography namespace,
or is the key type itself.

## Where the file is

Under the server's data path, in this plugin's own directory, and never under
the directory the host writes plugin configurations into. The path is derived
from the host's own `IApplicationPaths` rather than written down here, so a
server whose data directory is somewhere unusual is served correctly with no
setting to get wrong:

    git grep -n 'DirectoryName\|FileName' -- Jellyfin.Plugin.ServerPairing/KeyStore/KeyStorePath.cs

`KeyStorePathTests.TheKeyFileIsNotUnderThePluginConfigurationDirectory` is the
assertion, and it compares against the two paths the host reports rather than
against a string written twice.

## What the store holds

Three things per pairing: the current key, the key a rotation superseded, and
the instant that superseded key stops verifying. All three are persisted rather
than only the first, which is the answer taken on issue #30 on 2026-08-24. A
store that kept only the current key would lose the superseded one across a
restart, and a peer that had not yet caught up would stop being understood -
which is exactly the failure the rotation overlap exists to prevent.

Every read takes the instant it is judged at. A superseded key whose overlap has
run out is not a key any more, and a store that answered with one because nobody
had swept it would hand a caller something the rotation already ended. The
boundary is the instant itself: a key exactly at its own end is gone.

## The type key material travels in

Key material has its own type rather than being an array of bytes. That is not
tidiness. The rules issue #32 exists for cannot be written against `byte[]`: an
array prints its type name into a log without complaint, serialises wherever a
serialiser meets it, and is indistinguishable from every other array in a
reflection walk.

What the type does today:

- its string conversion carries none of its bytes, so a careless interpolation
  into a log line or an exception message produces a placeholder
- neither serialiser this plugin's dependencies bring can write one at all. Both
  refuse the type, because its only accessor is a span and a span is a ref
  struct neither can represent
- comparison is `CryptographicOperations.FixedTimeEquals` and there is no
  equality operator to reach for by habit
- destroying it overwrites the bytes and makes it unusable, and asking a
  destroyed key for its bytes is an error rather than an empty answer

## What zeroing does not achieve

`KeyMaterial.Destroy` calls `CryptographicOperations.ZeroMemory` on the array it
holds. **That narrows the window in which the key is in memory. It does not
close it.**

The runtime is free to move a managed array while a garbage collection compacts
the heap, and what it leaves behind at the old address is not zeroed by anything
this code can call. So a copy of the key may remain in memory the process no
longer considers live, until that memory is written over by something else. The
same applies to any copy the operating system made: a page written to swap, or a
crash dump, holds whatever was in it at the time.

Read that as: destroying a key stops this plugin from using it and shortens the
period in which it is trivially recoverable from the running process. It is not
a guarantee that the bytes are gone from the machine.

## What is NOT decided in this document, and where it is

Each of these is a real gap, open, and named so that reading this file is not
mistaken for reading a finished design.

- **What protects the file at rest.** Nothing in this plugin encrypts it. The
  keys are on disk in a form anybody who can read the file can use. Issue #31 is
  where what protects it, and what does not, is answered.
- **Atomic writes and concurrent access.** A write is a read of the file, a
  change, and a write back. It is not atomic, and it is not safe against a
  second writer in another process. Issue #34 is where that is fixed.
- **The file's permissions, and creating it.** The directory is created when
  something is first written and the file gets whatever permissions the platform
  gives it. Issue #35 owns creating the store lazily and with the right
  permissions from the first byte.
- **A restored, copied or corrupt store.** A file that does not parse currently
  throws rather than being answered for, and a store restored from a backup or
  copied to a second server is not detected. Issue #33 owns all three.
