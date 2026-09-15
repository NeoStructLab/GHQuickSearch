using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;

namespace GHQuickSearch
{
    internal sealed class ComponentEntry
    {
        public IGH_ObjectProxy Proxy { get; }
        public Guid Id => Proxy.Guid;
        public string Name => Proxy.Desc.Name;
        public string Category => Proxy.Desc.Category + " / " + Proxy.Desc.SubCategory;
        public ComponentEntry(IGH_ObjectProxy proxy) { Proxy = proxy; }
        public override string ToString() => Name;
    }
    internal sealed class ComponentCatalog
    {
        private readonly Dictionary<Guid, ComponentEntry> entries = new Dictionary<Guid, ComponentEntry>();
        public ComponentEntry Get(Guid id)
        {
            if (entries.TryGetValue(id, out var entry)) return entry;
            var proxy = Instances.ComponentServer.EmitObjectProxy(id);
            if (proxy == null) return null;
            return entries[id] = new ComponentEntry(proxy);
        }
        public List<ComponentEntry> Search(string query)
        {
            var terms = query.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0) return new List<ComponentEntry>();
            IGH_ObjectProxy[] hits = null;
            double[] weights = null;
            Instances.ComponentServer.FindObjects(terms, 40, ref hits, ref weights);
            return (hits ?? new IGH_ObjectProxy[0]).Where(p => p != null && !p.Obsolete)
                .Select(p => Get(p.Guid)).Where(p => p != null).GroupBy(p => p.Id).Select(g => g.First()).ToList();
        }
        public List<Guid> Defaults()
        {
            var ids = new List<Guid>();
            // Resolve actual installed proxies; never infer or hard-code component GUIDs.
            foreach (var name in new[] { "Panel", "Move", "List Item", "Tree Branch", "Expression" })
            {
                var match = Search(name).Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.Proxy.Location != null &&
                        p.Proxy.Location.StartsWith(System.IO.Path.GetDirectoryName(typeof(Instances).Assembly.Location), StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                if (match != null) ids.Add(match.Id);
            }
            return ids;
        }
    }
}
