using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FeaturesLib;

namespace Valheim_Serverside
{
    //public enum Features { A, B, C }

    class Features
    {
        private static bool Gaurd_ChatRPC()
        {
            return false;
        }
        public const string ChatRPCFeature = "mvp.features.ChatRPCFeature";
        public const Feature ChatRPCFeature = new FeatureGuard("mvp.features.ChatRPCFeature", Gaurd_ChatRPC);
        public AvailableFeatures GetFeatures()
        {
            return new AvailableFeatures().AddFeature().AddFeature();
        }

        public const string DebugChatRPC = Feature()

    }

}
