using System;
using System.Collections.Generic;
using System.Linq;

namespace FeaturesLib
{

	public interface IFeature
	{
		bool FeatureEnabled();
	}

	public class AvailableFeatures
	{
		public readonly List<IFeature> _features;

		public AvailableFeatures()
		{
			_features = new List<IFeature>();
		}

		public AvailableFeatures(List<IFeature> features)
		{
			_features = features;
		}

		public AvailableFeatures AddFeature(IFeature feature)
		{
			_features.Add(feature);
			return this;
		}

		public List<IFeature> EnabledFeatures()
		{
			return _features.Where(feature => feature.FeatureEnabled()).ToList();
		}

		public Type[] GetAllNestedTypes(Type type)
		/* Only finds publicly accessable types.
		 */
		{
			List<Type> rtypes = new List<Type>();
			if (type != null)
			{
				foreach (Type innerType in type.GetNestedTypes())
				{
					rtypes.Add(innerType);
					rtypes.AddRange(GetAllNestedTypes(innerType));
				}
			}
			return rtypes.ToArray();
		}

		public Type[] GetAllNestedTypes()
		{
			return (from list in EnabledFeatures().Select(feature => GetAllNestedTypes(feature.GetType())) from item in list select item).ToArray();
		}
	}
}
