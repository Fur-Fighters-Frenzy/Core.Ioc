using System;

namespace Validosik.Core.Ioc.Generated
{
    /// <summary>
    /// Implemented by generated container registries (codegen output).
    /// Manager discovers registries via ContainerRegistryResolver (no assembly scan).
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
    /// Binding DTO.
    /// - If UsesResolver == false: TargetType is implementation type.
    /// - If UsesResolver == true:  TargetType is resolver type (IContainableResolver<T>).
    /// </summary>
    public sealed class Binding
    {
        public Type InterfaceType { get; private set; }
        public Type TargetType { get; private set; }
        public ServiceLifetime Lifetime { get; private set; }
        public bool UsesResolver { get; private set; }

        public Binding(Type interfaceType, Type targetType, ServiceLifetime lifetime)
        {
            InterfaceType = interfaceType;
            TargetType = targetType;
            Lifetime = lifetime;
            UsesResolver = false;
        }

        public Binding(Type interfaceType, Type resolverType, ServiceLifetime lifetime, bool usesResolver)
        {
            InterfaceType = interfaceType;
            TargetType = resolverType;
            Lifetime = lifetime;
            UsesResolver = usesResolver;
        }
    }
}