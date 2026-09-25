using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;

namespace Valheim_Serverside.Features
{
	public class Fixes : IFeature
	/*
		Valheim 1.0 only rewrites world chunks it considers "changed" on save, and does not count
		a change received FROM a player (a build placed, an object moved) as changing the chunk --
		only changes the server itself originates do that. On a normal peer-hosted game this never
		matters, because the host's own client is usually the one making the change locally. This
		mod moves persistent-object ownership to the server, so a huge fraction of "changes" arrive
		as ZDO updates received over the network instead, and none of those mark their chunk dirty --
		meaning something a player just built or moved could silently be missing after a restart if
		its chunk was not otherwise touched by something the server itself did.
	*/
	{
		public bool FeatureEnabled()
		{
			return Configuration.fixSaveClientChanges.Value;
		}

		[HarmonyPatch(typeof(ZDO), "Deserialize")]
		public static class ZDO_Deserialize_Patch
		{
			static void Postfix(ZDO __instance)
			{
				if (__instance.Persistent && ZNet.instance != null && ZNet.instance.IsServer() && ZDOMan.instance != null)
				{
					ZDOMan.instance.SetDirtySector(__instance);
				}
			}
		}
	}
}
