using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PatchingLib
{
	public interface IPatchRequirement
	{
		string Name { get; }
		Func<bool> Checker { get; }
	}

	public class PatchRequirements
	{
		readonly Dictionary<string, Func<bool>> _requirements;

		public PatchRequirements()
		{
			_requirements = new Dictionary<string, Func<bool>>();
		}

		public PatchRequirements(Dictionary<string, Func<bool>> requirements)
		{
			_requirements = requirements;
		}

		public PatchRequirements AddRequirement(IPatchRequirement patchRequirement)
		{
			_requirements.Add(patchRequirement.Name, patchRequirement.Checker);
			return this;
		}

		public bool IsAllowed(string requirement_name)
		{
			_requirements.TryGetValue(requirement_name, out Func<bool> checker);
			if (checker != null)
			{
				return checker();
			}
			return false;
		}
	}

	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
	public class PatchRequiresAttribute : Attribute
	{
		public readonly string requirement_name;

		public PatchRequiresAttribute(string requirement_name)
		{
			this.requirement_name = requirement_name;
		}
	}

	public class HarmonyFeaturesPatcher
	{
		private readonly PatchRequirements _patchRequirements;

		public HarmonyFeaturesPatcher(PatchRequirements availableFeatures)
		{
			_patchRequirements = availableFeatures;
		}

		public void PatchAll(Type[] types, Harmony harmony_instance)
		{
			foreach (Type type in types)
			{
				var attributes = type.GetCustomAttributes<PatchRequiresAttribute>().ToList();
				bool enabled = !attributes.Any(attribute => !_patchRequirements.IsAllowed(attribute.requirement_name));
				if (enabled)
				{
					ZLog.Log("Patching: " + type.ToString());
					new PatchClassProcessor(harmony_instance, type).Patch();
				}
				else
				{
					ZLog.Log("Patch disabled: " + type.ToString());
				}
			}
		}
	}
}
