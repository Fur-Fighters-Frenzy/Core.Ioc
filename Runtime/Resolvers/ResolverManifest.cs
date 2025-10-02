using System;
using System.Collections.Generic;

namespace Validosik.Core.Ioc.Resolvers
{
    public sealed class ResolverManifest<TService>
    {
        public IReadOnlyList<Type> CandidateTypes { get; }
        public IReadOnlyList<ResolverProfile> Profiles { get; }

        public ResolverManifest(IReadOnlyList<Type> candidates, IReadOnlyList<ResolverProfile> profiles = null)
        {
            CandidateTypes = candidates;
            Profiles = profiles ?? Array.Empty<ResolverProfile>();
        }
    }
}