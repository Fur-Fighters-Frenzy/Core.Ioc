using Validosik.Core.Ioc.Interfaces;

namespace Validosik.Core.Ioc
{
    public class ServiceContainerManager
    {
        private readonly IServiceContainer[] containers;

        public ServiceContainerManager(int capacity)
        {
            containers = new IServiceContainer[capacity + 1]; // One more for shared services container
        }

        public virtual void CreateContainer(string key, object description)
        {
            
        }

        public virtual void GetContainer(string key)
        {

        }
    }
}