using PatchingLib;
using System;
using Utilities = Valheim_Serverside.Utilities;
namespace Requirements
{
	public class PatchRequirement
	{
		public class DebugBuild : IPatchRequirement
		{
			public const string name = "DebugBuild";

			string IPatchRequirement.Name => name;

			Func<bool> IPatchRequirement.Checker => Utilities.IsDebugBuild;
		}
	}
}
