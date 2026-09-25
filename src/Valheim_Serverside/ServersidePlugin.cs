using BepInEx;
using BepInEx.Logging;
using FeaturesLib;
using HarmonyLib;
using PatchingLib;
using PluginConfiguration;
using Requirements;
using Unity.Jobs.LowLevel.Unsafe;

namespace Valheim_Serverside
{

	[Harmony]
	[BepInPlugin("MVP.Valheim_Serverside_Simulations", "Serverside Simulations", "1.1.12")]
	[BepInDependency(ValheimPlusPluginId, BepInDependency.DependencyFlags.SoftDependency)]

	public class ServersidePlugin : BaseUnityPlugin
	{

		private static ServersidePlugin context;

		public static Configuration configuration;

		public static Harmony harmony;

		public const string ValheimPlusPluginId = "org.bepinex.plugins.valheim_plus";

		public static ManualLogSource logger;

		private void Awake()
		{
			context = this;
			logger = Logger;

			Configuration.Load(Config);

			if (!ModIsEnabled())
			{
				Logger.LogInfo("Serverside Simulations is disabled. (configuration)");
				return;
			}
			else if (!IsDedicated())
			{
				Logger.LogInfo("Serverside Simulations is disabled. (not a dedicated server)");
				return;
			}
			Logger.LogInfo("Installing Serverside Simulations");

			// Independent of the Harmony patches below, so this takes effect even if Core
			// fails to apply and the server falls back to running vanilla.
			LimitJobWorkers(Configuration.unityJobWorkers.Value);

			harmony = new Harmony("MVP.Valheim_Serverside_Simulations");

			AvailableFeatures availableFeatures = new AvailableFeatures();
			availableFeatures.AddFeature(new Features.Core());
			availableFeatures.AddFeature(new Features.MaxObjectsPerFrame());
			availableFeatures.AddFeature(new Features.Fixes());
			availableFeatures.AddFeature(new Features.Debugging());
			availableFeatures.AddFeature(new Features.Compat_ValheimPlus());

			PatchRequirements patchRequirements = new PatchRequirements();
			patchRequirements.AddRequirement(new PatchRequirement.DebugBuild());

			new HarmonyFeaturesPatcher(patchRequirements).PatchAll(availableFeatures.GetAllNestedTypes(), harmony);

			Logger.LogInfo("Serverside Simulations installed");
		}

		public bool ModIsEnabled()
		{
			return Configuration.modEnabled.Value;
		}

		/*
			Unity starts a job worker thread per CPU core, and the idle ones still use CPU --
			on a shared machine (this one also runs other apps, VMs, containers) that's pure
			waste on top of everything else competing for the same cores. Only ever lowers the
			count, never raises it above Unity's own default.
		*/
		private void LimitJobWorkers(int limit)
		{
			int current = JobsUtility.JobWorkerCount;
			if (limit <= 0 || limit >= current)
			{
				return;
			}
			JobsUtility.JobWorkerCount = limit;
			Logger.LogInfo($"Unity job worker threads: {current} -> {JobsUtility.JobWorkerCount}");
		}

		public static bool IsDedicated()
		{
			return new ZNet().IsDedicated();
		}
	}

}
