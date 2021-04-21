using BepInEx.Configuration;

namespace Valheim_Serverside
{
	public class Configuration
	{
		public ConfigEntry<bool> modEnabled;

		public ConfigEntry<bool> maxObjectsPerFrameEnabled;
		public ConfigEntry<int> maxObjectsPerFrame;
		public Configuration(ConfigFile config)
		{
			modEnabled = config.Bind<bool>("General", "Enabled", true, "Enable or disable the mod");

			maxObjectsPerFrameEnabled = config.Bind<bool>("MaxObjectsPerFrame", "Enabled", true, "Enable or disable the feature");
			maxObjectsPerFrame = config.Bind<int>("MaxObjectsPerFrame", "MaxObjects", 500, "Maximum number of objects the server can create per frame");
		}
	}
}
