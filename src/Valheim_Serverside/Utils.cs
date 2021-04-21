namespace Valheim_Serverside
{
	public class Utilities
	{
		public static bool IsDebugBuild()
		{
#if DEBUG
			return true;
#endif
			return false;
		}
	}
}
