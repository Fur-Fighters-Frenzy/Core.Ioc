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
        /// Interface -> Implementation mappings with lifetime
        /// </summary>
        Binding[] GetBindings();
    }

    /// <summary>
    /// Simple DTO for generated bindings.
    /// </summary>
    public sealed class Binding
    {
        public Type InterfaceType { get; private set; }
        public Type ImplementationType { get; private set; }
        public ServiceLifetime Lifetime { get; private set; }

        public Binding(Type interfaceType, Type implementation, ServiceLifetime lifetime)
        {
            InterfaceType = interfaceType;
            ImplementationType = implementation;
            Lifetime = lifetime;
        }
    }
}