using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Validosik.Core.Ioc.Interfaces;
using Validosik.Core.Ioc.Generated;
using Validosik.Core.Ioc.Resolvers;

namespace Validosik.Core.Ioc
{
    /// <summary>
    /// DI container with Shared/Scoped/Transient lifetimes + resolver-aware bindings.
    /// </summary>
    internal class ServiceContainer : IServiceContainer
    {
        private static readonly ResolverContext _defaultResolverContext = new ResolverContext();
        private static readonly Type _resolverOpenGenericType = typeof(IContainableResolver<>);

        private readonly Dictionary<Type, Binding> _bindings; // interface -> binding
        private readonly Dictionary<Type, object> _instances; // interface -> pre-registered instance
        private readonly Dictionary<Type, object> _scopedSingles; // interface -> instance (Scoped only)
        private readonly Dictionary<Type, object> _resolversCache;
        private readonly Func<Type, object> _getShared; // get from manager-shared storage
        private readonly Action<Type, object> _putShared; // put into manager-shared storage

        private Func<ResolverContext> _getResolverContext;

        public ServiceContainer(IEnumerable<Binding> bindings,
            Func<Type, object> getShared,
            Action<Type, object> putShared,
            [CanBeNull] Func<ResolverContext> getResolverContext)
        {
            _bindings = new Dictionary<Type, Binding>();
            _instances = new Dictionary<Type, object>();
            _scopedSingles = new Dictionary<Type, object>();
            _resolversCache = new Dictionary<Type, object>();
            _getShared = getShared;
            _putShared = putShared;
            _getResolverContext = getResolverContext ?? (() => _defaultResolverContext);

            foreach (var b in bindings)
            {
                AddBinding(b);
            }
        }

        public void Dispose()
        {
            foreach (var kv in _scopedSingles)
            {
                if (kv.Value is IDisposable d)
                {
                    d.Dispose();
                }
            }

            _scopedSingles.Clear();
            _instances.Clear();
        }

        public void SetResolverContextFunc([NotNull] Func<ResolverContext> getResolverContext)
        {
            _getResolverContext = getResolverContext;
        }

        public bool ContainsBinding(Type interfaceType)
        {
            if (interfaceType == null)
            {
                throw new ArgumentNullException(nameof(interfaceType));
            }

            return _instances.ContainsKey(interfaceType) || _bindings.ContainsKey(interfaceType);
        }

        public void RegisterInstance([NotNull] Type interfaceType, [NotNull] object instance)
        {
            if (!TryRegisterInstance(interfaceType, instance))
            {
                throw new InvalidOperationException("Registration already exists for " + interfaceType.FullName);
            }
        }

        public bool TryRegisterInstance([NotNull] Type interfaceType, [NotNull] object instance)
        {
            ValidateInstanceRegistration(interfaceType, instance);

            if (_instances.ContainsKey(interfaceType) || _bindings.ContainsKey(interfaceType))
            {
                return false;
            }

            _instances.Add(interfaceType, instance);
            return true;
        }

        public bool UnregisterInstance([NotNull] Type interfaceType)
        {
            if (interfaceType == null)
            {
                throw new ArgumentNullException(nameof(interfaceType));
            }

            return _instances.Remove(interfaceType);
        }

        public void AddBinding([NotNull] Binding binding)
        {
            if (!TryAddBinding(binding))
            {
                throw new InvalidOperationException("Binding already exists for " + binding.InterfaceType.FullName);
            }
        }

        public bool TryAddBinding([NotNull] Binding binding)
        {
            ValidateBinding(binding);

            if (_instances.ContainsKey(binding.InterfaceType) || _bindings.ContainsKey(binding.InterfaceType))
            {
                return false;
            }

            _bindings.Add(binding.InterfaceType, binding);
            return true;
        }

        public T Resolve<T>() where T : class => (T)Resolve(typeof(T));

        public object Resolve(Type interfaceType)
        {
            if (interfaceType == null)
            {
                throw new ArgumentNullException(nameof(interfaceType));
            }

            if (_instances.TryGetValue(interfaceType, out var registeredInstance))
            {
                return registeredInstance;
            }

            if (!_bindings.TryGetValue(interfaceType, out var binding))
            {
                throw new InvalidOperationException("No binding for " + interfaceType.FullName);
            }

            // Lifetime cache keys are the interface type (intended semantic).
            switch (binding.Lifetime)
            {
                case ServiceLifetime.Shared:
                {
                    var shared = _getShared(interfaceType);
                    if (shared != null)
                    {
                        return shared;
                    }

                    var (instance, withResolver) = CreateForBinding(interfaceType, binding);
                    if (!withResolver)
                    {
                        _putShared(interfaceType, instance);
                    }

                    return instance;
                }
                case ServiceLifetime.Scoped:
                {
                    if (_scopedSingles.TryGetValue(interfaceType, out var existing))
                    {
                        return existing;
                    }

                    var (instance, withResolver) = CreateForBinding(interfaceType, binding);
                    if (!withResolver)
                    {
                        _scopedSingles[interfaceType] = instance;
                    }

                    return instance;
                }
                case ServiceLifetime.Transient:
                    return CreateForBinding(interfaceType, binding).instance;

                default: throw new ArgumentOutOfRangeException();
            }
        }

        public bool TryResolve<T>(out T service) where T : class
        {
            var ok = TryResolve(typeof(T), out var o);
            service = ok ? (T)o : null;
            return ok;
        }

