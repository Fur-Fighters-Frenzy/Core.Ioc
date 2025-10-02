using System;
using Validosik.Core.Ioc.Resolvers;

namespace Validosik.Core.Ioc.Interfaces
{
    public interface IServiceContainer: IDisposable
    {
        T Resolve<T>() where T: class;
        object Resolve(Type type);

        bool TryResolve<T>(out T service) where T: class;
        bool TryResolve(Type type, out object service);

        void SetResolverContextFunc(Func<ResolverContext> getResolverContext);
    }
}