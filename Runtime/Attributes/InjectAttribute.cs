using System;

namespace Validosik.Core.Ioc.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class InjectAttribute : Attribute
    {
        public InjectAttribute(string guid)
        {
        }
    }
}