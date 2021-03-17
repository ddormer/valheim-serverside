using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using System;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace Valheim_Serverside
{
	[BepInPlugin("MVP.Valheim_Serverside", "Valheim Serverside", "1.0.0.0")]
	public class ServersidePlugin : BaseUnityPlugin
	{
		void Awake()
		{
			UnityEngine.Debug.Log("Valheim Serverside installed");
			Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
		}

		public static bool IsServer()
        {
			return ZNet.instance && ZNet.instance.IsServer();
        }

		[HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
		private class CreateDestroyObjects_Patch
		{
			private static bool Prefix(ZNetScene __instance)
			{
				if (!IsServer())
				{
					return true;
				}
				List<ZDO> m_tempCurrentObjects = new List<ZDO>();
				List<ZDO> m_tempCurrentDistantObjects = new List<ZDO>();
				foreach (ZNetPeer znetPeer in ZNet.instance.GetConnectedPeers())
				{
					Vector2i zone = ZoneSystem.instance.GetZone(znetPeer.GetRefPos());
					ZDOMan.instance.FindSectorObjects(zone, ZoneSystem.instance.m_activeArea, ZoneSystem.instance.m_activeDistantArea, m_tempCurrentObjects, m_tempCurrentDistantObjects);
				}

				Traverse.Create(__instance).Method("CreateObjects", m_tempCurrentObjects, m_tempCurrentDistantObjects).GetValue();
				Traverse.Create(__instance).Method("RemoveObjects", m_tempCurrentObjects, m_tempCurrentDistantObjects).GetValue();
				return false;
			}
		}

		[HarmonyPatch(typeof(ZNetScene), "CreateObject")]
		static class ZNetScene_CreateObject_Patch
		{
			static void Prefix(ref ZDO zdo)
			{
				if (ZNetScene.instance.GetPrefab(zdo.GetPrefab()) != Game.instance.m_playerPrefab && zdo.m_owner != ZNet.instance.GetUID())
				{
					zdo.SetOwner(ZNet.instance.GetUID());
				}
			}
		}

		[HarmonyPatch(typeof(ZoneSystem), "CreateGhostZones")]
		static class ZoneSystem_CreateGhostZones_Patch
		{
			static void Postfix(ZoneSystem __instance, ref Vector3 refPoint)
			{
				Traverse.Create(__instance).Method("CreateLocalZones", new object[] { refPoint }).GetValue();
			}
		}

		[HarmonyPatch(typeof(ZDOMan), "ReleaseZDOS")]
		static class ZDOMan_ReleaseZDOS_Patch
		{
			static bool Prefix()
			{
				return false;
			}
		}

		[HarmonyPatch(typeof(ZNet), "SendPeriodicData")]
		static class ZNet_SendPeriodicData_Patch
		{
			static bool Prefix(ZNet __instance, ref float dt)
			{
				Traverse m_periodicSendTimer = Traverse.Create(__instance).Field("m_periodicSendTimer");
				m_periodicSendTimer.SetValue(m_periodicSendTimer.GetValue<float>() + dt);
				if (m_periodicSendTimer.GetValue<float>() >= 2f)
				{
					m_periodicSendTimer.SetValue(0f);
					if (__instance.IsServer())
					{
						Traverse.Create(__instance).Method("SendNetTime").GetValue();
						Traverse.Create(__instance).Method("SendPlayerList").GetValue();

						// Spoof location of server to clients so that they don't try to release server-owned ZDOs
						foreach (ZNetPeer znetPeer in __instance.GetPeers())
						{
							if (znetPeer.IsReady())
							{
								znetPeer.m_rpc.Invoke("RefPos", new object[]
								{
								znetPeer.m_refPos,
								false
								});
							}
						}
						return false;
					}
					foreach (ZNetPeer znetPeer in __instance.GetPeers())
					{
						if (znetPeer.IsReady())
						{
							znetPeer.m_rpc.Invoke("RefPos", new object[]
							{
								__instance.GetReferencePosition(),
								false
							});
						}
					}
				}
				return false;
			}
		}
	}
}
