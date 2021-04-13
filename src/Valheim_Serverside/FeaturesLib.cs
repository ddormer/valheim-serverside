using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using System.Linq;

namespace FeaturesLib
{

    public class FeatureGuard
    {
        private readonly string name;
        private readonly Func<bool> checker;
        
        public FeatureGuard(string name, Func<bool> checker)
        {
            this.name = name;
            this.checker = checker;
        }

        public virtual string Name
        {
            get { return name; }
        }

        public virtual Func<bool> Checker
        {
            get { return checker; }
        }
    }

    public class HarmonyFeaturesPatcher
    {
        private AvailableFeatures availableFeatures;

        public HarmonyFeaturesPatcher(AvailableFeatures availableFeatures)
        {
            this.availableFeatures = availableFeatures;
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

                foreach (RequiredFeatureAttribute attribute in attributes)
                {
                    if (!this.availableFeatures.IsFeatureEnabled(attribute.Name))
                    {
                        continue;
                    }
                }
                new PatchClassProcessor(harmony_instance, type).Patch();
            }
        }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public class RequiredFeatureAttribute : Attribute
    {
        private readonly FeatureGuard guard;
        private readonly string name;

        public RequiredFeatureAttribute(FeatureGuard guard)
        {
            this.guard = guard;
            this.name = guard.Name;
        }

        public virtual string Name
        {
            get { return name; }
        }

        public bool IsFeatureEnabled()
        {
            return this.guard.Checker();
        }
    }

    public class AvailableFeatures
    {
        private Dictionary<string, Func<bool>> features;

        public AvailableFeatures()
        {
            this.features = new Dictionary<string, Func<bool>>();
        }

        public AvailableFeatures(Dictionary<string, Func<bool>> features)
        {
            this.features = features;
        }

        public AvailableFeatures AddFeature(string name, Func<bool> feature)
        {
            this.features.Add(name, feature);
            return this;
        }
        public AvailableFeatures AddFeature(FeatureGuard guard)
        {
            this.features.Add(guard.Name, guard.Checker);
            return this;
        }

        public bool IsFeatureEnabled(string name)
        {
            this.features.TryGetValue(name, out Func<bool> feature);
            if (feature != null)
            {
                return feature();
            }
            return false;
        }

        public bool IsFeatureEnabled(FeatureGuard guard)
        {
            return IsFeatureEnabled(guard.Name);
        }
    }
}
