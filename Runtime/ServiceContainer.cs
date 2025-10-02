using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Validosik.Core.Ioc.Interfaces;
using Validosik.Core.Ioc.Generated;
using Validosik.Core.Ioc.Resolvers;

namespace Validosik.Core.Ioc
{
    /// <summary>
    /// Simple DI container with Shared/Scoped/Transient lifetimes.
    /// Supports resolver bindings: interface -> resolver -> implementation type at runtime.
    /// Resolver decision is sticky per scope for Scoped/Shared, and evaluated on every call for Transient.
    /// </summary>
    public class ServiceContainer : IServiceContainer
    {
        private readonly Dictionary<Type, Binding> _bindings; // interface -> binding
        private readonly Dictionary<Type, object> _scopedSingles; // interface -> instance (Scoped only)
        private readonly Dictionary<Type, object> _resolverCache; // resolver type -> instance
        private readonly Func<Type, object> _getShared; // shared (by interface)
        private readonly Action<Type, object> _putShared;
        private readonly Func<ResolverContext> _getResolverContext; // provided by manager

        public ServiceContainer(IEnumerable<Binding> bindings,
            Func<Type, object> getShared,
            Action<Type, object> putShared,
            Func<ResolverContext> getResolverContext)
        {
            _bindings = new Dictionary<Type, Binding>();
            _scopedSingles = new Dictionary<Type, object>();
            _resolverCache = new Dictionary<Type, object>();
            _getShared = getShared;
            _putShared = putShared;
            _getResolverContext = getResolverContext ?? (() => new ResolverContext());

            foreach (var b in bindings)
            {
                _bindings[b.InterfaceType] = b;
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
            _resolverCache.Clear();
        }

        public T Resolve<T>() where T : class => (T)Resolve(typeof(T));

        public object Resolve(Type interfaceType)
        {
            if (interfaceType == null) throw new ArgumentNullException(nameof(interfaceType));
            if (!_bindings.TryGetValue(interfaceType, out var binding))
            {
                throw new InvalidOperationException("No binding for " + interfaceType.FullName);
            }

            // Shared/Scoped caches are keyed by interfaceType (not by implementation)
            switch (binding.Lifetime)
            {
                case ServiceLifetime.Shared:
                {
                    var reusable = _getShared(interfaceType);
                    if (reusable != null)
                    {
                        return reusable;
                    }

                    var created = CreateForBinding(binding);
                    _putShared(interfaceType, created);
                    return created;
                }
                case ServiceLifetime.Scoped:
                {
                    if (_scopedSingles.TryGetValue(interfaceType, out var existing))
                    {
                        return existing;
                    }

                    var created = CreateForBinding(binding);
                    _scopedSingles[interfaceType] = created;
                    return created;
                }
                case ServiceLifetime.Transient:
                    return CreateForBinding(binding);

                default:
                    throw new ArgumentOutOfRangeException();
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

        // ---- core helpers ----

        private object CreateForBinding(Binding binding)
        {
            // Direct implementation
            if (!binding.UsesResolver)
            {
                return Create(binding.ImplementationType);
            }

            // Resolver path: pick impl type first
            var implType = ResolveImplementationViaResolver(binding);
            if (implType == null)
            {
                throw new InvalidOperationException(
                    $"Resolver {binding.ResolverType?.FullName} returned null for {binding.InterfaceType?.FullName}");
            }

            if (!binding.InterfaceType.IsAssignableFrom(implType))
            {
                throw new InvalidOperationException(
                    $"Resolver returned {implType.FullName} which does not implement {binding.InterfaceType.FullName}");
            }

            return Create(implType);
        }

        private Type ResolveImplementationViaResolver(Binding binding)
        {
            // cache resolver instance per-container
            if (!_resolverCache.TryGetValue(binding.ResolverType, out var resolverObj))
            {
                resolverObj = Create(binding.ResolverType);
                _resolverCache[binding.ResolverType] = resolverObj;
            }

            // call Resolve(ctx)
            var m = binding.ResolverType.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Instance);
            if (m == null)
            {
                throw new InvalidOperationException("Resolver has no Resolve(ctx) method: " +
                                                    binding.ResolverType.FullName);
            }

            var implType = (Type)m.Invoke(resolverObj, new object[] { _getResolverContext() });
            return implType;
        }

        private object Create(Type impl)
        {
            // Pick constructor: [Inject] > single public > public with max params
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
            for (var i = 0; i < ps.Length; i++)
            {
                var pt = UnwrapParamType(ps[i]);
                if (pt == null)
                {
                    args[i] = GetDefault(ps[i].ParameterType);
                    continue;
                }

                args[i] = Resolve(pt); // resolve by interface
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
                {
                    return pt.GetGenericArguments()[0];
                }
            }

            if (pt.IsPrimitive || pt == typeof(string))
            {
                return null;
            }

            return pt;
        }

        private static object GetDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;
    }
}