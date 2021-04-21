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
			return (from feature in _features where feature.FeatureEnabled() select feature).ToList();
		}

		public Type[] GetAllNestedTypes()
		{
			return (from list in EnabledFeatures().Select(feature => feature.GetType().GetNestedTypes()) from item in list select item).ToArray();
		}
	}
}
