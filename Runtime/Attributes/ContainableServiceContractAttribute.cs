using System;

namespace Validosik.Core.Ioc.Attributes
{
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
    public sealed class ContainableServiceContractAttribute : Attribute
    {
        public string Guid { get; }
        public ServiceLifetime DefaultLifetime { get; }

        public ContainableServiceContractAttribute(string guid,
            ServiceLifetime defaultLifetime = ServiceLifetime.Scoped)
        {
            Guid = guid;
            DefaultLifetime = defaultLifetime;
        }
    }
}