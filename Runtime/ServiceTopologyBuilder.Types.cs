using System;
using System.Collections.Generic;
using System.Linq;

namespace Validosik.Core.Ioc
{
    internal static partial class ServiceTopologyBuilder
    {
        /// <summary>
        /// Directed edge between condensed (SCC) components in the DAG.
        /// </summary>
        internal sealed record Edge
        {
            public CompositeNode From { get; }
            public CompositeNode To { get; }

            public Edge(CompositeNode from, CompositeNode to)
            {
                From = from ?? throw new ArgumentNullException(nameof(from));
                To = to ?? throw new ArgumentNullException(nameof(to));
            }
        }

        /// <summary>
        /// Diagnostics for cycles: multi-node SCC or a self-loop on a single node.
        /// </summary>
        internal enum CycleKind
        {
            SelfLoop, // Single type depends on itself (S -> S)
            MultiNode // Strongly-connected group A -> B -> ... -> A
        }

        /// <summary>
        /// Diagnostics for cycles: multi-node SCC or self-loop on a single node.
        /// </summary>
        internal sealed record CycleInfo
        {
            public CycleKind Kind { get; }
            public IReadOnlyList<Type> Types { get; }

            public CycleInfo(CycleKind kind, IReadOnlyList<Type> types)
            {
                Kind = kind;
                Types = types ?? Array.Empty<Type>();
            }
        }

        /// <summary>
        /// SCC (strongly connected component). A *composite* node that contains one or many service types.
        /// </summary>
        internal sealed record CompositeNode
        {
            /// <summary>Stable SCC id (index in condensation).</summary>
            public int Id { get; }

            /// <summary>Original services that belong to this component (>= 1).</summary>
            public IReadOnlyList<ServiceGraph.Node> Services { get; }

            /// <summary>True if any member has a self-loop (S -> S) within this singleton component.</summary>
            public bool HasSelfLoop { get; }

            /// <summary>True if this component contains >1 service OR is a singleton with self-loop.</summary>
            public bool IsCyclic => HasSelfLoop || (Services?.Count ?? 0) > 1;

            /// <summary>Human-friendly stable display name.</summary>
            public string DisplayName
            {
                get
                {
                    if (Services == null || Services.Count == 0) return $"SCC#{Id} []";
                    if (Services.Count == 1)
                    {
                        var t = Services[0].Type;
                        return t.FullName ?? t.Name;
                    }

                    var names = string.Join(", ", Services.Select(s => s.Type.Name));
                    return $"SCC#{Id} [{names}]";
                }
            }

            public CompositeNode(int id, IReadOnlyList<ServiceGraph.Node> services, bool hasSelfLoop)
            {
                Id = id;
                Services = services ?? Array.Empty<ServiceGraph.Node>();
                HasSelfLoop = hasSelfLoop;
            }
        }

        /// <summary>
        /// Result DTO with all metadata needed by the container and diagnostics.
        /// </summary>
        internal sealed record ServiceTopology
        {
            /// <summary>All condensed components (SCCs).</summary>
            public IReadOnlyList<CompositeNode> Components { get; }

            /// <summary>Fast lookup: original Type -> its condensed component.</summary>
            public IReadOnlyDictionary<Type, CompositeNode> TypeToComponent { get; }

            /// <summary>Condensed DAG edges between components (acyclic).</summary>
            public IReadOnlyList<Edge> Edges { get; }

            /// <summary>Longest-path depth per component (0..MaxDepth).</summary>
            public IReadOnlyDictionary<CompositeNode, int> DepthByComponent { get; }

            /// <summary>Components grouped by depth (Layers[0]..Layers[MaxDepth]).</summary>
            public IReadOnlyList<IReadOnlyList<CompositeNode>> Layers { get; }

            /// <summary>All cycles detected (self-loop or multi-node SCC).</summary>
            public IReadOnlyList<CycleInfo> Cycles { get; }

            /// <summary>
            /// True if there is any self-loop or multi-node cycle.
            /// Policy hint: if true and you have no factories/Lazy to break cycles — treat as blocking.
            /// </summary>
            public bool HasBlockingIssues { get; }

            public ServiceTopology(
                IReadOnlyList<CompositeNode> components,
                IReadOnlyDictionary<Type, CompositeNode> typeToComponent,
                IReadOnlyList<Edge> edges,
                IReadOnlyDictionary<CompositeNode, int> depthByComponent,
                IReadOnlyList<IReadOnlyList<CompositeNode>> layers,
                IReadOnlyList<CycleInfo> cycles,
                bool hasBlockingIssues)
            {
                Components = components ?? Array.Empty<CompositeNode>();
                TypeToComponent = typeToComponent ?? new Dictionary<Type, CompositeNode>();
                Edges = edges ?? Array.Empty<Edge>();
                DepthByComponent = depthByComponent ?? new Dictionary<CompositeNode, int>();
                Layers = layers ?? Array.Empty<IReadOnlyList<CompositeNode>>();
                Cycles = cycles ?? Array.Empty<CycleInfo>();
                HasBlockingIssues = hasBlockingIssues;
            }
        }
    }
}