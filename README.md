# Valheim Serverside Simulations

Run world and monster simulations on a **dedicated server**.

Updated for patch: 0.148.6

### Caveats

- This mod dramatically increases server resource usage and may lead to a poor gameplay experience.
- Likely to break future game patches, please consider disabling this mod anytime a new game patch is released.
- Only runs on dedicated servers.
- This mod does not prevent cheating or any kind of client manipulation.

### Installation

 1. Install BepInEx (link in the requirements above)
 2. Copy plugin DLL into the BepInEx/plugins/ directory on your dedicated server.
 3. You're done! No client-side changes are needed.

### Why?

Ordinarily, to keep server resource usage low, Valheim will hand off simulation of an area to the first client that enters said area. However, if the player in charge of the area has a poor connection all other players in that area will suffer. This mod is an attempt at improving that specific situation at the cost of increased latency for the client which would ordinarily own the area.

### How?

The dedicated server mod will take control of the objects in the world, allowing the server to simulate them.

### Known issues

- When a player goes through a portal, other players nearby may momentarily see the player appear on their location. This does not effect the mechanics of teleportation and appears to only be a visual glitch.

#### For mod developers - compatibility with Serverside Simulations

For mod developers that are interested in maintaining compatibility with Serverside Simulations; The server often ends up owning ZDOs and terrain, thus any simulation code that would usually run on the client would also need to run on the server. Code using localPlayer or ZNet.instance.getReferencePosition() won't work on the server as there is no localPlayer and the reference position of the server is some made-up value, if your simulation code relies on player position then finding it from the connected peers might be a solution. Any graphical or hud-related code should probably be behind a ZNet.instance.IsDedicated() check, if that code is expected to run on the server.