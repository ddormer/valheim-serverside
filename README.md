# Valheim Serverside Simulations

Run world and monster simulations on a **dedicated server**.

Updated for patch: 0.148.7

### Caveats

- This mod dramatically increases server resource usage and may lead to a poor gameplay experience.
- While this mod is quite light on complexity, as with most mods it's possible future Valheim patches will break the mod in unexpected ways. We recommend you back up your characters and worlds and/or consider disabling this mod anytime a new game patch is released.
- Only runs on dedicated servers.
- This mod does not prevent cheating or any kind of client manipulation.

### Installation

 1. Install BepInEx (link in the requirements above)
 2. Copy plugin DLL into the BepInEx/plugins/ directory on your dedicated server.
 3. You're done! No client-side changes are needed.

### Why?

Ordinarily, to keep server resource usage low, the Valheim server will hand off simulation of an area to the first client that enters said area. However, if the player in charge of the area has a poor connection all other players in that area will suffer. This mod is an attempt at improving that specific situation at the cost of increased latency for the client which would ordinarily own the area.

### How?

This dedicated server mod causes terrain, monsters and other objects that are normally created and owned by clients to instead be created on—and thus owned and simulated by—the server.

### Known issues

- When a player goes through a portal, other players nearby may momentarily see the player appear on their location. This does not effect the mechanics of teleportation and appears to only be a visual glitch.

#### For mod developers - compatibility with Serverside Simulations

For mod developers interested in maintaining compatibility with Serverside Simulations:

- If your mod makes changes relating to simulation / behaviour of the world, it will need to be able run on the dedicated server and should take these points into account:
  - Player.m_localPlayer is always `null` on a dedicated server; your code should check for this.
  - On a dedicated server, `ZNet.instance.GetReferencePosition()` returns a position outside of the world and is not related to any player position.
  - Any graphical or hud-related code should probably be behind a `ZNet.instance.IsDedicated()` check, if that code is expected to run on the server.
  