using System;
using System.Collections.Generic;
using System.Linq;
using Validosik.Core.Ioc.Generated;
using Validosik.Core.Ioc.Interfaces;
using Validosik.Core.Ioc.Resolvers;

namespace Validosik.Core.Ioc
{
    /// <summary>
    /// Orchestrates multiple containers (scopes) and one shared storage.
    /// Discovers generated registries via reflection at startup.
    /// Holds ResolverContext used by resolver bindings.
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
            DiscoverGeneratedRegistries();
        }

        /// <summary>Set/replace the resolver context (affects future resolves; existing Scoped/Shared instances remain until scope switch)</summary>
        public void SetResolverContext(ResolverContext ctx)
        {
            _context = ctx ?? new ResolverContext();
        }

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
                getShared: interfaceType => _shared.TryGetValue(interfaceType, out var v) ? v : null,
                putShared: (interfaceType, obj) => { _shared[interfaceType] = obj; },
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

            // Shared instances remain in _shared
            _activeKey = key;
        }

        public IServiceContainer Current => _activeKey != null ? _containers[_activeKey] : null;

        private void DiscoverGeneratedRegistries()
        {
            var registries = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(a =>
                {
                    try
                    {
                        return a.GetTypes();
                    }
                    catch
                    {
                        return Type.EmptyTypes;
                    }
                })
                .Where(t => t is { IsAbstract: false } && typeof(IGeneratedContainerRegistry).IsAssignableFrom(t))
                .ToArray();

            foreach (var t in registries)
            {
                var reg = (IGeneratedContainerRegistry)Activator.CreateInstance(t);
                CreateContainer(reg.ContainerKey, reg.GetBindings());
            }
        }
    }
}