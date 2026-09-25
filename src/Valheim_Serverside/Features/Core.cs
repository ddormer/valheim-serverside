using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using OpCode = System.Reflection.Emit.OpCode;
using OpCodes = System.Reflection.Emit.OpCodes;


namespace Valheim_Serverside.Features
{
	public class Core : IFeature
	{
		public bool FeatureEnabled()
		{
			return true;
		}

		public static bool IsServer()
		{
			return ZNet.instance && ZNet.instance.IsServer();
		}

		public static void PrintLog(string text)
		{
			System.Diagnostics.Trace.WriteLine(text);
		}

		public static void PrintLog(object[] obj)
		{
			System.Diagnostics.Trace.WriteLine(string.Concat(obj));
		}

		// Defensive wrappers around Traverse. A direct method/field reference
		// gets checked by the compiler on every game update; a Traverse-based
		// reflection call does not -- and Traverse doesn't throw when a name
		// or argument-type mismatch means it can't find the target, it just
		// silently returns a default value (this is exactly how the
		// IsInPeerActiveArea signature change went undetected until a player
		// reported broken item pickup, with zero exceptions anywhere in the
		// logs). These log loudly instead, so a future game update that
		// breaks any of these calls announces itself immediately.
		private static void LogIfMissing(bool exists, object instance, string name)
		{
			if (!exists)
			{
				ServersidePlugin.logger.LogError($"Reflection target missing/mismatched: {instance?.GetType().Name}.{name} -- the game API likely changed and this patch needs updating.");
			}
		}

		private static Traverse SafeMethod(object instance, string name, params object[] args)
		{
			var t = Traverse.Create(instance).Method(name, args);
			LogIfMissing(t.MethodExists(), instance, name);
			return t;
		}

		private static Traverse SafeMethod(object instance, string name, Type[] paramTypes, object[] args)
		{
			var t = Traverse.Create(instance).Method(name, paramTypes, args);
			LogIfMissing(t.MethodExists(), instance, name);
			return t;
		}

		private static Traverse SafeField(object instance, string name)
		{
			var t = Traverse.Create(instance).Field(name);
			LogIfMissing(t.FieldExists(), instance, name);
			return t;
		}

		// Null-safe substitutes for Player.m_localPlayer reads found inside
		// vanilla methods the server reaches once it owns objects (see
		// Pickable_RPC_Pick_Patch below for the full story of why this
		// class of bug exists). Player.m_localPlayer is always null on a
		// dedicated server -- these return the same "nobody" values vanilla
		// itself already falls back to in the spots that DO null-check
		// first (e.g. Container.RPC_OpenResponse).
		private static class LocalPlayerSafety
		{
			public static ZDOID SafeZDOID()
			{
				return Player.m_localPlayer != null ? Player.m_localPlayer.GetZDOID() : ZDOID.None;
			}

			public static long SafePlayerID()
			{
				return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : -1L;
			}

			public static string SafePlayerName()
			{
				return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "Server";
			}
		}

