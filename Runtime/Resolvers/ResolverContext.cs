using System.Collections.Generic;

namespace Validosik.Core.Ioc.Resolvers
{
    public class ResolverContext
    {
        public string Platform { get; set; }
        public string QualityLevel { get; set; }
        public string Region { get; set; }
        public IReadOnlyDictionary<string, bool> FeatureFlags { get; set; }

        public ResolverContext()
        {
        }

        public ResolverContext(string platform, string qualityLevel, string region,
            IReadOnlyDictionary<string, bool> featureFlags)
        {
            Platform = platform;
            QualityLevel = qualityLevel;
            Region = region;
            FeatureFlags = featureFlags;
        }
    }
}