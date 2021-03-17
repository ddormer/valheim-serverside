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

		[HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
		private class CreateDestroyObjects_Patch
		{
			private static bool Prefix(ZNetScene __instance)
			{
				if (!ZNet.instance || !ZNet.instance.IsServer())
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
	}
}
