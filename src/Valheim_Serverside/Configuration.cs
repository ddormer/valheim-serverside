using BepInEx.Configuration;
using BepInEx;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valheim_Serverside
{
	class Configuration
	{
		public ConfigEntry<bool> modEnabled;
		public ConfigEntry<int> maxObjectsPerFrame;
		public Configuration(ConfigFile config)
		{
			modEnabled = config.Bind<bool>("General", "Enabled", true, "Enable or disable the mod");
			maxObjectsPerFrame = config.Bind<int>("General", "MaxObjectsPerFrame", 500, "Maximum number of objects the server can create per frame");
		}
	}
}
