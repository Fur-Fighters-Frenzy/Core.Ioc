using System;

namespace Validosik.Core.Ioc.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class InitializableAttribute : Attribute
    {
        public InitializableAttribute(string guid)
        {

        }
    }
}