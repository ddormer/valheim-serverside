using FeaturesLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestValheim_Serverside
{
	public class TestFeatureEnabled : IFeature
	{
		public bool FeatureEnabled()
		{
			return true;
		}

		public static class Test_Patch1
		{

		}
		public static class Test_Patch2
		{

		}
	}
	public class TestFeatureDisabled : IFeature
	{
		public bool FeatureEnabled()
		{
			return false;
		}
		public static class Test_Patch
		{

		}
	}

	[TestClass]
	public class FeaturesLibTests
	{
		[TestMethod]
		public void Test_AvailableFeatures()
		{
			AvailableFeatures foo = new AvailableFeatures();
			foo.AddFeature(new TestFeatureEnabled());
			foo.AddFeature(new TestFeatureEnabled());
			foo.AddFeature(new TestFeatureDisabled());
			Assert.AreEqual(2, foo.EnabledFeatures().Count);
		}

		[TestMethod]
		public void Test_GetAllNestedTypes()
		{
			AvailableFeatures foo = new AvailableFeatures();
			foo.AddFeature(new TestFeatureEnabled());
			foo.AddFeature(new TestFeatureEnabled());
			foo.AddFeature(new TestFeatureDisabled());
			Assert.AreEqual(4, foo.GetAllNestedTypes().Length);
		}
	}
}