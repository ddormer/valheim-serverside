using FeaturesLib;
using HarmonyLib;
using System;

namespace Valheim_Serverside
{
	[Harmony]
	class Debugging
	{
		[RequiredFeature("debug")]
		[HarmonyPatch(typeof(Chat), "RPC_ChatMessage")]
		public static class Chat_RPC_ChatMessage_Patch
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
				else if (text.StartsWith("maxobjects"))
				{
					ServersidePlugin.configuration.maxObjectsPerFrame.Value = Convert.ToInt32(text.Split(' ').GetValue(1));
				}
			}
		}
	}

}
