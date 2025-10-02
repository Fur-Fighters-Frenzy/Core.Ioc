using System;
using Validosik.Core.Ioc.Interfaces;

namespace Validosik.Core.Ioc.Resolvers
{
    public interface IContainableResolver<TService> where TService : IContainableService
    {
        /// <summary>
        /// Editor-time: candidates and profiles
        /// </summary>
        /// <returns></returns>
        ResolverManifest<TService> Describe();
        
        /// <summary>
        /// Runtime: actual selection
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns>Resolved type</returns>
        Type Resolve(ResolverContext ctx);
    }
}