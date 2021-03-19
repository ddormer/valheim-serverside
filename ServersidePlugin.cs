﻿using System;
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

		public static void PrintLog(string text)
        {
			System.Diagnostics.Trace.WriteLine(text);
        }
		public static void PrintLog(object[] obj)
		{
			System.Diagnostics.Trace.WriteLine(string.Concat(obj));
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

				m_tempCurrentObjects = m_tempCurrentObjects.Distinct().ToList();
				m_tempCurrentDistantObjects = m_tempCurrentDistantObjects
											  .Distinct()
											  .Except(m_tempCurrentObjects)
											  .ToList();
				Traverse.Create(__instance).Method("CreateObjects", m_tempCurrentObjects, m_tempCurrentDistantObjects).GetValue();
				Traverse.Create(__instance).Method("RemoveObjects", m_tempCurrentObjects, m_tempCurrentDistantObjects).GetValue();
				return false;
			}
		}

		//[HarmonyPatch(typeof(ZNetScene), "CreateObject")]
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

		[HarmonyPatch(typeof(ZoneSystem), "Update")]
		static class ZoneSystem_Update_Patch
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
					// flag line can probably be removed, as well as the check for it.
					//bool flag = Traverse.Create(__instance).Method("CreateLocalZones", ZNet.instance.GetReferencePosition()).GetValue<bool>();
					Traverse.Create(__instance).Method("UpdateTTL", 0.1f).GetValue();
					if (ZNet.instance.IsServer()) // && !flag)
					{
						//Traverse.Create(__instance).Method("CreateGhostZones", ZNet.instance.GetReferencePosition()).GetValue();
						//UnityEngine.Debug.Log(String.Concat(new object[] { "CreateLocalZones for", refPoint.x, " ", refPoint.y, " ", refPoint.z }));
						foreach (ZNetPeer znetPeer in ZNet.instance.GetPeers())
						{
							//PrintLog("Is generated: " + Traverse.Create(__instance).Method("IsZoneGenerated", __instance.GetZone(znetPeer.GetRefPos())).GetValue<bool>());
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
