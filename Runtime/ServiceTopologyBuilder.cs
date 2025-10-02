using System;
using System.Collections.Generic;
using System.Linq;

namespace Validosik.Core.Ioc
{
    /// <summary>
    /// Builds a condensed DAG from a service->dependencies graph, detects cycles,
    /// computes longest-path depths and layered initialization order.
    /// Edges are oriented S -> Dep(S). To initialize dependencies first, start from max depth down to 0.
    /// </summary>
    internal static partial class ServiceTopologyBuilder
    {
        /// <summary>
        /// Build the condensed DAG and all metadata.
        /// Complexity: O(V + E).
        /// </summary>
        internal static ServiceTopology Build(ServiceGraph graph)
        {
            if (graph is null) throw new ArgumentNullException(nameof(graph));

            // 1) Index all involved types (nodes and dependencies) and build adjacency.
            var allTypes = graph.Nodes.Select(n => n.Type)
                .Concat(graph.Nodes.SelectMany(n => n.Dependencies))
                .Distinct()
                .ToList();

            var typeIndex = new Dictionary<Type, int>(allTypes.Count);
            for (var i = 0; i < allTypes.Count; i++) typeIndex[allTypes[i]] = i;

            var adj = new List<int>[allTypes.Count];
            for (var i = 0; i < adj.Length; i++) adj[i] = new List<int>();

            // Track self-loops explicitly (Tarjan won't mark singletons with self-loop as multi-size SCC)
            var selfLoop = new bool[allTypes.Count];

            foreach (var n in graph.Nodes)
            {
                if (!typeIndex.TryGetValue(n.Type, out var u)) continue;

                foreach (var d in n.Dependencies)
                {
                    if (!typeIndex.TryGetValue(d, out var v)) continue;
                    adj[u].Add(v);
                    if (u == v) selfLoop[u] = true;
                }
            }

            // 2) Tarjan SCC: comp[v] = sccId
            var comp = TarjanScc(adj);
            var sccCount = comp.Max() + 1;

            // 3) Group original service nodes by sccId; also propagate attributes.
            var sccServices = Enumerable.Range(0, sccCount)
                .Select(_ => new List<ServiceGraph.Node>())
                .ToList();

            // Map Type -> ServiceGraph.Node for quick lookup of attributes/decl
            var typeToNode = graph.Nodes.ToDictionary(n => n.Type, n => n);

            for (var i = 0; i < allTypes.Count; i++)
            {
                var t = allTypes[i];
                if (typeToNode.TryGetValue(t, out var node))
                {
                    sccServices[comp[i]].Add(node);
                }
                else
                {
                    // Type is only present as a dependency (not declared as a service).
                    // You may decide to include it as a "phantom" node or skip it.
                    // Here we skip it in component payload; it's still part of the index/graph.
                }
            }

            // build SCC membership (types) map
            var sccMembers = Enumerable.Range(0, sccCount).Select(_ => new List<Type>()).ToList();
            for (var i = 0; i < allTypes.Count; ++i)
            {
                sccMembers[comp[i]].Add(allTypes[i]);
            }

            // 4) Build composite nodes (SCCs) with self-loop info and member types
            var components = new List<CompositeNode>(sccCount);
            for (var id = 0; id < sccCount; ++id)
            {
                // If component is a singleton and we had a recorded self-loop on its vertex -> HasSelfLoop = true
                var hasSelf = false;
                if (sccServices[id].Count <= 1)
                {
                    // find underlying single type index to check selfLoop
                    // pick first present service type if exists; otherwise try any member type from this SCC
                    var anyMemberIndex = Array.FindIndex(comp, c => c == id);
                    hasSelf = anyMemberIndex >= 0 && selfLoop[anyMemberIndex];
                }

                components.Add(new CompositeNode(
                    id,
                    sccServices[id],
                    sccMembers[id],
                    hasSelf
                ));
            }

            // 5) Build condensed DAG edges and indegree
            var edgeSet = new HashSet<(int From, int To)>();
            var indegree = new int[sccCount];

            foreach (var n in graph.Nodes)
            {
                if (!typeIndex.TryGetValue(n.Type, out var u)) continue;
                var cu = comp[u];

                foreach (var d in n.Dependencies)
                {
                    if (!typeIndex.TryGetValue(d, out var v)) continue;
                    var cv = comp[v];
                    if (cu == cv) continue; // inside the same component

                    if (edgeSet.Add((cu, cv)))
                        indegree[cv]++;
                }
            }

            var condensedEdges = edgeSet
                .Select(e => new Edge(components[e.From], components[e.To]))
                .ToList();

            // 6) Longest-path depths on DAG (Kahn)
            var depth = new int[sccCount];
            var queue = new Queue<int>(Enumerable.Range(0, sccCount).Where(i => indegree[i] == 0));

            // Classic Kahn: as we "remove" edges, push new zero-indegree nodes.
            // Update depth[v] = max(depth[v], depth[u]+1)
            var dagAdj = Enumerable.Range(0, sccCount).Select(_ => new List<int>()).ToList();
            foreach (var (from, to) in edgeSet) dagAdj[from].Add(to);

            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                foreach (var v in dagAdj[u])
                {
                    if (depth[v] < depth[u] + 1)
                        depth[v] = depth[u] + 1;
                    if (--indegree[v] == 0) queue.Enqueue(v);
                }
            }

