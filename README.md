# Valheim Serverside

Run world and monster simulations on a **dedicated server**.

### Caveats

- This mod dramatically increases server resource usage and may lead to  experience.
- Only runs on dedicated servers.
- This mod does not prevent cheating or any kind of client manipulation.

### Why?

Traditionally Valheim will simulate the world on the client that first enters an area which keeps server resource usage low; However if the player that is in charge of the area has a poor connection, then all other players in that area will suffer. This mod is an attempt at improving that specific situation at the cost of the the client that owns the area having increased latency.

### How?

The dedicated server mod will forcefully take control of the objects in the world and simulate them.

### Known issues

- When a player uses a teleporter, other players nearby may momentarily see the player appear on their location. This does not effect the mechanics of teleportation and appears to only be a visual glitch.