        public bool TryResolve(Type type, out object service)
        {
            try
            {
                service = Resolve(type);
                return true;
            }
            catch
            {
                service = null;
                return false;
            }
        }

        /// <returns>Instantiated object and flag if it was instantiated using resolver or not</returns>
        private (object instance, bool withResolver) CreateForBinding(Type interfaceType, Binding binding)
        {
            if (!binding.UsesResolver)
            {
                // Normal implementation binding
                return (Create(binding.TargetType), false);
            }

            // binding.TargetType is resolver type: IContainableResolver<TInterface>
            object resolver;
            if (_resolversCache.TryGetValue(binding.TargetType, out var cachedResolver))
            {
                resolver = cachedResolver;
            }
            else
            {
                resolver = _resolversCache[binding.TargetType] = Activator.CreateInstance(binding.TargetType);
            }

            // Call resolver.Resolve(ResolverContext)
            var m = binding.TargetType.GetMethod("Resolve", BindingFlags.Instance | BindingFlags.Public);
            if (m == null)
            {
                throw new InvalidOperationException("Resolver.Resolve(ctx) not found on " +
                                                    binding.TargetType.FullName);
            }

            var implType = (Type)m.Invoke(resolver, new object[] { _getResolverContext() });
            if (implType == null || !interfaceType.IsAssignableFrom(implType))
            {
                throw new InvalidOperationException("Resolver returned invalid type for " + interfaceType.FullName);
            }

            return (Create(implType), true);
        }

        private object Create(Type impl)
        {
            var constructors = impl.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            ConstructorInfo chosen = null;
            if (constructors.Length == 1) chosen = constructors[0];
            if (chosen == null)
            {
                chosen = constructors.FirstOrDefault(c =>
                    c.GetCustomAttributes().Any(a => a.GetType().Name == "InjectAttribute"));
            }

            if (chosen == null)
            {
                chosen = constructors.OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            }

            if (chosen == null)
            {
                throw new InvalidOperationException("No public constructor for " + impl.FullName);
            }

            var ps = chosen.GetParameters();
            if (ps.Length == 0)
            {
                return Activator.CreateInstance(impl);
            }

            var args = new object[ps.Length];
            for (var i = 0; i < ps.Length; ++i)
            {
                var pt = UnwrapParamType(ps[i]);
                if (pt == null)
                {
                    args[i] = GetDefault(ps[i].ParameterType);
                    continue;
                }

                if (pt.IsAssignableFrom(impl))
                {
                    throw new InvalidOperationException(impl.Name +
                                                        " type contains itself in its constructor. Please fix this.");
                }

                args[i] = Resolve(pt);
            }

            return chosen.Invoke(args);
        }

        private static Type UnwrapParamType(ParameterInfo info)
        {
            var pt = info.ParameterType;
            if (pt.IsGenericType)
            {
                var def = pt.GetGenericTypeDefinition();
                if (def == typeof(IEnumerable<>) || def == typeof(Lazy<>) || def == typeof(Func<>))
                    return pt.GetGenericArguments()[0];
            }

            if (pt.IsPrimitive || pt == typeof(string)) return null;
            return pt;
        }

        private static object GetDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

        private static void ValidateBinding([NotNull] Binding binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            if (binding.InterfaceType == null)
            {
                throw new ArgumentException("Binding.InterfaceType is null.", nameof(binding));
            }

            if (binding.TargetType == null)
            {
                throw new ArgumentException("Binding.TargetType is null.", nameof(binding));
            }

            if (binding.TargetType.IsAbstract || binding.TargetType.IsInterface)
            {
                throw new ArgumentException(
                    "Binding target type '" + binding.TargetType.FullName + "' is not instantiable.",
                    nameof(binding));
            }

            if (!binding.UsesResolver)
            {
                if (!binding.InterfaceType.IsAssignableFrom(binding.TargetType))
                {
                    throw new ArgumentException(
                        "Binding target type '" + binding.TargetType.FullName +
                        "' is not assignable to '" + binding.InterfaceType.FullName + "'.",
                        nameof(binding));
                }

                return;
            }

            if (!ImplementsResolverContract(binding.TargetType, binding.InterfaceType))
            {
                throw new ArgumentException(
                    "Resolver type '" + binding.TargetType.FullName +
                    "' must implement IContainableResolver<" + binding.InterfaceType.FullName + ">.",
                    nameof(binding));
            }
        }

        private static void ValidateInstanceRegistration([NotNull] Type interfaceType, [NotNull] object instance)
        {
            if (interfaceType == null)
            {
                throw new ArgumentNullException(nameof(interfaceType));
            }

            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            var instanceType = instance.GetType();
            if (!interfaceType.IsAssignableFrom(instanceType))
            {
                throw new ArgumentException(
                    "Instance type '" + instanceType.FullName +
                    "' is not assignable to '" + interfaceType.FullName + "'.",
                    nameof(instance));
            }
        }

        private static bool ImplementsResolverContract(Type resolverType, Type interfaceType)
        {
            var interfaces = resolverType.GetInterfaces();
            for (var i = 0; i < interfaces.Length; ++i)
            {
                var candidate = interfaces[i];
                if (!candidate.IsGenericType)
                {
                    continue;
                }

                if (candidate.GetGenericTypeDefinition() != _resolverOpenGenericType)
                {
                    continue;
                }

                if (candidate.GetGenericArguments()[0] == interfaceType)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
