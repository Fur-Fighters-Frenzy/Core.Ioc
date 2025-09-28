using System;
using Validosik.Core.Ioc.Interfaces;

namespace Validosik.Core.Ioc
{
    public class ServiceContainer : IServiceContainer
    {
        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public T Resolve<T>() where T : class
        {
            throw new NotImplementedException();
        }

        public object Resolve(Type type)
        {
            throw new NotImplementedException();
        }

        public bool TryResolve<T>(out T service) where T : class
        {
            throw new NotImplementedException();
        }

        public bool TryResolve(Type type, out object service)
        {
            throw new NotImplementedException();
        }
    }
}