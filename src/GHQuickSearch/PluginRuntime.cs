using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace GHQuickSearch
{
    internal static class PluginRuntime
    {
        private static bool started;
        private static readonly Dictionary<GH_Canvas, MiddleClickHandler> attached = new Dictionary<GH_Canvas, MiddleClickHandler>();
        public static readonly ComponentCatalog Catalog = new ComponentCatalog();
        public static readonly FavoritesStore Favorites = new FavoritesStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GHQuickSearch", "favorites.json"));
        public static void Start()
        {
            if (started) return;
            started = true;
            Instances.CanvasCreated += Attach;
            if (Instances.ActiveCanvas != null) Attach(Instances.ActiveCanvas);
        }
        internal static void Attach(GH_Canvas canvas)
        {
            if (canvas == null || canvas.IsDisposed || attached.ContainsKey(canvas)) return;
            var handler = new MiddleClickHandler(canvas);
            attached.Add(canvas, handler);
            canvas.AddValidator(handler);
            canvas.Disposed += (s, e) => { handler.Dispose(); attached.Remove(canvas); };
        }
        internal static void Log(Exception ex)
        {
            // No polling or network traffic. Logging is bounded and only on failure.
            try
            {
                var dir = Path.GetDirectoryName(Favorites.FilePath); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "last-error.txt"), DateTime.Now.ToString("O") + "\n" + ex);
            }
            catch { }
        }
    }

    internal sealed class MiddleClickHandler : GH_CanvasValidator, IDisposable
    {
        private readonly GH_Canvas canvas;
        private bool pending, disposed, nativeSearch;
        private Form popup;
        internal MiddleClickHandler(GH_Canvas canvas) { this.canvas = canvas; }
        public override bool CanShowComponentSearchBox(PointF at)
        {
            if(disposed || canvas.IsDisposed || !canvas.IsHandleCreated || !canvas.ModifiersEnabled) return true;
            if(nativeSearch || (Control.ModifierKeys & Keys.Shift)!=0) return true;
            // Only replace canvas double-click search. Canvas-menu validation is
            // inherited unchanged, so the middle button keeps its native menu.
            var client=canvas.PointToClient(Cursor.Position);
            var point=canvas.Viewport.UnprojectPoint(client);
            if(!canvas.ClientRectangle.Contains(client)||Math.Abs(point.X-at.X)>2||Math.Abs(point.Y-at.Y)>2)return true;
            return !OpenPopup(at);
        }
        private bool OpenPopup(PointF location)
        {
            if(pending || (popup!=null && !popup.IsDisposed)) return true;
            var screen=canvas.PointToScreen(Point.Round(canvas.Viewport.ProjectPoint(location)));
            var document = canvas.Document;
            bool InsertAt(Guid id,PointF at)
            {
                var inserted=Insert(id,document,at);
                // Native insertion creates a document when the canvas starts
                // empty. Bind this popup to that document for subsequent drops.
                // Existing-document switches still fail the guard in Insert.
                if(inserted&&document==null)document=canvas.Document;
                return inserted;
            }
            pending = true;
            try
            {
                canvas.BeginInvoke((Action)(() =>
                {
                    pending = false;
                    if (disposed || canvas.IsDisposed || canvas.Document != document) return;
                    try
                    {
                        if (!PluginRuntime.Favorites.IsLoaded)
                            PluginRuntime.Favorites.Load(PluginRuntime.Catalog.Defaults());
                        var items=PluginRuntime.Favorites.Ids.Select(ToCompactItem).ToList();
                        popup = new CompactPopup(items,screen,
                            query=>PluginRuntime.Catalog.Search(query).Select(entry=>ToCompactItem(entry.Id)).ToList(),
                            id=>InsertAt(id,location),null,ids=>PluginRuntime.Favorites.Update(ids),
                            (id,at)=>{
                                if(disposed||canvas.IsDisposed||canvas.Document!=document)return false;
                                var client=canvas.PointToClient(at);
                                if(!canvas.ClientRectangle.Contains(client))return false;
                                return InsertAt(id,canvas.Viewport.UnprojectPoint(client));
                            });
                        popup.Show(canvas.FindForm());
                    }
                    catch (Exception ex) { pending=false;popup?.Dispose();popup=null;PluginRuntime.Log(ex); ShowNative(screen); }
                }));
                return true;
            }
            catch (Exception ex) { pending = false; PluginRuntime.Log(ex); return false; }
        }
        private static CompactItem ToCompactItem(Guid id)
        {
            var entry=PluginRuntime.Catalog.Get(id);
            return new CompactItem{Id=id,Name=entry?.Name??"Unavailable component",Icon=entry?.Proxy.Icon,
                Detail=entry==null?id.ToString():entry.Category+"\n"+entry.Proxy.Desc.Description};
        }
        private bool Insert(Guid id,GH_Document document,PointF location)
        {
            if(disposed||canvas.IsDisposed||canvas.Document!=document)return false;
            try{return canvas.InstantiateNewObject(id,location,true);}
            catch(Exception ex){PluginRuntime.Log(ex);return false;}
        }
        private void OpenSettings(GH_Document document,PointF location,Point screen)
        {
            if(disposed||canvas.IsDisposed)return;
            pending=true;
            try { canvas.BeginInvoke((Action)(()=>{
                pending=false;
                if(disposed||canvas.IsDisposed||canvas.Document!=document)return;
                try
                {
                    popup=new SearchPopup(canvas,document,location,screen,PluginRuntime.Catalog,PluginRuntime.Favorites,()=>ShowNative(screen));
                    popup.Show(canvas.FindForm());
                }
                catch(Exception ex){PluginRuntime.Log(ex);ShowNative(screen);}
            })); }
            catch(Exception ex){pending=false;PluginRuntime.Log(ex);}
        }
        internal void ShowNative(Point screen)
        {
            if (disposed || canvas.IsDisposed) return;
            nativeSearch=true;
            try{canvas.ShowComponentSearchBox(screen);}
            finally{nativeSearch=false;}
        }
        public void Dispose()
        {
            disposed = true;
            if (popup != null && !popup.IsDisposed) popup.Close();
        }
    }
}
