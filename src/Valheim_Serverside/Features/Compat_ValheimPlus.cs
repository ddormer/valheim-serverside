using BepInEx.Bootstrap;
using FeaturesLib;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace Valheim_Serverside.Features
{
	public class Compat_ValheimPlus : IFeature
	{
		public bool haveValheimPlus = false;

		public Compat_ValheimPlus()
		{
			haveValheimPlus = Chainloader.PluginInfos.ContainsKey(ServersidePlugin.ValheimPlusPluginId);
			if (haveValheimPlus)
			{
				TryPatchInventoryAssistant();
				ServersidePlugin.harmony.Patch(
					AccessTools.Method(typeof(ZNetScene), "Awake"),
					postfix: new HarmonyMethod(typeof(ZNetScene_Awake_Patch), nameof(ZNetScene_Awake_Patch.Postfix))
				);
			}
		}

		public bool FeatureEnabled()
		{
			// TODO: config?
			return haveValheimPlus;
		}

		public class ZNetScene_Awake_Patch
		{
			static bool triedToPatchSmelter = false;

			public static void Postfix()
			{
				// This needs to run after ObjectDB has been initialized, otherwise
				// we get null reference exceptions trying to initialize the type.
				// Only attempt to patch once.
				if (!triedToPatchSmelter)
				{
					TryPatchSmelter();
					triedToPatchSmelter = true;
				}
			}
		}

		private void TryPatchInventoryAssistant()
		{
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
			Type Smelter_UpdateSmelter_Patch = Type.GetType("ValheimPlus.GameClasses.Smelter_UpdateSmelter_Patch, ValheimPlus");
			if (Smelter_UpdateSmelter_Patch != null)
			{
				ServersidePlugin.logger.LogInfo("Patching ValheimPlus.GameClasses.Smelter_UpdateSmelter_Patch.Prefix");
				ServersidePlugin.harmony.Patch(
					AccessTools.Method(Smelter_UpdateSmelter_Patch, "Prefix"),
					transpiler: new HarmonyMethod(typeof(Compat_ValheimPlus), nameof(Compat_ValheimPlus.Transpile_Smelter_FixedUpdate_Patch))
				);
			}
			else
			{
				ServersidePlugin.logger.LogError("Couldn't find ValheimPlus.GameClasses.Smelter_UpdateSmelter_Patch");
			}
		}

		private static IEnumerable<CodeInstruction> Transpile_InventoryAssistant_GetNearbyChests(IEnumerable<CodeInstruction> instructions)
		{
			return new CodeMatcher(instructions)
				// Replace Container.CheckAccess call with Ldc_I4_1 (true)
				.MatchForward(false,
					// The below matcher is commented out and the
					// `RemoveInstructions` param was set to 3 from 4. The
					// original problem is that a jump points to this `Ldloc_S`
					// by label, and removing the instruction prevents the
					// branch/jump from functioning. Another option would be to copy the label onto the inserted `Ldc_I4_1` later on.
					// new CodeMatch(OpCodes.Ldloc_S),
					new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer))),
					new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Player), nameof(Player.GetPlayerID))),
					new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Container), "CheckAccess"))
				)
				.RemoveInstructions(3)
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
