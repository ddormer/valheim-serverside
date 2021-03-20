using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

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

			Local-Zone: 
			Ghost-Zone: 
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
	}
}
