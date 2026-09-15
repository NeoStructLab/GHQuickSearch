using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using Grasshopper.Kernel;

[assembly: InternalsVisibleTo("GHQuickSearch.Tests")]
namespace GHQuickSearch
{
    public sealed class QuickSearchInfo : GH_AssemblyInfo
    {
        public override string Name => "GHQuickSearch";
        public override string Description => "Favorites and native component search in a lightweight canvas popup.";
        public override Guid Id => new Guid("ae498f10-d675-49c7-8d1f-7983fce8f201");
        public override Bitmap Icon => null;
        public override string AuthorName => "GHQuickSearch";
        public override string AuthorContact => "";
    }
    public sealed class QuickSearchPriority : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            PluginRuntime.Start();
            return GH_LoadingInstruction.Proceed;
        }
    }
}
