using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Validosik.Core.Ioc.Interfaces;
using Validosik.Core.Ioc.Generated;

namespace Validosik.Core.Ioc
{
    /// <summary>
    /// Simple DI container with Shared/Scoped/Transient lifetimes.
    /// Shared instances live in the manager's shared dictionary and are reused across scopes.
    /// </summary>
    public class ServiceContainer : IServiceContainer
    {
        private readonly Dictionary<Type, Binding> _bindings; // interface -> binding
        private readonly Dictionary<Type, object> _scopedSingles; // interface -> instance (Scoped only)
        private readonly Func<Type, object> _getShared; // get from manager-shared storage
        private readonly Action<Type, object> _putShared;

        public ServiceContainer(IEnumerable<Binding> bindings,
            Func<Type, object> getShared,
            Action<Type, object> putShared)
        {
            _bindings = new Dictionary<Type, Binding>();
            _scopedSingles = new Dictionary<Type, object>();
            _getShared = getShared;
            _putShared = putShared;

            foreach (var b in bindings)
                _bindings[b.InterfaceType] = b;
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

        public T Resolve<T>() where T : class
        {
            var obj = Resolve(typeof(T));
            return (T)obj;
        }

        public object Resolve(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            if (!_bindings.TryGetValue(type, out var binding))
            {
                throw new InvalidOperationException("No binding for " + type.FullName);
            }

            switch (binding.Lifetime)
            {
                case ServiceLifetime.Shared:
                {
                    var shared = _getShared(type);
                    if (shared != null) return shared;
                    var created = Create(binding.ImplementationType);
                    _putShared(type, created);
                    return created;
                }
                case ServiceLifetime.Scoped:
                {
                    object existing;
                    if (_scopedSingles.TryGetValue(type, out existing))
                        return existing;
                    var created = Create(binding.ImplementationType);
                    _scopedSingles[type] = created;
                    return created;
                }
                case ServiceLifetime.Transient:
                    return Create(binding.ImplementationType);

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

        private object Create(Type impl)
        {
            // Pick constructor by: [Inject] > single public > public with max params
            var constructors = impl.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            ConstructorInfo chosen = null;

            if (constructors.Length == 1) chosen = constructors[0];
            if (chosen == null)
                chosen = constructors.FirstOrDefault(c =>
                    c.GetCustomAttributes().Any(a => a.GetType().Name == "InjectAttribute"));
            if (chosen == null)
                chosen = constructors.OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();

            if (chosen == null) throw new InvalidOperationException("No public constructor for " + impl.FullName);

            var ps = chosen.GetParameters();
            if (ps.Length == 0) return Activator.CreateInstance(impl);

            var args = new object[ps.Length];
            for (var i = 0; i < ps.Length; i++)
            {
                var pt = UnwrapParamType(ps[i]);
                if (pt == null)
                {
                    args[i] = GetDefault(ps[i].ParameterType);
                    continue;
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

        private static object GetDefault(Type t)
        {
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }
    }
}