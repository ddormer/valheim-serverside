using FeaturesLib;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace Valheim_Serverside.Features
{
	public class Compat_ValheimPlus : IFeature
	{
		public bool FeatureEnabled()
		{
			// TODO: config
			return true;
		}

		public Compat_ValheimPlus()
		{
			TryPatchInventoryAssistant();
		}

		[HarmonyPatch(typeof(ZNetScene), "Awake")]
		public class ZNetScene_Awake_Patch
		{
			static bool patchedSmelter = false;

			public static void Postfix()
			{
				// This needs to run after ObjectDB has been initialized, otherwise
				// we get null reference exceptions trying to initialize the type
				if (!patchedSmelter)
				{
					TryPatchSmelter();
					patchedSmelter = true;
				}
			}
		}

		private static void TryPatchInventoryAssistant()
		{
			if (!ServersidePlugin.haveValheimPlus)
			{
				return;
			}

			Type InventoryAssistant = Type.GetType("ValheimPlus.InventoryAssistant, ValheimPlus");
			if (InventoryAssistant != null)
			{
				ServersidePlugin.logger.LogInfo("Patching ValheimPlus.InventoryAssistant.GetNearbyChests");
				ServersidePlugin.harmony.Patch(
					AccessTools.Method(InventoryAssistant, "GetNearbyChests"),
					transpiler: new HarmonyMethod(typeof(Compat_ValheimPlus), nameof(Compat_ValheimPlus.Transpile_InventoryAssistant_GetNearbyChests))
				);
			}
			else
			{
				ServersidePlugin.logger.LogError("Couldn't find ValheimPlus.InventoryAssistant");
			}
		}

		private static void TryPatchSmelter()
		{
			if (!ServersidePlugin.haveValheimPlus)
			{
				return;
			}

			Type Smelter_FixedUpdate_Patch = Type.GetType("ValheimPlus.GameClasses.Smelter_FixedUpdate_Patch, ValheimPlus");
			if (Smelter_FixedUpdate_Patch != null)
			{
				ServersidePlugin.logger.LogInfo("Patching ValheimPlus.GameClasses.Smelter_FixedUpdate_Patch.Postfix");
				ServersidePlugin.harmony.Patch(
					AccessTools.Method(Smelter_FixedUpdate_Patch, "Postfix"),
					transpiler: new HarmonyMethod(typeof(Compat_ValheimPlus), nameof(Compat_ValheimPlus.Transpile_Smelter_FixedUpdate_Patch))
				);
			}
			else
			{
				ServersidePlugin.logger.LogError("Couldn't find ValheimPlus.GameClasses.Smelter_FixedUpdate_Patch");
			}
		}

		private static IEnumerable<CodeInstruction> Transpile_InventoryAssistant_GetNearbyChests(IEnumerable<CodeInstruction> instructions)
		{
			return new CodeMatcher(instructions)
				// Remove Container.CheckAccess call
				.MatchForward(false,
					new CodeMatch(OpCodes.Ldloc_S),
					new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer))),
					new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Player), nameof(Player.GetPlayerID))),
					new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Container), "CheckAccess"))
				)
				.RemoveInstructions(4)
				.Insert(new CodeInstruction(OpCodes.Ldc_I4_1))

				// Replace loading checkWard argument with `false` to skip the check
				.MatchForward(true,
					new CodeMatch(OpCodes.Stloc_S),
					new CodeMatch(OpCodes.Ldarg_2)
				)
				.SetInstruction(new CodeInstruction(OpCodes.Ldc_I4_0))

				.InstructionEnumeration()
			;
		}

		private static IEnumerable<CodeInstruction> Transpile_Smelter_FixedUpdate_Patch(IEnumerable<CodeInstruction> instructions)
		{
			return new CodeMatcher(instructions)
				.MatchForward(true,
					new CodeMatch(OpCodes.Ldsfld),
					new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(UnityEngine.Object), "op_Implicit")),
					new CodeMatch(OpCodes.Brfalse)
				)
				.SetOpcodeAndAdvance(OpCodes.Brtrue)
				.InstructionEnumeration()
			;
		}
	}
}
