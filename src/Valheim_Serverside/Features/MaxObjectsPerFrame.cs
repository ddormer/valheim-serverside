using FeaturesLib;
using HarmonyLib;
using MonoMod.Cil;
using PluginConfiguration;

using OC = Mono.Cecil.Cil.OpCodes;

namespace Valheim_Serverside.Features
{
	public class MaxObjectsPerFrame : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.maxObjectsPerFrameEnabled.Value;
		}

		public static int GetMaxCreatedPerFrame()
		{
			return Configuration.maxObjectsPerFrame.Value;
		}

		[HarmonyPatch(typeof(ZNetScene), "CreateObjects")]
		public class CreateObjects_Patch
		/*
			Set the local variable `maxCreatedPerFrame` to the result of `GetMaxCreatedPerFrame`.

			Setting this higher allows the world to be loaded faster if the server CPU can keep up.

			Originally set to 100, see `Configuration.maxObjectsPerFrame` for the new value.
		 */
		{
			public static void ILManipulator(ILContext il)
			{
				new ILCursor(il)
					.GotoNext(MoveType.Before,
						i => i.MatchLdarg(0),
						i => i.MatchLdarg(1),
						i => i.MatchLdloc(0),
						i => i.MatchLdloca(2),
						i => i.MatchCall<ZNetScene>("CreateObjectsSorted")
					)
					.Emit(OC.Call, AccessTools.Method(
						typeof(MaxObjectsPerFrame),
						nameof(MaxObjectsPerFrame.GetMaxCreatedPerFrame)))
					.Emit(OC.Stloc_0);
			}
		}
	}
}
