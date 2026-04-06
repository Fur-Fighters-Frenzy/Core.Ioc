using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Validosik.Core.Ioc.Generated;
using Validosik.Core.Ioc.Resolvers;

namespace Validosik.Core.Ioc
{
    /// <summary>
    /// Orchestrates multiple containers and a shared storage.
    /// Uses generated ContainerRegistryResolver (no assembly scan).
    /// </summary>
    public class ServiceContainerManager
    {
        private readonly Dictionary<string, ServiceContainer> _containers =
            new Dictionary<string, ServiceContainer>(StringComparer.Ordinal);

        private readonly Dictionary<Type, object> _shared = new Dictionary<Type, object>(); // interface->instance
        private string _activeKey;

        public event Action<string> ContainerSwitched;

        public ServiceContainerManager()
        {
            DiscoverFromGeneratedResolver();
        }

        public string CurrentKey => _activeKey;

        public virtual void CreateContainer(string key, IEnumerable<Binding> bindings,
            [CanBeNull] Func<ResolverContext> getResolverContext)
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
                getResolverContext: getResolverContext
            );
            _containers[key] = container;

            _activeKey ??= key;
        }

        public virtual void SwitchContainer(string key, [CanBeNull] Func<ResolverContext> getResolverContext)
        {
            var container = GetContainerOrThrow(key);
            _activeKey = key; // Shared lives in _shared; nothing else to do
            if (getResolverContext != null)
            {
                container.SetResolverContextFunc(getResolverContext);
            }

            ContainerSwitched?.Invoke(key);
        }

        public bool ContainsBinding(Type interfaceType)
        {
            return CurrentContainerOrThrow().ContainsBinding(interfaceType);
        }

        public bool ContainsBinding(string key, Type interfaceType)
        {
            return GetContainerOrThrow(key).ContainsBinding(interfaceType);
        }

        public virtual void AddBinding(Binding binding)
        {
            CurrentContainerOrThrow().AddBinding(binding);
        }

        public virtual void AddBinding(string key, Binding binding)
        {
            GetContainerOrThrow(key).AddBinding(binding);
        }

        public virtual bool TryAddBinding(Binding binding)
        {
            return CurrentContainerOrThrow().TryAddBinding(binding);
        }

        public virtual bool TryAddBinding(string key, Binding binding)
        {
            return GetContainerOrThrow(key).TryAddBinding(binding);
        }

        public T Resolve<T>() where T : class => (T)Resolve(typeof(T));

        public object Resolve(Type interfaceType)
        {
            var container = CurrentContainerOrThrow();
            return container.Resolve(interfaceType);
        }

        public bool TryResolve<T>(out T service) where T : class
        {
            var ok = TryResolve(typeof(T), out var obj);
            service = ok ? (T)obj : null;
            return ok;
        }

        public bool TryResolve(Type t, out object obj)
        {
            var container = CurrentContainerOrThrow();
            return container.TryResolve(t, out obj);
        }

        private ServiceContainer CurrentContainerOrThrow()
        {
            if (_activeKey == null || !_containers.TryGetValue(_activeKey, out var container))
            {
                throw new InvalidOperationException("No active container");
            }

            return container;
        }

        private ServiceContainer GetContainerOrThrow(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("key");
            }

            if (!_containers.TryGetValue(key, out var container))
            {
                throw new KeyNotFoundException("No container: " + key);
            }

            return container;
        }

        private void DiscoverFromGeneratedResolver()
        {
            foreach (var reg in Validosik.Core.Ioc.Generated.ContainerRegistrySource.GetAll())
            {
                CreateContainer(reg.ContainerKey, reg.GetBindings(), null);
            }
        }
    }
}