using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Validosik.Core.Ioc.Attributes;

namespace Validosik.Core.Ioc
{
    internal class ServiceScanner
    {
        private readonly ServiceGraph _graph = new ServiceGraph();

        internal ServiceGraph Scan(params (Type type, ContainableServiceContractAttribute[] attributes)[] types)
        {
            for (var i = 0; i < types.Length; ++i)
            {
                var (type, attributes) = types[i];
                var constructor = PickConstructor(type);
                var dependencies = constructor.GetParameters()
                    .Select(UnwrapParamType)
                    .Where(p => p is not null);

                _graph.Add(type, attributes[0], dependencies);
            }

            return _graph;
        }

        private ConstructorInfo PickConstructor(Type t)
        {
            var constructors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (constructors.Length == 1)
            {
                return constructors[0];
            }

            var withInject = constructors.FirstOrDefault(c => c.GetCustomAttribute<InjectAttribute>() != null);
            if (withInject != null)
            {
                return withInject;
            }

            return constructors.OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        }

        private static Type UnwrapParamType(ParameterInfo info)
        {
            var parameterType = info.ParameterType;

            // IEnumerable<T>, Lazy<T>, Func<T>
            if (parameterType.IsGenericType)
            {
                var typeDefinition = parameterType.GetGenericTypeDefinition();
                if (typeDefinition == typeof(IEnumerable<>)
                    || typeDefinition == typeof(Lazy<>)
                    || typeDefinition == typeof(Func<>))
                {
                    return parameterType.GetGenericArguments()[0];
                }
            }

            if (parameterType.IsPrimitive || parameterType == typeof(string))
            {
                return null;
            }

            return parameterType;
        }
    }
}