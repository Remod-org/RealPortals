# RealPortals

## Command

 - portal -- Has subcommands as show below
 If typed by itself with no arguments, it will show details about the portal in front of the user

 - portal name {NEWNAME} -- Assign a name to the portal
 - portal all {true/false} -- Set access to all players
 - portal friends {true/false} -- Allow friends/clans/teams to access the portal
 - portal bi {true/false} -- Set the portal to be bidirectional or not (default is true)
 - portal swap -- Swap the ends of the portal so that exit becomes entrance, etc.  Only useful for unidirectional portals.
 - portal tool -- Issue the Gary's Mod Toolgun to the user so that they can create and destroy portals.

## The way of the gun

 1. If a player has a toolgun and the realportals.gun permission. They can manage portals.
 2. If a player does not have a toolgun, but they do have the permission, they can deploy one using 'portal tool'.
 3. To create a portal, first go to the location of the portal entrance, or first location.

## Configuration
```json
{
  "requirePermission": false,
  "useFriends": true,
  "useClans": true,
  "useTeams": true,
  "Version": {
    "Major": 1,
    "Minor": 0,
    "Patch": 2
  }
}
```

 - requirePermission -- If true, users must have the realportals.use permission to use portals
 - useFriends -- Use a friends plugin to manage access to shared portals
 - useClans -- Use a clans plugin to manage access to shared portals
 - useTeams -- Use Rust teams to manage access to shared portals

## Permissions

 - realportals.use -- Required to use plugin-defined portals
 - realportals.gun -- Required to manage portals including the issuance of a portal gun

