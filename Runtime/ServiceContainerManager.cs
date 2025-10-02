using System;
using System.Collections.Generic;
using Validosik.Core.Ioc.Generated;
using Validosik.Core.Ioc.Interfaces;
using Validosik.Core.Ioc.Resolvers;

namespace Validosik.Core.Ioc
{
    /// <summary>
    /// Orchestrates multiple containers and a shared storage.
    /// Uses generated ContainerRegistryResolver (no assembly scan).
    /// </summary>
    public class ServiceContainerManager
    {
        private readonly Dictionary<string, IServiceContainer> _containers =
            new Dictionary<string, IServiceContainer>(StringComparer.Ordinal);

        private readonly Dictionary<Type, object> _shared = new Dictionary<Type, object>(); // interface->instance
        private string _activeKey;
        private ResolverContext _context = new ResolverContext();

        public ServiceContainerManager()
        {
            DiscoverFromGeneratedResolver();
        }

        public void SetResolverContext(ResolverContext ctx) => _context = ctx ?? new ResolverContext();
        internal ResolverContext GetResolverContext() => _context;

        public virtual void CreateContainer(string key, IEnumerable<Binding> bindings)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("key");
            }

            if (_containers.ContainsKey(key))
            {
                throw new InvalidOperationException("Container exists: " + key);
            }

            var container = new ServiceContainer(
                bindings,
                getShared: t => _shared.TryGetValue(t, out var v) ? v : null,
                putShared: (t, o) => _shared[t] = o,
                getResolverContext: () => _context
            );
            _containers[key] = container;

            _activeKey ??= key;
        }

        public virtual IServiceContainer GetContainer(string key)
        {
            if (!_containers.TryGetValue(key, out var c))
            {
                throw new KeyNotFoundException("No container: " + key);
            }

            return c;
        }

        public virtual void SwitchContainer(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("key");
            }

            if (!_containers.ContainsKey(key))
            {
                throw new KeyNotFoundException("No container: " + key);
            }

            _activeKey = key; // Shared lives in _shared; nothing else to do
        }

        public IServiceContainer Current => _activeKey != null ? _containers[_activeKey] : null;

        private void DiscoverFromGeneratedResolver()
        {
            foreach (var reg in Validosik.Core.Ioc.Generated.ContainerRegistrySource.GetAll())
            {
                CreateContainer(reg.ContainerKey, reg.GetBindings());
            }
        }
    }
}