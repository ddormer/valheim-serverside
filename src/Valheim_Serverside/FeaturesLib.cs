using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using System.Linq;

namespace FeaturesLib
{
    public class AvailableFeatures
    {
        readonly Dictionary<string, Func<bool>> _features;
        
        public AvailableFeatures()
        {
            _features = new Dictionary<string, Func<bool>>();
        }

        public AvailableFeatures(Dictionary<string, Func<bool>> features)
        {
            _features = features;
        }

        public AvailableFeatures AddFeature(string name, Func<bool> checker)
        {
            _features.Add(name, checker);
            return this;
        }

        public bool IsFeatureEnabled(string feature_name)
        {
            this._features.TryGetValue(feature_name, out Func<bool> checker);
            if (checker != null)
            {
                return checker();
            }
            return false;
        }
    }

    //  Harmony support:

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public class RequiredFeatureAttribute : Attribute
    {
        public readonly string feature_name;

        public RequiredFeatureAttribute(string feature_name)
        {
            this.feature_name = feature_name;
        }
    }

    public class HarmonyFeaturesPatcher
    {
        private readonly AvailableFeatures _availableFeatures;

        public HarmonyFeaturesPatcher(AvailableFeatures availableFeatures)
        {
            _availableFeatures = availableFeatures;
        }

        public void PatchAll(Assembly assembly, Harmony harmony_instance, bool feature_required = false)
        {
            foreach (Type type in AccessTools.GetTypesFromAssembly(assembly))
            {
                List<RequiredFeatureAttribute> attributes = type.GetCustomAttributes<RequiredFeatureAttribute>().ToList();

                if (feature_required && attributes.Count == 0)
                {
                    continue;
                }

                bool enabled = true;

                foreach (RequiredFeatureAttribute attribute in attributes)
                {
                    if (!_availableFeatures.IsFeatureEnabled(attribute.feature_name))
                    {
                        enabled = false;
                    }
                }

                if (enabled) {
                    new PatchClassProcessor(harmony_instance, type).Patch();
                }
            }
        }
    }
}
