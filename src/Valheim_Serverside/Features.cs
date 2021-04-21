namespace Valheim_Serverside
{
	class FeatureCheckers
	{
		public static bool MaxObjectsPerFrame()
		{
			return ServersidePlugin.configuration.maxObjectsPerFrameEnabled.Value;
		}

		public static bool IsDebug()
		{
#if DEBUG
			return true;
#endif
			return false;
		}
	}
}
