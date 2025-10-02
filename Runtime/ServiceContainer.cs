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
    /// DI container with Shared/Scoped/Transient lifetimes + resolver-aware bindings.
    /// </summary>
    public class ServiceContainer : IServiceContainer
    {
        private readonly Dictionary<Type, Binding> _bindings; // interface -> binding
        private readonly Dictionary<Type, object> _scopedSingles; // interface -> instance (Scoped only)
        private readonly Func<Type, object> _getShared; // get from manager-shared storage
        private readonly Action<Type, object> _putShared; // put into manager-shared storage
        private readonly Func<ResolverContext> _getResolverContext;

        public ServiceContainer(IEnumerable<Binding> bindings,
            Func<Type, object> getShared,
            Action<Type, object> putShared,
            Func<ResolverContext> getResolverContext)
        {
            _bindings = new Dictionary<Type, Binding>();
            _scopedSingles = new Dictionary<Type, object>();
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
        }

        public T Resolve<T>() where T : class => (T)Resolve(typeof(T));

        public object Resolve(Type interfaceType)
        {
            if (interfaceType == null)
            {
                throw new ArgumentNullException(nameof(interfaceType));
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

                    var created = CreateForBinding(interfaceType, binding);
                    _putShared(interfaceType, created);
                    return created;
                }
                case ServiceLifetime.Scoped:
                {
                    if (_scopedSingles.TryGetValue(interfaceType, out var existing))
                    {
                        return existing;
                    }

                    var created = CreateForBinding(interfaceType, binding);
                    _scopedSingles[interfaceType] = created;
                    return created;
                }
                case ServiceLifetime.Transient:
                    return CreateForBinding(interfaceType, binding);

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

        private object CreateForBinding(Type interfaceType, Binding binding)
        {
            if (!binding.UsesResolver)
            {
                // Normal implementation binding
                return Create(binding.TargetType);
            }

            // binding.TargetType is resolver type: IContainableResolver<TInterface>
            var resolver = Activator.CreateInstance(binding.TargetType);
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

            return Create(implType);
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
            for (var i = 0; i < ps.Length; i++)
            {
                var pt = UnwrapParamType(ps[i]);
                args[i] = pt == null ? GetDefault(ps[i].ParameterType) : Resolve(pt);
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
    }
}