using System;

namespace Validosik.Core.Ioc.Generated
{
    /// <summary>
    /// Implemented by generated container registries (codegen output).
    /// Manager discovers all registries at startup and orchestrates containers.
    /// </summary>
    public interface IGeneratedContainerRegistry
    {
        string ContainerKey { get; }

        /// <summary>
        /// Interface mappings with lifetime (either direct implementation, or a resolver)
        /// </summary>
        Binding[] GetBindings();
    }

    /// <summary>
    /// DTO for generated bindings. Either ImplementationType OR ResolverType is set
    /// </summary>
    public sealed class Binding
    {
        public Type InterfaceType { get; private set; }
        public Type ImplementationType { get; private set; } // nullable when using resolver
        public Type ResolverType { get; private set; }       // nullable when direct impl
        public ServiceLifetime Lifetime { get; private set; }

        public bool UsesResolver { get { return ResolverType != null; } }

        public Binding(Type interfaceType, Type implementation, ServiceLifetime lifetime)
        {
            InterfaceType = interfaceType;
            ImplementationType = implementation;
            ResolverType = null;
            Lifetime = lifetime;
        }

        public Binding(Type interfaceType, Type resolverType, ServiceLifetime lifetime, bool _useResolverOverload)
        {
            // Overload tag avoids ctor ambiguity (stringly API from codegen)
            InterfaceType = interfaceType;
            ResolverType = resolverType;
            ImplementationType = null;
            Lifetime = lifetime;
        }
    }
}