            var depthByComponent = new Dictionary<CompositeNode, int>(sccCount);
            for (var id = 0; id < sccCount; id++)
                depthByComponent[components[id]] = depth[id];

            var maxDepth = depthByComponent.Count == 0 ? 0 : depthByComponent.Values.Max();
            var layers = Enumerable.Range(0, maxDepth + 1)
                .Select(_ => (IReadOnlyList<CompositeNode>)new List<CompositeNode>())
                .ToArray();

            foreach (var c in components)
            {
                var d = depthByComponent[c];
                ((List<CompositeNode>)layers[d]).Add(c);
            }

            // 7) Collect cycles (self-loop or multi-node SCC)
            var cycles = new List<CycleInfo>();
            for (var id = 0; id < sccCount; id++)
            {
                var compNode = components[id];
                if (compNode.Services.Count > 1)
                {
                    cycles.Add(new CycleInfo(
                        CycleKind.MultiNode,
                        compNode.Services.Select(s => s.Type).ToArray()
                    ));
                }
                else if (compNode.Services.Count == 1 && compNode.HasSelfLoop)
                {
                    cycles.Add(new CycleInfo(
                        CycleKind.SelfLoop,
                        new[] { compNode.Services[0].Type }
                    ));
                }
            }

            // 8) Expose a map Type -> Component for convenience
            var typeToComponent = new Dictionary<Type, CompositeNode>();
            foreach (var compNode in components)
            foreach (var s in compNode.Services)
                typeToComponent[s.Type] = compNode;

            // 9) Blocking policy flags
            // Business rules you asked for:
            // - Self-loop on a single class => forbidden by policy.
            // - Multi-node SCC => you cannot split init times (mutual deps through others) => forbidden.
            var hasBlocking = cycles.Count > 0;

            return new ServiceTopology(
                components,
                typeToComponent,
                condensedEdges,
                depthByComponent,
                layers,
                cycles,
                hasBlocking
            );
        }

        /// <summary>
        /// Tarjan's SCC: returns comp[v] = component id for each vertex index.
        /// </summary>
        private static int[] TarjanScc(IReadOnlyList<List<int>> adj)
        {
            var n = adj.Count;
            var index = new int[n];
            Array.Fill(index, -1);
            var low = new int[n];
            var onStack = new bool[n];
            var st = new Stack<int>(n);
            int idx = 0, scc = 0;
            var comp = new int[n];

            void Dfs(int v)
            {
                index[v] = low[v] = idx++;
                st.Push(v);
                onStack[v] = true;

                foreach (var w in adj[v])
                {
                    if (index[w] == -1)
                    {
                        Dfs(w);
                        low[v] = Math.Min(low[v], low[w]);
                    }
                    else if (onStack[w])
                    {
                        low[v] = Math.Min(low[v], index[w]);
                    }
                }

                if (low[v] == index[v])
                {
                    int x;
                    do
                    {
                        x = st.Pop();
                        onStack[x] = false;
                        comp[x] = scc;
                    } while (x != v);

                    scc++;
                }
            }

            for (var v = 0; v < n; v++)
                if (index[v] == -1)
                    Dfs(v);

            return comp;
        }
    }
}