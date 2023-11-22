using FeaturesLib;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

		[HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
		public class CreateDestroyObjects_Patch
		/*
			The bread and butter of the mod, this patch facilitates spawning objects on the server.

			Creates and destroys ZDOs by finding all objects in each peer area.

			Some object overlap can happen if peers are close to each other: we find all active areas (sectors)
			and deduplicate using HashSets.

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
			private static readonly List<Vector2i> m_tempNearSectors = new List<Vector2i>();
			private static readonly List<Vector2i> m_tempDistantSectors = new List<Vector2i>();

			private static bool Prefix(ZNetScene __instance, ref List<ZDO> ___m_tempCurrentObjects, ref List<ZDO> ___m_tempCurrentDistantObjects)
			{
				m_tempNearSectors.Clear();
				m_tempDistantSectors.Clear();
				___m_tempCurrentObjects.Clear();
				___m_tempCurrentDistantObjects.Clear();

				Utilities.FindAllActiveSectors(ZoneSystem.instance.m_activeArea, ZoneSystem.instance.m_activeDistantArea, m_tempNearSectors, m_tempDistantSectors);
				Utilities.FindObjectsInSectors(m_tempNearSectors, ___m_tempCurrentObjects);
				Utilities.FindDistantObjectsInSectors(m_tempDistantSectors, ___m_tempCurrentDistantObjects);

				__instance.CreateObjects(___m_tempCurrentObjects, ___m_tempCurrentDistantObjects);
				__instance.RemoveObjects(___m_tempCurrentObjects, ___m_tempCurrentDistantObjects);

				return false;
			}
		}

		[HarmonyPatch(typeof(ZoneSystem), "IsActiveAreaLoaded")]
		public static class ZoneSystem_IsActiveAreaLoaded_Patch
		{
			private static bool Prefix(ZoneSystem __instance, ref bool __result, Dictionary<Vector2i, dynamic> ___m_zones)
			{
				foreach (ZNetPeer peer in ZNet.instance.GetPeers())
				{
					Vector2i zone = __instance.GetZone(peer.GetRefPos());
					for (int i = zone.y - __instance.m_activeArea; i <= zone.y + __instance.m_activeArea; i++)
					{
						for (int j = zone.x - __instance.m_activeArea; j <= zone.x + __instance.m_activeArea; j++)
						{
							if (!___m_zones.ContainsKey(new Vector2i(j, i)))
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
					//bool flag = __instance.CreateLocalZones(ZNet.instance.GetReferencePosition());
					__instance.UpdateTTL(0.1f);
					if (ZNet.instance.IsServer()) // && !flag)
					{
						//__instance.CreateGhostZones(ZNet.instance.GetReferencePosition());
						//UnityEngine.Debug.Log(String.Concat(new object[] { "CreateLocalZones for", refPoint.x, " ", refPoint.y, " ", refPoint.z }));
						foreach (ZNetPeer znetPeer in ZNet.instance.GetPeers())
						{
							__instance.CreateLocalZones(znetPeer.GetRefPos());
						}
					}
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
			static bool Prefix(ZDOMan __instance, ref Vector3 refPosition, ref long uid, List<ZDO> ___m_tempNearObjects)
			{
				Vector2i zone = ZoneSystem.instance.GetZone(refPosition);
				___m_tempNearObjects.Clear();

				__instance.FindSectorObjects(zone, ZoneSystem.instance.m_activeArea, 0, ___m_tempNearObjects, null);
				foreach (ZDO zdo in ___m_tempNearObjects)
				{
					if (zdo.Persistent)
					{
						bool anyPlayerInArea = false;
						foreach (ZNetPeer peer in ZNet.instance.GetPeers())
						{
							if (ZNetScene.InActiveArea(zdo.GetSector(), ZoneSystem.instance.GetZone(peer.GetRefPos())))
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
							|| __instance.IsInPeerActiveArea(zdo.GetSector(), zdo.GetOwner())
							)
							&& anyPlayerInArea
						)
						{
							zdo.SetOwner(ZNet.GetUID());
						}
					}
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(RandEventSystem), "FixedUpdate")]
		public static class RandEventSystem_FixedUpdate_Patch
		/*
			Patches out m_localPlayer == null check by reversing the boolean check
			and instead of:

				if (this.IsInsideRandomEventArea(this.m_randomEvent, Player.m_localPlayer.transform.position))

			reuses the previously-assigned playerInArea boolean.

			Fixes monsters not spawning during events with this mod active.
		*/
		{
			static Dictionary<OpCode, OpCode> StlocToLdloc = new Dictionary<OpCode, OpCode> {
				{OpCodes.Stloc_0, OpCodes.Ldloc_0},
				{OpCodes.Stloc_1, OpCodes.Ldloc_1},
				{OpCodes.Stloc_2, OpCodes.Ldloc_2},
				{OpCodes.Stloc_3, OpCodes.Ldloc_3},
				{OpCodes.Stloc_S, OpCodes.Ldloc_S},
				{OpCodes.Stloc, OpCodes.Ldloc}
			};

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> _instructions)
			{
				//var codes = new List<CodeInstruction>(instructions);
				MethodInfo isAnyPlayerInfo = AccessTools.Method(typeof(RandEventSystem), "IsAnyPlayerInEventArea");
				FieldInfo field_m_localPlayer = AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer));
				MethodInfo opImplicitInfo = AccessTools.Method(typeof(UnityEngine.Object), "op_Implicit");

				bool foundIsAnyPlayer = false;
				CodeInstruction ldPlayerInArea = null;

				List<CodeInstruction> instructions = _instructions.ToList();
				List<CodeInstruction> new_instructions = _instructions.ToList();

				var insideRandomEventAreaCheck = new SequentialInstructions(new List<CodeInstruction>(new CodeInstruction[]
				{
					new CodeInstruction(OpCodes.Ldarg_0),
					new CodeInstruction(OpCodes.Ldarg_0),
					new CodeInstruction(OpCodes.Ldfld),
					new CodeInstruction(OpCodes.Ldsfld),
					new CodeInstruction(OpCodes.Callvirt),
					new CodeInstruction(OpCodes.Callvirt),
					new CodeInstruction(OpCodes.Call)
				}));
				for (int i = 0; i < instructions.Count; i++)
				{
					CodeInstruction instruction = instructions[i];

					if (instruction.OperandIs(isAnyPlayerInfo))
					{
						//ZLog.Log("isAnyPlayerInfo");
						foundIsAnyPlayer = true;
					}
					else if (foundIsAnyPlayer && instruction.IsStloc())
					{
						//ZLog.Log("foundIsAnyPlayer && IsStloc");
						ldPlayerInArea = instruction.Clone();
						ldPlayerInArea.opcode = StlocToLdloc[instruction.opcode];
						foundIsAnyPlayer = false;
					}
					else if (ldPlayerInArea != null && insideRandomEventAreaCheck.Check(instruction))
					{
						//ZLog.Log("Removing a lot and inserting ldPlayerInArea");
						int count = insideRandomEventAreaCheck.Sequential.Count;
						int startIdx = i - (count - 1);
						new_instructions.RemoveRange(startIdx, count);
						new_instructions.Insert(startIdx, ldPlayerInArea);
						break;
					}
				}

				var localPlayerCheck = new SequentialInstructions(new List<CodeInstruction>(new CodeInstruction[]
				{
					new CodeInstruction(OpCodes.Ldsfld, field_m_localPlayer),
					new CodeInstruction(OpCodes.Call, opImplicitInfo),
					new CodeInstruction(OpCodes.Brfalse)
				}));
				for (int i = 0; i < new_instructions.Count; i++)
				{
					CodeInstruction instruction = new_instructions[i];
					if (localPlayerCheck.Check(instruction))
					{
						yield return new CodeInstruction(OpCodes.Brtrue, instruction.operand);
						continue;
					}
					yield return instruction;
				}
			}
		}

		public static List<SpawnSystem.SpawnData> GetCurrentSpawners(RandEventSystem instance, SpawnSystem spawnSystem)
		/*
			Return spawners if there are nearby players in the event area.
		*/
		{
			if (instance.m_activeEvent == null)
			{
				return null;
			}

			ZNetView spawnSystem_m_nview = spawnSystem.m_nview;
			RandomEvent randomEvent = instance.m_randomEvent;

			foreach (Player player in Player.GetAllPlayers())
			{
				if (ZNetScene.InActiveArea(spawnSystem_m_nview.GetZDO().GetSector(), player.transform.position))
				{
					if (instance.IsInsideRandomEventArea(randomEvent, player.transform.position))
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
					if (!__instance.OutsideActiveArea(point, znetPeer.GetRefPos()))
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
	}
}
