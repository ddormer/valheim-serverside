using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FeaturesLib;
using System.Reflection;

namespace Valheim_Serverside
{
    class FeatureCheckers
    {
        public static bool IsDebug()
        {
#if DEBUG
            return true;
#endif
            return false;
        }
    }


}
