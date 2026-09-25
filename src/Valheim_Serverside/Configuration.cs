using BepInEx.Configuration;

namespace PluginConfiguration
{
	public class Configuration
	{
		public static ConfigEntry<bool> modEnabled;

		public static ConfigEntry<bool> maxObjectsPerFrameEnabled;
		public static ConfigEntry<int> maxObjectsPerFrame;

		public static ConfigEntry<int> unityJobWorkers;
		public static ConfigEntry<int> maxZonesPerTick;

		public static ConfigEntry<bool> fixSaveClientChanges;
		public static ConfigEntry<float> stuckLocationPrefabTimeout;

		public static void Load(ConfigFile config)
		{
			modEnabled = config.Bind<bool>("General", "Enabled", true, "Enable or disable the mod");

			maxObjectsPerFrameEnabled = config.Bind<bool>("MaxObjectsPerFrame", "Enabled", true, "Enable or disable the feature");
			maxObjectsPerFrame = config.Bind<int>("MaxObjectsPerFrame", "MaxObjects", 100, "Maximum number of objects the server can create per frame.");

			unityJobWorkers = config.Bind<int>("Server", "UnityJobWorkers", 4,
				"Upper limit on Unity job worker threads. Unity starts one per CPU core, and on many-core hosts the idle ones still use CPU. Only ever lowers the count. 0 leaves Unity's default.");
			maxZonesPerTick = config.Bind<int>("Performance", "MaxZonesPerTick", 1,
				new ConfigDescription("Most new zones the server generates per zone tick (10 ticks a second), shared by all players in turn. A new zone is generated in full in one frame, so several players exploring at once used to cost one zone each in the same frame. 0 = one per player per tick, as before.",
					new AcceptableValueRange<int>(0, 100)));

			fixSaveClientChanges = config.Bind<bool>("Fixes", "SaveClientChanges", true,
				"Mark a world chunk as changed when a player's own change to an object arrives, so the next save writes it. Valheim 1.0 only rewrites changed chunks and does not count changes received from players, so what a player just built or moved could be missing after a restart.");

			stuckLocationPrefabTimeout = config.Bind<float>("Fixes", "StuckLocationPrefabTimeoutSeconds", 300f,
				new ConfigDescription("How long (in seconds) a location's prefab load can sit unfinished before the server force-releases it anyway. Some location loads never complete on this headless server (confirmed live in logs), and without a cap those entries sit in memory forever, growing by one for every such location any player ever explores. 0 disables the timeout, restoring the old unbounded-leak behavior for these.",
					new AcceptableValueRange<float>(0f, 3600f)));
		}
	}
}
