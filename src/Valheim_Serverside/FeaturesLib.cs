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
		{
			List<Type> rtypes = new List<Type>();

			if (type != null)
			{
				var inner_types = type.GetNestedTypes();
				foreach (Type t in inner_types)
				{
					rtypes.Add(t);
					rtypes.AddRange(GetAllNestedTypes(t));
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
