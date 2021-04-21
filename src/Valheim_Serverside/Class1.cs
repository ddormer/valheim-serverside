using FeaturesLib;
using HarmonyLib;
using MonoMod.Cil;

using OC = Mono.Cecil.Cil.OpCodes;

namespace Valheim_Serverside
{
	public class MaxObjectsPerFrameFeature
	{
		public int GetMaxCreatedPerFrame()
		{
			return ServersidePlugin.configuration.maxObjectsPerFrame.Value;
		}

		[RequiredFeature("MaxObjectsPerFrame")]
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
						typeof(MaxObjectsPerFrameFeature),
						nameof(MaxObjectsPerFrameFeature.GetMaxCreatedPerFrame)))
					.Emit(OC.Stloc_0);
			}
		}
	}
}
