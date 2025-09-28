using System;

namespace Validosik.Core.Ioc.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class ContainableServiceAttribute : Attribute
    {
        private string Guid { get; }

        public ContainableServiceAttribute(string guid)
        {
            Guid = guid;
        }
    }
}