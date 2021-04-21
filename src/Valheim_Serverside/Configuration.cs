using BepInEx.Configuration;

namespace Valheim_Serverside
{
	class Configuration
	{
		public ConfigEntry<bool> modEnabled;
		public Configuration(ConfigFile config)
		{
			modEnabled = config.Bind<bool>("General", "Enabled", true, "Enable or disable the mod");
		}
	}
}
