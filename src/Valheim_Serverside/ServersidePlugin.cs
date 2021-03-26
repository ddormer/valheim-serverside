using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using UnityEngine;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace Valheim_Serverside
{
	[BepInPlugin("MVP.Valheim_Serverside", "Valheim Serverside", "1.0.0.0")]
	public class ServersidePlugin : BaseUnityPlugin
	{
		public static ConfigEntry<bool> modEnabled;

		private static ServersidePlugin context;

		private void Awake()
		{
			context = this;
			modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable or disable the mod");

			if (!modEnabled.Value)
			{
				Logger.LogInfo("Valheim Serverside is disabled");
				return;
			}

			Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
			Logger.LogInfo("Valheim Serverside installed");
		}

		public static bool IsServer()
		/*
			Calls `ZNet.instance.IsServer` after checking if `ZNet.instance` exists.
		*/
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

		[HarmonyPatch(typeof(ZNetScene), "OutsideActiveArea", new Type[] { typeof(Vector3) })]
		private class ZNetScene_OutsideActiveArea_Patch
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


		[HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
		private class CreateDestroyObjects_Patch
		/*
			Creates and destroys ZDOs by finding all objects in each peer area.

			Some object overlap can happen if peers are close to each other, the objects are
			deduplicated by using a HashSet, see `List.Distinct`.

			This method originally only checked for objects in `ZNet.GetReferencePosition()`.

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
				foreach (ZNetPeer znetPeer in ZNet.instance.GetConnectedPeers())
				{
					Vector2i zone = ZoneSystem.instance.GetZone(znetPeer.GetRefPos());
					ZDOMan.instance.FindSectorObjects(zone, ZoneSystem.instance.m_activeArea, ZoneSystem.instance.m_activeDistantArea, m_tempCurrentObjects, m_tempCurrentDistantObjects);
				}

				m_tempCurrentDistantObjects = m_tempCurrentDistantObjects.Distinct().ToList();
				m_tempCurrentObjects = m_tempCurrentObjects.Distinct().ToList();
				Traverse.Create(__instance).Method("CreateObjects", m_tempCurrentObjects, m_tempCurrentDistantObjects).GetValue();
				Traverse.Create(__instance).Method("RemoveObjects", m_tempCurrentObjects, m_tempCurrentDistantObjects).GetValue();
				return false;
			}
		}

		[HarmonyPatch(typeof(ZoneSystem), "Update")]
		static class ZoneSystem_Update_Patch
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
					//bool flag = Traverse.Create(__instance).Method("CreateLocalZones", ZNet.instance.GetReferencePosition()).GetValue<bool>();
					Traverse.Create(__instance).Method("UpdateTTL", 0.1f).GetValue();
					if (ZNet.instance.IsServer()) // && !flag)
					{
						//Traverse.Create(__instance).Method("CreateGhostZones", ZNet.instance.GetReferencePosition()).GetValue();
						//UnityEngine.Debug.Log(String.Concat(new object[] { "CreateLocalZones for", refPoint.x, " ", refPoint.y, " ", refPoint.z }));
						foreach (ZNetPeer znetPeer in ZNet.instance.GetPeers())
						{
							Traverse.Create(__instance).Method("CreateLocalZones", znetPeer.GetRefPos()).GetValue();
						}
					}
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
		static class ZDOMan_ReleaseNearbyZDOS_Patch
		/*
			Releases nearby ZDOs for a player if no other peers are nearby that player.
			If instead the nearby ZDO has no owner, set owner to server so that it simulates on the server.

			Original method:
			If ZDO is no longer near the peer, release ownership. If no owner set, change ownership to said peer.
		*/
		{
			static bool Prefix(ZDOMan __instance, ref Vector3 refPosition, ref long uid)
			{
				Vector2i zone = ZoneSystem.instance.GetZone(refPosition);
				List<ZDO> m_tempNearObjects = Traverse.Create(__instance).Field("m_tempNearObjects").GetValue<List<ZDO>>();
				m_tempNearObjects.Clear();

				__instance.FindSectorObjects(zone, ZoneSystem.instance.m_activeArea, 0, m_tempNearObjects, null);
				foreach (ZDO zdo in m_tempNearObjects)
				{
					if (zdo.m_persistent)
					{
						List<bool> in_area = new List<bool>();
						foreach (ZNetPeer peer in ZNet.instance.GetPeers())
						{
							in_area.Add(ZNetScene.instance.InActiveArea(zdo.GetSector(), ZoneSystem.instance.GetZone(peer.GetRefPos())));
						}
						if (zdo.m_owner == uid || zdo.m_owner == ZNet.instance.GetUID())
						{
							if (!in_area.Contains(true))
							{
								zdo.SetOwner(0L);
							}
						}

						else if ((zdo.m_owner == 0L || !new Traverse(__instance).Method("IsInPeerActiveArea", new object[] { zdo.GetSector(), zdo.m_owner }).GetValue<bool>())
								 && in_area.Contains(true))
						{
							zdo.SetOwner(ZNet.instance.GetUID());
						}
					}
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(Chat), "RPC_ChatMessage")]
		static class Chat_RPC_ChatMessage_Patch
		{
			static void Prefix(ref long sender, ref string text)
			{
				ZNetPeer peer = ZNet.instance.GetPeer(sender);
				if (peer == null)
				{
					return;
				}
				if (text == "startevent")
				{
					RandEventSystem.instance.SetRandomEventByName("army_theelder", peer.GetRefPos());
				}
				else if (text == "stopevent")
				{
					RandEventSystem.instance.ResetRandomEvent();
				}
			}
		}

		[HarmonyPatch(typeof(RandEventSystem), "FixedUpdate")]
		static class RandEventSystem_FixedUpdate_Patch
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
				MethodInfo isInsideRandomEventAreaInfo = AccessTools.Method(typeof(RandEventSystem), "IsInsideRandomEventArea");
				FieldInfo field_m_localPlayer = AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer));
				MethodInfo opImplicitInfo = AccessTools.Method(typeof(UnityEngine.Object), "op_Implicit");

				bool foundIsAnyPlayer = false;
				CodeInstruction ldPlayerInArea = null;

				List<CodeInstruction> instructions = _instructions.ToList();
				List<CodeInstruction> new_instructions = _instructions.ToList();


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
					else if (instruction.opcode == OpCodes.Ldarg_0)
					{
						if (instructions[i + 1].opcode == OpCodes.Ldarg_0 &&
							instructions[i + 2].opcode == OpCodes.Ldfld &&
							instructions[i + 3].opcode == OpCodes.Ldsfld &&
							instructions[i + 4].opcode == OpCodes.Callvirt &&
							instructions[i + 5].opcode == OpCodes.Callvirt &&
							instructions[i + 6].opcode == OpCodes.Call
							)
						{
							//ZLog.Log("Removing a lot and inserting ldPlayerInArea");
							new_instructions.RemoveRange(i, 7);
							new_instructions.Insert(i, ldPlayerInArea);
						}
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

			/*static bool Prefix(RandEventSystem __instance, ref RandomEvent ___m_activeEvent, ref RandomEvent ___m_forcedEvent, ref RandomEvent ___m_randomEvent)
			{
				Traverse _t = new Traverse(__instance);
				float fixedDeltaTime = Time.fixedDeltaTime;
				_t.Method("UpdateForcedEvents", fixedDeltaTime).GetValue();
				_t.Method("UpdateRandomEvent", fixedDeltaTime).GetValue();
				if (___m_forcedEvent != null)
				{
					___m_forcedEvent.Update(ZNet.instance.IsServer(), ___m_forcedEvent == ___m_activeEvent, true, fixedDeltaTime);
				}
				if (___m_randomEvent != null && ZNet.instance.IsServer())
				{
					bool playerInArea = _t.Method("IsAnyPlayerInEventArea", ___m_randomEvent).GetValue<bool>();
					if (___m_randomEvent.Update(true, ___m_randomEvent == ___m_activeEvent, playerInArea, fixedDeltaTime))
					{
						_t.Method("SetRandomEvent", new Type[] { typeof(RandomEvent), typeof(Vector3) }, new object[] { null, Vector3.zero }).GetValue();
					}
				}
				if (___m_forcedEvent != null)
				{
					_t.Method("SetActiveEvent", new Type[] { typeof(RandomEvent), typeof(bool) }, new object[] { ___m_forcedEvent, false }).GetValue();
					return false;
				}
				if (___m_randomEvent == null *//* || !Player.m_localPlayer *//*)
				{
					_t.Method("SetActiveEvent", new Type[] { typeof(RandomEvent), typeof(bool) }, new object[] { null, false }).GetValue();
					return false;
				}
				foreach (Player player in Player.GetAllPlayers())
				{
					if (_t.Method("IsInsideRandomEventArea", ___m_randomEvent, player.transform.position).GetValue<bool>())
					{
						_t.Method("SetActiveEvent", new Type[] { typeof(RandomEvent), typeof(bool) }, new object[] { ___m_randomEvent, false }).GetValue();
						return false;
					}
				}
				_t.Method("SetActiveEvent", new Type[] { typeof(RandomEvent), typeof(bool) }, new object[] { null, false }).GetValue();
				return false;
			}*/
		}

		[HarmonyPatch(typeof(SpawnSystem), "UpdateSpawning")]
        static class SpawnSystem_UpdateSpawning_Patch
        {

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> _instructions)
			{
				FieldInfo field_m_localPlayer = AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer));
				var localPlayerCheck = new SequentialInstructions(new List<CodeInstruction>(new CodeInstruction[]
				{
					new CodeInstruction(OpCodes.Ldsfld, field_m_localPlayer),
					new CodeInstruction(OpCodes.Ldnull),
					new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(UnityEngine.Object), "op_Equality")),
					new CodeInstruction(OpCodes.Brfalse)
				}));

				foreach (CodeInstruction instruction in _instructions)
				{
					if (localPlayerCheck.Check(instruction))
                    {
						yield return new CodeInstruction(OpCodes.Brtrue, instruction.operand);
						continue;
                    }
					yield return instruction;
				}
			}
			//static bool Prefix(SpawnSystem __instance)
			//{
			//	Traverse _t = new Traverse(__instance);
			//	ZNetView m_nview = _t.Field("m_nview").GetValue<ZNetView>();
			//	if (!m_nview.IsValid() || !m_nview.IsOwner())
			//	{
			//		return false;
			//	}
			//	/*if (Player.m_localPlayer == null)
			//	{
			//		return false;
			//	}*/
			//	List<Player> m_nearPlayers = _t.Field("m_nearPlayers").GetValue<List<Player>>();
			//	m_nearPlayers.Clear();
			//	_t.Method("GetPlayersInZone", m_nearPlayers).GetValue();
			//	if (m_nearPlayers.Count == 0)
			//	{
			//		return false;
			//	}
			//	DateTime time = ZNet.instance.GetTime();
			//	_t.Method("UpdateSpawnList", __instance.m_spawners, time, false).GetValue();
			//	List<SpawnSystem.SpawnData> currentSpawners = RandEventSystem.instance.GetCurrentSpawners();
			//	if (currentSpawners != null)
			//	{
			//		_t.Method("UpdateSpawnList", currentSpawners, time, true).GetValue();
			//	}
			//	return false;
			//}
		}
    }
}
