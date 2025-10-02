using System;
using System.Collections.Generic;
using System.Linq;
using Validosik.Core.Ioc.Attributes;

namespace Validosik.Core.Ioc
{
    internal sealed class ServiceGraph
    {
        public sealed record Node
        {
            public Type Type { get; }
            public ContainableServiceContractAttribute ContractAttribute { get; }
            public List<Type> Dependencies { get; }

            public Node(Type type, ContainableServiceContractAttribute contractAttribute, List<Type> dependencies)
            {
                Type = type;
                ContractAttribute = contractAttribute;
                Dependencies = dependencies;
            }
        }

        private readonly List<Node> _nodes = new();

        public IReadOnlyList<Node> Nodes => _nodes;

        public void Add(Type type, ContainableServiceContractAttribute contractAttribute, IEnumerable<Type> dependencies)
        {
            _nodes.Add(
                new Node(
                    type,
                    contractAttribute,
                    dependencies.Distinct()
                        .Where(d => d != type)
                        .ToList()
                )
            );
        }
    }
}