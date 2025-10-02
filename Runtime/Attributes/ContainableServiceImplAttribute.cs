using System;

namespace Validosik.Core.Ioc.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class ContainableServiceImplAttribute : Attribute
    {
        /// <summary>
        /// Stable interface binding (for serialization)
        /// </summary>
        public string ContractGuid { get; }

        /// <summary>
        /// Stable implementation ID (for migrations/diffs)
        /// </summary>
        public string ImplGuid { get; }

        public ContainableServiceImplAttribute(string contractGuid, string implGuid)
        {
            ContractGuid = contractGuid;
            ImplGuid = implGuid;
        }
    }
}