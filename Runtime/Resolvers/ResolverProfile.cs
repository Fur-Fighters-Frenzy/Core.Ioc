using System.Collections.Generic;

namespace Validosik.Core.Ioc.Resolvers
{
    public sealed class ResolverProfile
    {
        public string Name { get; }
        public IReadOnlyDictionary<string, object> Conditions { get; }

        public ResolverProfile(string name, IReadOnlyDictionary<string, object> conditions)
        {
            Name = name;
            Conditions = conditions;
        }
    }
}