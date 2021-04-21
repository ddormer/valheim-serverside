using BepInEx;
using FeaturesLib;
using HarmonyLib;
using PatchingLib;
using PluginConfiguration;
using Requirements;

namespace Valheim_Serverside
{

	[Harmony]
	[BepInPlugin("MVP.Valheim_Serverside_Simulations", "Serverside Simulations", "1.0.3")]

	public class ServersidePlugin : BaseUnityPlugin
	{

		private static ServersidePlugin context;

		public static Configuration configuration;

		private void Awake()
		{
			context = this;

			Configuration.Load(Config);

			if (!ModIsEnabled() || !IsDedicated())
			{
				Logger.LogInfo("Serverside Simulations is disabled");
				return;
			}

			AvailableFeatures availableFeatures = new AvailableFeatures();
			availableFeatures.AddFeature(new Features.Core());
			availableFeatures.AddFeature(new Features.MaxObjectsPerFrame());
			availableFeatures.AddFeature(new Features.Debugging());

			PatchRequirements patchRequirements = new PatchRequirements();
			patchRequirements.AddRequirement(new PatchRequirement.DebugBuild());

			Harmony harmony = new Harmony("MVP.Valheim_Serverside_Simulations");
			new HarmonyFeaturesPatcher(patchRequirements).PatchAll(availableFeatures.GetAllNestedTypes(), harmony);

			Logger.LogInfo("Serverside Simulations installed");
		}

		public bool ModIsEnabled()
		{
			return Configuration.modEnabled.Value;
		}

		public static bool IsDedicated()
		{
			return new ZNet().IsDedicated();
		}
	}

}
