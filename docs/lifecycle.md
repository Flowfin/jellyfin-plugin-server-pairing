# Disable, uninstall, reinstall

What each of the three leaves behind, what this plugin does about it, and what an
operator has to do by hand. The honest part is stated first: a plugin cannot
guarantee cleanup on uninstall, so this document names the file and what deleting
it means rather than promising something nothing can keep.

## The two directories, and why the answer follows from them

Everything below turns on the plugin living in one directory and its key store in
another. Read at the two server lines this plugin builds against rather than
assumed:

    for t in v10.11.9 v12.0-rc3; do gh api -H "Accept: application/vnd.github.raw" \
      "repos/jellyfin/jellyfin/contents/Emby.Server.Implementations/AppBase/BaseApplicationPaths.cs?ref=$t" \
      | grep -nE 'DataPath = |PluginsPath =>'; done
    -- v10.11.9 --
    31:            ProgramDataPath = programDataPath;
    36:            DataPath = Directory.CreateDirectory(Path.Combine(ProgramDataPath, "data")).FullName;
    58:        public string PluginsPath => Path.Combine(ProgramDataPath, "plugins");
    -- v12.0-rc3 --
    31:            ProgramDataPath = programDataPath;
    36:            DataPath = Directory.CreateDirectory(Path.Combine(ProgramDataPath, "data")).FullName;
    58:        public string PluginsPath => Path.Combine(ProgramDataPath, "plugins");

Two siblings under the program data path, and the same two at both lines. The
assembly is installed under `plugins`; the key store is built under `data`, from
the host's own paths rather than from anything written down here:

    git grep -n 'DirectoryName = \|FileName = \|FileFor' -- Jellyfin.Plugin.ServerPairing/KeyStore/KeyStorePath.cs
    Jellyfin.Plugin.ServerPairing/KeyStore/KeyStorePath.cs:30:    public const string DirectoryName = "server-pairing";
    Jellyfin.Plugin.ServerPairing/KeyStore/KeyStorePath.cs:35:    public const string FileName = "keys.json";
    Jellyfin.Plugin.ServerPairing/KeyStore/KeyStorePath.cs:56:    public static string FileFor(IApplicationPaths paths) => Path.Combine(DirectoryFor(paths), FileName);

So the file an operator is being told about is
`<program data>/data/server-pairing/keys.json`, and the program data path is the
server's own and differs per installation. [`keystore.md`](keystore.md) argues
why the store is not the plugin configuration; that argument is not repeated
here.

## Disabled

The server stops loading the assembly. This plugin then answers nothing, so a
peer that had a pairing with this server sees its requests fail rather than being
refused for a reason, and the operator on the far side sees a broken pairing
rather than a revoked one. That difference is worth saying out loud, because from
the far end an outage and an ending look the same until somebody asks.

The key store stays where it is. Nothing deletes it, because nothing runs to
delete it. Re-enabling resumes with the same pairings.

**The operator action:** none, if the disabling was deliberate. If the far side
should stop waiting, revoke before disabling rather than after, because a
disabled plugin cannot attempt the courtesy notification a revocation makes.

## Uninstalled

The host deletes the plugin's own directory, recursively:

    gh api -H "Accept: application/vnd.github.raw" \
      "repos/jellyfin/jellyfin/contents/Emby.Server.Implementations/Plugins/PluginManager.cs?ref=v10.11.9" \
      | sed -n '649,656p'
            private bool DeletePlugin(LocalPlugin plugin)
            {
                // Attempt a cleanup of old folders.
                try
                {
                    Directory.Delete(plugin.Path, true);
                    _logger.LogDebug("Deleted {Path}", plugin.Path);
                }

`plugin.Path` is under the plugins directory and the key store is under the data
directory, so **the key store survives an uninstall**. That is right for an
operator who is reinstalling and wrong for one who is finished, and neither of
them is told which they are getting.

**For an operator who is finished with this plugin:** delete
`<program data>/data/server-pairing/` after uninstalling. Deleting it destroys
the key material for every pairing this server held. Nothing else reads that
directory. It does not end the pairing on the peer: the peer keeps its own key
and its own record, and the peer's operator ends their side. Revoke each pairing
before uninstalling if the far side should learn about it, because a plugin the
host has deleted sends nothing.

**For an operator who is reinstalling:** nothing. Leave the directory alone and
the pairings come back with the plugin.

## The hook the host offers, and that this plugin does not take

An uninstall is not entirely silent to a plugin, and this document says so rather
than repeating the easier claim that it is. The server asks the instance before
it removes anything:

    gh api -H "Accept: application/vnd.github.raw" \
      "repos/jellyfin/jellyfin/contents/Emby.Server.Implementations/Updates/InstallationManager.cs?ref=v10.11.9" \
      | sed -n '395p'
                plugin.Instance?.OnUninstalling();

and the method is on the base class at both lines:

    for t in v10.11.9 v12.0-rc3; do gh api -H "Accept: application/vnd.github.raw" \
      "repos/jellyfin/jellyfin/contents/MediaBrowser.Common/Plugins/BasePlugin.cs?ref=$t" \
      | grep -n 'OnUninstalling'; done
    -- v10.11.9 --
    76:        public virtual void OnUninstalling()
    -- v12.0-rc3 --
    76:        public virtual void OnUninstalling()

This plugin does not override it:

    git grep -n 'OnUninstalling' -- Jellyfin.Plugin.ServerPairing ; echo "exit=$?"
    exit=1

Empty output, exit one. What overriding it would buy is a decision rather than an
omission, and it is not taken here: it would run inside the uninstall the
operator asked for, on a server that may be about to restart, and what it should
do - destroy the store, notify every peer, or neither - is the question under
`## Uninstalled` above. It belongs with the revocation work rather than inside a
document.

## Reinstalled over a surviving store

The pairings come back, because the key material was never removed. That is a
feature and it is also a surprise: an operator who uninstalled to clear a problem
and reinstalled gets the same pairings back, including one they thought they had
got rid of.

The plugin is meant to say so on first load rather than presenting them as though
nothing happened. It does not, and nothing in the tree could: no code runs at
startup, and the registrator adds services and reads nothing.

    git grep -c 'AddSingleton' -- Jellyfin.Plugin.ServerPairing/PluginServiceRegistrator.cs
    Jellyfin.Plugin.ServerPairing/PluginServiceRegistrator.cs:5

Five registrations and no reader among them.

**The operator action:** if the pairings coming back is not what was wanted,
delete the directory named above and reinstall. There is no in-plugin way to do
it today.

## What this document does not settle

The two conditions of issue #58 that are not a document. A plugin starting
against a store with existing pairings does not report that it found them, and
nothing asserts that a normal shutdown deletes nothing from the store, because
neither a startup path nor a shutdown path exists. Whether that path belongs to
#58 or to whichever milestone first needs one is not settled here.

What a pairing's data on the peer costs when this side vanishes is
[`data.md`](data.md) and is not repeated here. This document is about the file on
this machine.
