# Valheim Serverside Simulations

[![Build Plugin](https://github.com/ddormer/valheim-serverside/actions/workflows/build-plugin.yml/badge.svg)](https://github.com/ddormer/valheim-serverside/actions/workflows/build-plugin.yml)

![foo](https://github.com/ddormer/valheim-serverside/blob/main/ss-gh.png)

Run world and monster simulations on a **dedicated server**.

Updated for patch: 0.203.11

### Features
- Server simulates world and AI physics.
- Client FPS improvements
- Ships are simulated by the driver to improve the steering experience with high latency.

### Installation

 1. Install BepInEx (optionally installing "Better Networking" on both clients and the server is recommended)
 2. Copy plugin DLL into the BepInEx/plugins/ directory on your dedicated server.
 3. You're done! No client-side changes are needed.

### Configuration

- MaxObjectsPerFrame.MaxObjects can be increased to improve the loading times of areas on the server, at the expense of CPU usage.

### Caveats

- The mod dramatically increases server resource usage and may lead to a poor gameplay experience.
- The mod should be disabled when using the "optterrain" command. 
- Only runs on dedicated servers.
- This mod does not prevent cheating or any kind of client manipulation.
- While this mod is quite light on complexity, as with most mods it's possible future Valheim patches will break the mod in unexpected ways. We recommend you back up your characters and worlds and/or consider disabling this mod anytime a new game patch is released.

### Why?

Ordinarily, to keep server resource usage low, the Valheim server will hand off simulation of an area to the first client that enters said area. However, if the player in charge of the area has a poor connection all other players in that area will suffer. This mod is an attempt at improving that specific situation at the cost of increased latency for the client which would ordinarily own the area.

### How?

This dedicated server mod causes terrain, monsters and other objects that are normally created and owned by clients to instead be created on—and thus owned and simulated by—the server.

#### For mod developers - compatibility with Serverside Simulations

For mod developers interested in maintaining compatibility with Serverside Simulations:
- If your mod makes changes relating to simulation / behaviour of the world, it will need to be able run on the dedicated server and should take these points into account:
  - Player.m_localPlayer is always `null` on a dedicated server; your code should check for this.
  - On a dedicated server, `ZNet.instance.GetReferencePosition()` returns a position outside of the world and is not related to any player position.
  - Any graphical or hud-related code should probably be behind a `ZNet.instance.IsDedicated()` check, if that code is expected to run on the server.

### Manually compiling

To manually compile, create a file at `src/valheim/Environment.props` with the following content, and change the path to point at your Valheim install.

```
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <!-- Needs to be your path to the base Valheim folder -->
    <VALHEIM_DEDI_INSTALL>E:\SteamLibrary\steamapps\common\Valheim dedicated server</VALHEIM_DEDI_INSTALL>
  </PropertyGroup>
</Project>
```
