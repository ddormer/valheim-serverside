using HarmonyLib;
using PluginConfiguration;
using Requirements;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;


namespace Valheim_Serverside.Features
{
	[Harmony]
	class Debugging : FeaturesLib.IFeature
	{
		public bool FeatureEnabled()
		{
			return Utilities.IsDebugBuild();
		}

		[PatchingLib.PatchRequires(PatchRequirement.DebugBuild.name)]
		[HarmonyPatch(typeof(Chat), "RPC_ChatMessage")]
		public static class Chat_RPC_ChatMessage_Patch
		{
			private static HashSet<Vector2i> m_tempNearSectors = new HashSet<Vector2i>();
			private static HashSet<Vector2i> m_tempDistantSectors = new HashSet<Vector2i>();
			private static List<ZDO> m_tempCurrentObjects = new List<ZDO>();
			private static List<ZDO> m_tempCurrentDistantObjects = new List<ZDO>();

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
				else if (text.StartsWith("maxobjects"))
				{
					Configuration.maxObjectsPerFrame.Value = Convert.ToInt32(text.Split(' ').GetValue(1));
				}
				else if (text == "testfindobjects")
				{
					var sw = new Stopwatch();
					sw.Start();
					for (int i = 0; i < 1000; i++)
					{
						m_tempCurrentObjects.Clear();
						m_tempCurrentDistantObjects.Clear();
						foreach (ZNetPeer znetPeer in ZNet.instance.GetConnectedPeers())
						{
							Vector2i zone = ZoneSystem.instance.GetZone(znetPeer.GetRefPos());
							ZDOMan.instance.FindSectorObjects(zone, ZoneSystem.instance.m_activeArea, ZoneSystem.instance.m_activeDistantArea, m_tempCurrentObjects, m_tempCurrentDistantObjects);
						}

						m_tempCurrentDistantObjects = m_tempCurrentDistantObjects.Distinct().ToList();
						m_tempCurrentObjects = m_tempCurrentObjects.Distinct().ToList();
					}
					ServersidePlugin.logger.LogInfo($"Elapsed: {sw.ElapsedMilliseconds}ms");
				}
				else if (text == "testfindobjects2")
				{
					var sw = new Stopwatch();
					sw.Start();
					for (int i = 0; i < 1000; i++)
					{
						m_tempNearSectors.Clear();
						m_tempDistantSectors.Clear();
						m_tempCurrentObjects.Clear();
						m_tempCurrentObjects.Clear();

						Utilities.FindAllActiveSectors(ZoneSystem.instance.m_activeArea, ZoneSystem.instance.m_activeDistantArea, m_tempNearSectors, m_tempDistantSectors);
						Utilities.FindObjectsInSectors(m_tempNearSectors, m_tempCurrentObjects);
						Utilities.FindDistantObjectsInSectors(m_tempDistantSectors, m_tempCurrentObjects);
					}
					ServersidePlugin.logger.LogInfo($"Elapsed: {sw.ElapsedMilliseconds}ms");
				}
			}
		}
	}
}
