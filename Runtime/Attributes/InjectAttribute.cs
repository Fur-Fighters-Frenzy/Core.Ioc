using System;

namespace Validosik.Core.Ioc.Attributes
{
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Constructor | AttributeTargets.Method,
        AllowMultiple = false,
        Inherited = true)]
    public sealed class InjectAttribute : Attribute
    {
        public string Guid { get; }

        public InjectAttribute()
        {
        }

        public InjectAttribute(string guid)
        {
            Guid = guid;
        }
    }
}