		[HarmonyPatch(typeof(Pickable), "RPC_Pick")]
		public static class Pickable_RPC_Pick_Patch
		/*
			RPC_Pick throws a NullReferenceException whenever it executes on a
			headless dedicated server -- confirmed live (Exception in
			ZRpc::HandlePackage, NullReferenceException inside RPC_Pick's own
			DMD wrapper) and via IL disassembly of the real method body:
			it unconditionally does `Player.m_localPlayer.GetZDOID()` to
			attribute a pickup visual/audio effect. Player.m_localPlayer is
			always null on a dedicated server -- there's no local player
			there, ever, with or without this mod. Under vanilla peer-hosted
			play this never matters, because RPC_Pick only ever runs on
			whichever player's own client already owns the object. This mod
			moves ownership of persistent objects (including ground items)
			to the server, so a Pickable owned by the server is the one
			context where RPC_Pick actually executes headless -- and always
			crashes, silently to the player (the client just sees nothing
			happen and keeps retrying).

			Fix: a transpiler that replaces just the crashing two-instruction
			sequence (ldsfld Player::m_localPlayer; callvirt
			Character::GetZDOID()) with a call to a null-safe equivalent,
			leaving 100% of the rest of vanilla's method (item drops,
			aggravate, the RPC_SetPicked broadcast that actually removes the
			item for everyone) untouched and unduplicated.
		*/
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var m_localPlayerField = AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer));
				var getZDOID = AccessTools.Method(typeof(Character), nameof(Character.GetZDOID));
				var safeMethod = AccessTools.Method(typeof(LocalPlayerSafety), nameof(LocalPlayerSafety.SafeZDOID));

				var codes = new List<CodeInstruction>(instructions);
				for (int i = 0; i < codes.Count - 1; i++)
				{
					if (codes[i].opcode == OpCodes.Ldsfld && codes[i].OperandIs(m_localPlayerField)
						&& codes[i + 1].opcode == OpCodes.Callvirt && codes[i + 1].OperandIs(getZDOID))
					{
						// Preserve any labels (branch targets) attached to the
						// instruction being replaced -- see
						// Ship_UpdateSailSize_Patch for why this matters.
						codes[i] = new CodeInstruction(OpCodes.Call, safeMethod) { labels = codes[i].labels };
						codes.RemoveAt(i + 1);
						break;
					}
				}
				return codes;
			}
		}

		[HarmonyPatch(typeof(Ship), "UpdateSailSize")]
		public static class Ship_UpdateSailSize_Patch
		/*
			UpdateSailSize throws a NullReferenceException on the server
			whenever a sail hasn't yet reached its target position and was
			previously "in position" -- confirmed via IL disassembly: it
			unconditionally reads Player.m_localPlayer twice while building
			a ZDOID used only for a debug log line and a cosmetic effect.
			The crash happens BEFORE the actual sail-position Lerp/cloth
			update later in the same method, so on a dedicated server a sail
			that starts moving never finishes -- the exception refires every
			physics tick until it does (never).

			Fix: same transpiler approach as Pickable_RPC_Pick_Patch -- swap
			both Player.m_localPlayer reads for the shared null-safe
			equivalents, leaving the actual sail-position update untouched.
		*/
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var m_localPlayerField = AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer));
				var getPlayerID = AccessTools.Method(typeof(Player), nameof(Player.GetPlayerID));
				var getZDOID = AccessTools.Method(typeof(Character), nameof(Character.GetZDOID));
				var safeID = AccessTools.Method(typeof(LocalPlayerSafety), nameof(LocalPlayerSafety.SafePlayerID));
				var safeZDOID = AccessTools.Method(typeof(LocalPlayerSafety), nameof(LocalPlayerSafety.SafeZDOID));

				var codes = new List<CodeInstruction>(instructions);
				for (int i = 0; i < codes.Count - 1; i++)
				{
					if (codes[i].opcode != OpCodes.Ldsfld || !codes[i].OperandIs(m_localPlayerField))
					{
						continue;
					}

					// One of these two ldsfld instructions (the second, in
					// practice) is itself a branch target -- replacing it
					// with a plain `new CodeInstruction(...)` drops its
					// .labels, orphaning whatever jumps there and producing
					// an invalid method ("Label #N is not marked"). Carry
					// the original instruction's labels over explicitly.
					if (codes[i + 1].opcode == OpCodes.Callvirt && codes[i + 1].OperandIs(getPlayerID))
					{
						var replacement = new CodeInstruction(OpCodes.Call, safeID) { labels = codes[i].labels };
						codes[i] = replacement;
						codes.RemoveAt(i + 1);
					}
					else if (codes[i + 1].opcode == OpCodes.Callvirt && codes[i + 1].OperandIs(getZDOID))
					{
						var replacement = new CodeInstruction(OpCodes.Call, safeZDOID) { labels = codes[i].labels };
						codes[i] = replacement;
						codes.RemoveAt(i + 1);
					}
				}
				return codes;
			}
		}

		[HarmonyPatch(typeof(CookingStation), "SpawnItem")]
		public static class CookingStation_SpawnItem_Patch
		/*
			SpawnItem throws a NullReferenceException on the server whenever
			m_recordCrafter is true (e.g. the Frost Foundry) -- confirmed via
			IL disassembly: it unconditionally reads
			Player.m_localPlayer.GetPlayerID()/GetPlayerName() to attribute
			the crafted item. The exception fires AFTER the item has already
			been instantiated, aborting the method before whatever clears
			the cooking slot afterward runs -- duplicating the item every
			time the station is used on the server.

			Fix: same transpiler approach as Pickable_RPC_Pick_Patch --
			replace both Player.m_localPlayer reads with the shared
			null-safe equivalents (crafter attribution becomes "Server"
			instead of a real player, the same tradeoff vanilla itself
			already makes elsewhere), letting the method run to completion
			so the slot actually clears and the item stops duplicating.
		*/
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var m_localPlayerField = AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer));
				var getPlayerID = AccessTools.Method(typeof(Player), nameof(Player.GetPlayerID));
				var getPlayerName = AccessTools.Method(typeof(Player), nameof(Player.GetPlayerName));
				var safeID = AccessTools.Method(typeof(LocalPlayerSafety), nameof(LocalPlayerSafety.SafePlayerID));
				var safeName = AccessTools.Method(typeof(LocalPlayerSafety), nameof(LocalPlayerSafety.SafePlayerName));

				var codes = new List<CodeInstruction>(instructions);
				for (int i = 0; i < codes.Count - 1; i++)
				{
					if (codes[i].opcode != OpCodes.Ldsfld || !codes[i].OperandIs(m_localPlayerField))
					{
						continue;
					}

					// Preserve any labels (branch targets) attached to the
					// instruction being replaced -- see
					// Ship_UpdateSailSize_Patch for why this matters.
					if (codes[i + 1].opcode == OpCodes.Callvirt && codes[i + 1].OperandIs(getPlayerID))
					{
						var replacement = new CodeInstruction(OpCodes.Call, safeID) { labels = codes[i].labels };
						codes[i] = replacement;
						codes.RemoveAt(i + 1);
					}
					else if (codes[i + 1].opcode == OpCodes.Callvirt && codes[i + 1].OperandIs(getPlayerName))
					{
						var replacement = new CodeInstruction(OpCodes.Call, safeName) { labels = codes[i].labels };
						codes[i] = replacement;
						codes.RemoveAt(i + 1);
					}
				}
				return codes;
			}
		}

		[HarmonyPatch(typeof(Leviathan), "RPC_Left")]
		public static class Leviathan_RPC_Left_Patch
		/*
			RPC_Left throws a NullReferenceException on the server --
			confirmed via IL disassembly: it unconditionally reads
			Player.m_localPlayer.transform.position purely to decide whether
			to increment a per-client stat. There's no local player on a
			dedicated server to increment a stat for, so unlike
			Ship/CookingStation above this doesn't need a transpiler --
			skipping the whole method when headless matches vanilla's own
			convention elsewhere (e.g. Container.RPC_OpenResponse already
			returns immediately when Player.m_localPlayer is null).
		*/
		{
			static bool Prefix()
			{
				return Player.m_localPlayer != null;
			}
		}

		[HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
		public class CreateDestroyObjects_Patch
		/*
			The bread and butter of the mod, this patch facilitates spawning objects on the server.

			Creates and destroys ZDOs by finding all objects in each peer area.

			Some object overlap can happen if peers are close to each other, the objects are
			deduplicated by using a HashSet, see `List.Distinct`.

			This method originally works only with objects surrounding `ZNet.GetReferencePosition()` which returns some
			made-up nonsense on a dedicated server.

			DistantObjects: Are objects that have `m_distant` set to `true`, set (probably) in the prefab data;
			Distant objects are not affected by draw distance.

			CreateObjects: Makes no distinction between objects and nearby-objects except in the order
						   they are created.
		
			RemoveObjects: Marks all ZDOs for deletion by setting the current frame number on the ZDO,
						   and then checks if any of the ZDOs marked for deletion have an older/different
						   frame number.
		*/
		{
			private static bool Prefix(ZNetScene __instance)
			{
				List<ZDO> m_tempCurrentObjects = new List<ZDO>();
				List<ZDO> m_tempCurrentDistantObjects = new List<ZDO>();
				// Vanilla reads the synced value from ZNet here, not ZoneSystem's own
				// copy -- using ZoneSystem.instance.m_simulationDistance instead was
				// silently widening (or otherwise mismatching) the active/create area
				// versus what vanilla intends, sweeping in objects that shouldn't have
				// counted as "nearby" yet. Confirmed against another fork of this mod
				// that got this right from the start.
				SimulationDistance simulationDistance = ZNet.instance.GetSyncedSimulationDistance();
				foreach (ZNetPeer znetPeer in ZNet.instance.GetConnectedPeers())
				{
					// 1.0: ZoneSystem.GetZone now returns Vector2s (Vector2i is
					// gone entirely), and FindSectorObjects takes a single
					// SimulationDistance instead of separate near/far ints
					// (ZoneSystem.m_activeArea/m_activeDistantArea no longer exist).
					Vector2s zone = ZoneSystem.GetZone(znetPeer.GetRefPos());
					ZDOMan.instance.FindSectorObjects(zone, simulationDistance, m_tempCurrentObjects, m_tempCurrentDistantObjects);
				}

				m_tempCurrentDistantObjects = m_tempCurrentDistantObjects.Distinct().ToList();
				m_tempCurrentObjects = m_tempCurrentObjects.Distinct().ToList();
				SafeMethod(__instance, "CreateObjects", m_tempCurrentObjects, m_tempCurrentDistantObjects).GetValue();
				SafeMethod(__instance, "RemoveObjects", m_tempCurrentObjects, m_tempCurrentDistantObjects).GetValue();
				return false;
			}
		}

		[HarmonyPatch(typeof(ZoneSystem), "IsActiveAreaLoaded")]
		public static class ZoneSystem_IsActiveAreaLoaded_Patch
		{
			private static bool Prefix(ZoneSystem __instance, ref bool __result, Dictionary<Vector2s, dynamic> ___m_zones)
			{
				foreach (ZNetPeer peer in ZNet.instance.GetPeers())
				{
					Vector2s zone = ZoneSystem.GetZone(peer.GetRefPos());
					int activeArea = __instance.m_simulationDistance.NearSimulationDistance;
					for (int i = zone.y - activeArea; i <= zone.y + activeArea; i++)
					{
						for (int j = zone.x - activeArea; j <= zone.x + activeArea; j++)
						{
							if (!___m_zones.ContainsKey(new Vector2s(j, i)))
							{
								__result = false;
								return false;
							}
						}
					}
				}
				__result = true;
				return false;
			}
		}

		[HarmonyPatch(typeof(ZoneSystem), "Update")]
		public static class ZoneSystem_Update_Patch
		/*
			Creates Local-Zones for each peer position. Enabling simulation to be handled by the server.

			Original method: tries to create a Local-Zone for the position the player is standing in,
			if this is a server then a Ghost-Zone is created for the current reference position as well
			as for each peer's position.

			Local-Zone: Created on every player's client, container for things like terrain and vegetation.
			Ghost-Zone: Created only on the server, unsimulated (associated GameObjects are destroyed), used
						only to send associated information to clients.
		*/
		{
			// Which peer the round-robin zone budget (below) picks up from next tick.
			private static int s_nextPeerIndex;

			static bool Prefix(ZoneSystem __instance, ref float ___m_updateTimer)
			{
				if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
				{
					return false;
				}

				___m_updateTimer += Time.deltaTime;
				if (___m_updateTimer > 0.1f)
				{
					___m_updateTimer = 0f;
					// original flag line removed, as well as the check for it as it always returns `false` on the server.
					//bool flag = Traverse.Create(__instance).Method("CreateLocalZones", ZNet.instance.GetReferencePosition()).GetValue<bool>();
					SafeMethod(__instance, "UpdateTTL", 0.1f).GetValue();
					if (ZNet.instance.IsServer()) // && !flag)
					{
						//Traverse.Create(__instance).Method("CreateGhostZones", ZNet.instance.GetReferencePosition()).GetValue();
						//UnityEngine.Debug.Log(String.Concat(new object[] { "CreateLocalZones for", refPoint.x, " ", refPoint.y, " ", refPoint.z }));
						// A zone is generated in full in one frame (terrain, vegetation,
						// locations), so giving every peer a zone the same tick spikes
						// whenever several players explore at once. Budget at most
						// MaxZonesPerTick actual generations per tick, peers taking
						// turns starting from whoever was skipped last time -- a peer
						// that already has a current zone doesn't spend any of the
						// budget (CreateLocalZones returns false for it), so it never
						// starves an exploring player waiting behind a stationary one.
						List<ZNetPeer> peers = ZNet.instance.GetPeers();
						int zoneBudget = Configuration.maxZonesPerTick.Value > 0 ? Configuration.maxZonesPerTick.Value : peers.Count;
						int peerCount = peers.Count;
						int generated = 0;
						for (int n = 0; n < peerCount && generated < zoneBudget; n++)
						{
							int index = (s_nextPeerIndex + n) % peerCount;
							if (SafeMethod(__instance, "CreateLocalZones", peers[index].GetRefPos()).GetValue<bool>())
							{
								generated++;
								s_nextPeerIndex = (index + 1) % peerCount;
							}
						}
					}
					// Vanilla calls this every tick to decrement each loaded
					// location prefab's lifetime countdown and Release() it
					// once expired. This patch never called it, so
					// m_locationPrefabs only ever grows -- an unbounded leak
					// of every location prefab loaded since boot, worse the
					// more of the map gets explored. Confirmed via IL
					// disassembly of ZoneSystem.UpdatePrefabLifetimes.
					SafeMethod(__instance, "UpdatePrefabLifetimes").GetValue();
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(ZoneSystem), "UpdatePrefabLifetimes")]
		public static class ZoneSystem_UpdatePrefabLifetimes_Patch
		/*
			Vanilla decrements every loaded-location-prefab's lifetime
			countdown and Release()s+removes it once expired, with no regard
			for whether the underlying SoftReferenceableAssets load has
			actually finished yet (LocationPrefabLoadData.IsLoaded only
			becomes true via an OnPrefabLoaded/OnRoomLoaded async callback).

			Confirmed live: on this dedicated server, some locations' prefab
			loads never complete -- IsLoaded stays false forever, apparently
			because a headless server never receives that callback for these.
			Restoring the call above to UpdatePrefabLifetimes (to fix the
			m_locationPrefabs leak) meant vanilla's blind countdown now
			evicts that in-flight, never-loaded entry before it ever loads --
			and PokeCanSpawnLocation just creates a brand new one on the very
			next retry. Result: the same location gets requested, evicted,
			and re-requested forever. Confirmed in the live log: one location
			alone re-triggered 106+ times over two hours, continuously,
			generating constant ZDO ownership churn (repeating "Server
			claimed ownership of LocationProxy/*LocationMusic" at the same
			coordinates) and, per player reports, real lag -- coinciding
			with when the UpdatePrefabLifetimes fix went live.

			Fix: only count down/evict entries that have actually finished
			loading. A truly-loaded-and-idle prefab still gets released on
			schedule (preserving the original leak fix); an in-flight load
			is left alone instead of being evicted mid-flight.

			Residual gap this closes: an entry whose load never completes at
			all (confirmed live -- some locations' IsLoaded simply never
			becomes true on this headless server) is *permanently* skipped by
			the `!entry.IsLoaded` guard above -- nothing ever counts it down
			or releases it, so it sits in m_locationPrefabs forever. Every
			session leaves some fraction of these behind, and unlike the
			churn-driven memory use elsewhere (proportional to explored area,
			confirmed to plateau when exploration stops), none of these are
			ever given back -- a slow, permanent leak on top of that. IL
			confirms LocationPrefabLoadData is a class (extends
			System.Object), so the m_iterationLifetime decrement above
			correctly persists for entries that DO load; this gap is
			specifically about ones that never do.

			Fix: track how long each not-yet-loaded entry has sat unfinished
			(keyed by the entry's own identity via ConditionalWeakTable, so
			this never itself keeps an otherwise-dead entry alive). Past
			StuckLocationPrefabTimeoutSeconds (default 5 minutes -- generous,
			well beyond any legitimate slow load), force-release and drop it
			anyway, same as vanilla's own unconditional Release() on eviction,
			just bounded instead of immediate. Logged so this is observable
			rather than silent.
		*/
		{
			private static readonly ConditionalWeakTable<ZoneSystem.LocationPrefabLoadData, StrongBox<float>> s_unloadedSince = new ConditionalWeakTable<ZoneSystem.LocationPrefabLoadData, StrongBox<float>>();
			private static float s_lastCountLogTime;

			static bool Prefix(List<ZoneSystem.LocationPrefabLoadData> ___m_locationPrefabs, List<int> ___m_tempLocationPrefabsToRelease)
			{
				float now = Time.time;
				float timeout = Configuration.stuckLocationPrefabTimeout.Value;

				for (int i = 0; i < ___m_locationPrefabs.Count; i++)
				{
					var entry = ___m_locationPrefabs[i];
					if (!entry.IsLoaded)
					{
						if (timeout <= 0f)
						{
							continue;
						}
						if (s_unloadedSince.TryGetValue(entry, out var since))
						{
							if (now - since.Value > timeout)
							{
								___m_tempLocationPrefabsToRelease.Add(i);
								s_unloadedSince.Remove(entry);
								ServersidePlugin.logger.LogWarning($"UpdatePrefabLifetimes: force-releasing a location prefab load stuck unfinished for over {timeout}s -- would otherwise have accumulated in memory permanently.");
							}
						}
						else
						{
							s_unloadedSince.Add(entry, new StrongBox<float>(now));
						}
						continue;
					}
					s_unloadedSince.Remove(entry);
					entry.m_iterationLifetime--;
					if (entry.m_iterationLifetime <= 0)
					{
						___m_tempLocationPrefabsToRelease.Add(i);
					}
				}
				for (int i = ___m_tempLocationPrefabsToRelease.Count - 1; i >= 0; i--)
				{
					int idx = ___m_tempLocationPrefabsToRelease[i];
					___m_locationPrefabs[idx].Release();
					___m_locationPrefabs.RemoveAt(idx);
				}
				___m_tempLocationPrefabsToRelease.Clear();

				// Periodic visibility into the list this whole patch exists to
				// bound -- the only direct way to confirm (rather than infer
				// from overall process memory) whether this stays flat over a
				// long session instead of quietly climbing again.
				if (now - s_lastCountLogTime > 600f)
				{
					s_lastCountLogTime = now;
					ServersidePlugin.logger.LogInfo($"UpdatePrefabLifetimes: m_locationPrefabs.Count={___m_locationPrefabs.Count}");
				}

				return false;
			}
		}

		[HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
		public static class ZDOMan_ReleaseNearbyZDOS_Patch
		/*
			Releases nearby ZDOs for a player if no other peers are nearby that player.
			If instead the nearby ZDO has no owner, set owner to server so that it simulates on the server.

			Original method:
			If ZDO is no longer near the peer, release ownership. If no owner set, change ownership to said peer.
		*/
		{
			static bool Prefix(ZDOMan __instance, ref Vector3 refPosition, ref long uid)
			{
				Vector2s zone = ZoneSystem.GetZone(refPosition);
				List<ZDO> m_tempNearObjects = SafeField(__instance, "m_tempNearObjects").GetValue<List<ZDO>>();
				m_tempNearObjects.Clear();

				// Far=0 replicates the old "near objects only" call (no separate
				// activeDistantArea param exists anymore to pass 0 for directly).
				// Reads the ZNet-synced distance, not ZoneSystem's own copy -- see
				// the matching comment in CreateDestroyObjects_Patch above.
				var currentDistance = ZNet.instance.GetSyncedSimulationDistance();
				var nearOnlyDistance = new SimulationDistance(currentDistance.NearSimulationDistance, 0, currentDistance.IsClassic);
				__instance.FindSectorObjects(zone, nearOnlyDistance, m_tempNearObjects, null);
				foreach (ZDO zdo in m_tempNearObjects)
				{
					if (zdo.Persistent)
					{
						bool anyPlayerInArea = false;
						foreach (ZNetPeer peer in ZNet.instance.GetPeers())
						{
							// InActiveArea no longer has a (Vector2s, Vector2s) overload --
							// zdo.GetSector() (now Vector2s) doesn't fit the remaining
							// (Vector3, Vector3) / (Vector3, Vector2s) signatures, so use
							// the ZDO's actual position instead of its sector coordinate.
							if (ZNetScene.InActiveArea(zdo.GetPosition(), ZoneSystem.GetZone(peer.GetRefPos())))
							{
								anyPlayerInArea = true;
								break;
							}
						}
						long zdoOwner = zdo.GetOwner();
						if (zdoOwner == uid || zdoOwner == ZNet.GetUID())
						{
							if (!anyPlayerInArea)
							{
								zdo.SetOwner(0L);
							}
						}
						else if (
							(zdoOwner == 0L
							// IsInPeerActiveArea is now (Vector3, long), not (sector, ownerId) --
							// this call went undetected at compile time (it's reflection-based),
							// and Harmony's Traverse doesn't throw on the mismatch, it just
							// silently returns false. !false == true unconditionally meant this
							// whole branch fired on every check regardless of prior ownership,
							// so the server perpetually reclaimed every nearby persistent ZDO
							// (including ground items) away from whoever validly held it --
							// looked exactly like "can't pick anything up."
							|| !SafeMethod(__instance, "IsInPeerActiveArea", zdo.GetPosition(), zdo.GetOwner()).GetValue<bool>()
							)
							&& anyPlayerInArea
						)
						{
							zdo.SetOwner(ZNet.GetUID());
							GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
							ServersidePlugin.logger.LogDebug($"ReleaseNearbyZDOS: Server claimed ownership of {(prefab != null ? prefab.name : $"prefab#{zdo.GetPrefab()}")} at {zdo.GetPosition()} (was owned by {zdoOwner})");
						}
					}
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(RandEventSystem), "FixedUpdate")]
		public static class RandEventSystem_FixedUpdate_Patch
		/*
			Replaces the check:

				if (Player.m_localPlayer && this.IsInsideRandomEventArea(this.m_randomEvent, Player.m_localPlayer.transform.position))

			with:

				if (this.IsAnyPlayerInEventArea(this.m_randomEvent))

			Player.m_localPlayer is always null on a dedicated server (there is no local player
			character), so the original check always fails and the event gets deactivated
			(SetActiveEvent(null, false)) before it can ever go active -- meaning random events
			(raids) never activate, and therefore their monsters never spawn, on a dedicated server.

			Previously this transpiler looked for an existing call to IsAnyPlayerInEventArea
			elsewhere in FixedUpdate and reused its cached boolean result instead of writing a
			fresh call. As of the game update that shipped ~2026-09-19, FixedUpdate no longer
			calls IsAnyPlayerInEventArea anywhere in its body, so that anchor never matched, this
			whole transpiler silently no-op'd, and the original m_localPlayer==null check stayed
			live -- confirmed 2026-09-24 via static IL comparison against the deployed
			assembly_valheim.dll (monodis dump of RandEventSystem::FixedUpdate showed the raw
			vanilla null-check block untouched, with no IsAnyPlayerInEventArea call anywhere in
			the method). No random event had ever gone active on this dedicated server as a
			result. Fixed by locating the null-check + IsInsideRandomEventArea block directly
			(by field/method identity, not by reusing an assumed-nearby call) and replacing it
			with a fresh IsAnyPlayerInEventArea(m_randomEvent) call, so this no longer depends on
			the vanilla method happening to call it elsewhere first.
		*/
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				FieldInfo field_m_localPlayer = AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer));
				MethodInfo opImplicitInfo = AccessTools.Method(typeof(UnityEngine.Object), "op_Implicit");
				MethodInfo isInsideRandomEventAreaInfo = AccessTools.Method(typeof(RandEventSystem), "IsInsideRandomEventArea");
				MethodInfo isAnyPlayerInfo = AccessTools.Method(typeof(RandEventSystem), "IsAnyPlayerInEventArea");
				FieldInfo field_m_randomEvent = AccessTools.Field(typeof(RandEventSystem), "m_randomEvent");

				var matcher = new CodeMatcher(instructions);

				// Find "if (Player.m_localPlayer) { ... }" -- the truthiness check on m_localPlayer.
				matcher.MatchForward(false,
					new CodeMatch(OpCodes.Ldsfld, field_m_localPlayer),
					new CodeMatch(OpCodes.Call, opImplicitInfo),
					new CodeMatch(OpCodes.Brfalse)
				);
				if (!matcher.IsValid)
				{
					ServersidePlugin.logger.LogError("RandEventSystem.FixedUpdate transpiler: m_localPlayer null-check pattern not found -- patch NOT applied, random events will not activate on this dedicated server. Game code may have changed again; needs re-diffing against the live assembly.");
					return instructions;
				}
				int startPos = matcher.Pos;

				// From there, find the IsInsideRandomEventArea(...) call and the branch right after it.
				matcher.MatchForward(false,
					new CodeMatch(OpCodes.Call, isInsideRandomEventAreaInfo)
				);
				if (!matcher.IsValid)
				{
					ServersidePlugin.logger.LogError("RandEventSystem.FixedUpdate transpiler: IsInsideRandomEventArea call not found -- patch NOT applied, random events will not activate on this dedicated server. Game code may have changed again; needs re-diffing against the live assembly.");
					return instructions;
				}
				matcher.Advance(1);
				if (!matcher.IsValid || !matcher.Instruction.Branches(out System.Reflection.Emit.Label? branchTarget) || branchTarget == null)
				{
					ServersidePlugin.logger.LogError("RandEventSystem.FixedUpdate transpiler: expected branch after IsInsideRandomEventArea call not found -- patch NOT applied, random events will not activate on this dedicated server. Game code may have changed again; needs re-diffing against the live assembly.");
					return instructions;
				}
				int endPos = matcher.Pos;

				return matcher
					.Start()
					.Advance(startPos)
					.RemoveInstructions(endPos - startPos + 1)
					.InsertAndAdvance(
						new CodeInstruction(OpCodes.Ldarg_0),
						new CodeInstruction(OpCodes.Ldarg_0),
						new CodeInstruction(OpCodes.Ldfld, field_m_randomEvent),
						new CodeInstruction(OpCodes.Call, isAnyPlayerInfo),
						new CodeInstruction(OpCodes.Brfalse, branchTarget.Value)
					)
					.InstructionEnumeration();
			}
		}

		public static List<SpawnSystem.SpawnData> GetCurrentSpawners(RandEventSystem instance, SpawnSystem spawnSystem)
		/*
			Return spawners if there are nearby players in the event area.
		*/
		{
			if (SafeField(instance, "m_activeEvent").GetValue<RandomEvent>() == null)
			{
				return null;
			}

			ZNetView spawnSystem_m_nview = SafeField(spawnSystem, "m_nview").GetValue<ZNetView>();
			RandomEvent randomEvent = SafeField(instance, "m_randomEvent").GetValue<RandomEvent>();

			foreach (Player player in Player.GetAllPlayers())
			{
				if (ZNetScene.InActiveArea(spawnSystem_m_nview.GetZDO().GetPosition(), ZoneSystem.GetZone(player.transform.position)))
				{
					if (SafeMethod(instance, "IsInsideRandomEventArea", new Type[] { typeof(RandomEvent), typeof(Vector3) }, new object[] { randomEvent, player.transform.position }).GetValue<bool>())
					{
						return instance.GetCurrentSpawners();
					}
				}
			}
			return null;
		}

		[HarmonyPatch(typeof(SpawnSystem), "UpdateSpawning")]
		public static class SpawnSystem_UpdateSpawning_Patch
		/*
			Patches out m_localPlayer == null check in SpawnSystem.UpdateSpawning
			by reversing the boolean check.

			Fixes enemies not spawning during random events.
		*/
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> _instructions)
			{
				return new CodeMatcher(_instructions)
					// Reverse Player.m_localPlayer == false check to allow function to run on dedicated server
					.MatchForward(true,
						new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer))),
						new CodeMatch(OpCodes.Ldnull),
						new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(UnityEngine.Object), "op_Equality")),
						new CodeMatch(OpCodes.Brfalse)
					)
					.SetOpcodeAndAdvance(OpCodes.Brtrue)

					// Replace RandEventSystem.GetCurrentSpawners call with call to our method.
					.MatchForward(false,
						new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(RandEventSystem), nameof(RandEventSystem.GetCurrentSpawners)))
					)
					.RemoveInstruction()
					.Insert(
						// Arg 0 is SpawnSystem instance; push to stack (2nd arg to Core.GetCurrentSpawners)
						new CodeInstruction(OpCodes.Ldarg_0),
						new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Core), nameof(Core.GetCurrentSpawners)))
					)

					.InstructionEnumeration()
				;
			}
		}

		[HarmonyPatch(typeof(ZNetScene), "OutsideActiveArea", new Type[] { typeof(Vector3) })]
		public static class ZNetScene_OutsideActiveArea_Patch
		/*
			Originally uses `ZNet.GetReferencePosition` to determine active area but with the server 
			handling all areas, it must check if the `Vector3` is within any of the peers' active areas.

			Returns `false` if the point is within *any* of the peers' active areas and `false` otherwise.

			SpawnArea (e.g BonePileSpawner) uses `OutsideActiveArea` to determine if it should be simulated.
		*/
		{
			static bool Prefix(ref bool __result, ZNetScene __instance, Vector3 point)
			{
				__result = true;
				foreach (ZNetPeer znetPeer in ZNet.instance.GetPeers())
				{
					// OutsideActiveArea(Vector3, Vector3) is gone in 1.0 -- only
					// (Vector3) and (Vector3, Vector2s) remain, so pass the peer's
					// zone instead of their raw position.
					if (!ZNetScene.OutsideActiveArea(point, ZoneSystem.GetZone(znetPeer.GetRefPos())))
					{
						__result = false;
					}
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
		public static class ZRoutedRpc_RouteRPC_Patch
		/*
			When a client requests to be the "user" (driver) of a ship this RPC method
			is sent from the current ship owner when they accept the request.
			We set the owner of the ship to the new ship driver.

			Allows players to drive ships with no roundtrip latency.
		*/
		{
			static void Prefix(ZRoutedRpc.RoutedRPCData rpcData)
			{
				if (rpcData.m_methodHash == "RequestRespons".GetStableHashCode())
				{
					bool granted = rpcData.m_parameters.ReadBool();
					ZDO zdo = ZDOMan.instance.GetZDO(rpcData.m_targetZDO);
					if (zdo != null && granted)
					{
						ServersidePlugin.logger.LogDebug($"RequestRespons: Setting ship's owner to {rpcData.m_targetPeerID}");
						zdo.SetOwner(rpcData.m_targetPeerID);
					}
				}
			}
		}

		[HarmonyPatch(typeof(Humanoid), "UpdateAttack")]
		public static class Humanoid_UpdateAttack_Patch
		/*
			Remove the `m_currentAttack` from the Humanoid if it doesn't have a character instance.
			
			The underlying reason for an `Attack` instance not to have `m_character` set
			is currently not known, and requires further investigation.
		 */
		{
			static void Prefix(ref Humanoid __instance)
			{
				if (__instance.m_currentAttack != null && __instance.m_currentAttack.m_character == null)
				{
					__instance.m_currentAttack = null;
				}
			}
		}

		[HarmonyPatch(typeof(WearNTear), "UpdateSupport")]
		public static class WearNTear_UpdateSupport_Patch
		/*
			Call `SetupColliders` if `WearNTear.m_bounds` is not set but `WearNTear.m_colliders` are set.
			

			The underlying reason is currently not known, and requires further investigation.
		 */
		{
			static void Prefix(ref WearNTear __instance)
			{
				if (__instance.m_colliders != null && __instance.m_bounds == null)
				{
					__instance.SetupColliders();
				}
			}
		}

		[HarmonyPatch(typeof(Ship), "UpdateOwner")]
		public static class Ship_UpdateOwner_Patch
		/*
			This method is invoked on a 4 second timer. 

			Keep the Ship owner set to the Ship's driver.

			If the Ship has no valid user, set the owner to the server
			to ensure simulations are handled by the server.

			Only change ownership when the Ship's container is not in use,
			to prevent them from being kicked out of said container.

			Prevent boat from taking impact damage from out of sync water 
			levels when taking ownership.
		*/
		{
			static bool Prefix(ref Ship __instance)
			{
				ZDO zdo = __instance.m_nview.GetZDO();
				// Don't do anything if a player is using ship's container
				if (zdo.GetInt("InUse", 0) == 0)
				{
					if (!__instance.m_shipControlls.HaveValidUser())
					{
						__instance.m_lastWaterImpactTime = Time.time;
						zdo.SetOwner(ZNet.GetUID());
						return false;
					}
					long driver = __instance.m_shipControlls.GetUser();
					if (driver != 0L)
					{
						long driverID = Player.GetPlayer(driver).GetOwner();
						ServersidePlugin.logger.LogDebug($"UpdateOwner: Setting ship's owner to {driverID}");
						zdo.SetOwner(driverID);
					}
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(AudioMan), "Update")]
		public static class AudioMan_Update_Patch
		/*
			Skip `AudioMan.Update` on the server.
		 */
		{
			static bool Prefix()
			{
				return false;
			}
		}

		[HarmonyPatch(typeof(ShieldDomeImageEffect), "GetDomeColor")]
		public static class ShieldDomeImageEffect_GetDomeColor_Patch
		/*
			The original code results in a null reference while trying to create a gradient.
			We force `GetDomeColor` to return a constant value in favor of patching the
			larger caller function to remove the reference to `GetDomeColor`.
		 */
		{
			static bool Prefix(ref Color __result)
			{
				__result = new Color(1, 1, 1);
				return false;
			}
		}

	}
}
