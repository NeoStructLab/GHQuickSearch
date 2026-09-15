using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace GHQuickSearch
{
    internal sealed class SearchPopup : Form
    {
        private readonly GH_Canvas canvas;
        private readonly GH_Document document;
        private readonly PointF insertionPoint;
        private readonly Point screenPoint;
        private readonly ComponentCatalog catalog;
        private readonly FavoritesStore favorites;
        private readonly Action nativeSearch;
        private readonly FlowLayoutPanel favoriteBar = new FlowLayoutPanel();
        private readonly TextBox search = new TextBox();
        private readonly ListBox results = new ListBox();
        private readonly Label status = new Label();
        private readonly Timer debounce = new Timer { Interval = 120 };
        private readonly ToolTip tips = new ToolTip();
        private bool menuOpen, inserting, dragging;
        private Point dragStart;
        private const string FavoriteDragFormat="GHQuickSearch.Favorite";
        private ContextMenuStrip currentMenu;
        private readonly Font detailFont = new Font("Segoe UI", 8.5f);
        private readonly Font resultFont = new Font("Segoe UI", 10f);
        private readonly Font normalFont = new Font("Segoe UI", 9f);
        private readonly Font titleFont = new Font("Segoe UI", 12f, FontStyle.Bold);
        private readonly Font searchFont = new Font("Segoe UI", 12f);
        private static readonly Color Accent = Color.FromArgb(0, 112, 104);

        internal SearchPopup(GH_Canvas canvas, GH_Document document, PointF insertionPoint, Point screenPoint,
            ComponentCatalog catalog, FavoritesStore favorites, Action nativeSearch)
        {
            this.canvas=canvas; this.document=document; this.insertionPoint=insertionPoint;
            this.screenPoint=screenPoint; this.catalog=catalog; this.favorites=favorites; this.nativeSearch=nativeSearch;
            Text="Favorites settings"; Name="GHQuickSearchPopup";
            FormBorderStyle=FormBorderStyle.None; ControlBox=false; ShowInTaskbar=false;
            StartPosition=FormStartPosition.Manual; AutoScaleMode=AutoScaleMode.Dpi; AutoScaleDimensions=new SizeF(96,96);
            ClientSize=new Size(450,540); BackColor=Color.White; Font=normalFont; KeyPreview=true;

            var layout=new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(14), ColumnCount=1, RowCount=7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(var height in new[]{28,22,150,36,28}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));
            Controls.Add(layout);
            var title=new Label {Text="Favorites settings", ForeColor=Accent, Font=titleFont, Dock=DockStyle.Fill, TextAlign=ContentAlignment.MiddleLeft};
            layout.Controls.Add(title,0,0);
            var favTitle=new Label {Text="DRAG TO REORDER   ·   Right-click to remove", ForeColor=Color.DimGray, Dock=DockStyle.Fill, Font=detailFont};
            layout.Controls.Add(favTitle,0,1);
            favoriteBar.Dock=DockStyle.Fill; favoriteBar.AutoScroll=true; favoriteBar.WrapContents=true; favoriteBar.Margin=Padding.Empty;
            favoriteBar.AllowDrop=true;
            favoriteBar.DragEnter+=FavoriteDragOver;favoriteBar.DragOver+=FavoriteDragOver;favoriteBar.DragDrop+=FavoriteDrop;
            layout.Controls.Add(favoriteBar,0,2);
            search.Dock=DockStyle.Fill; search.Font=searchFont; search.Margin=new Padding(0,4,0,4);
            search.AccessibleName="Search components";
            layout.Controls.Add(search,0,3);
            status.Text="Search, then click a result to add it above"; status.ForeColor=Color.DimGray;
            status.Dock=DockStyle.Fill; status.TextAlign=ContentAlignment.MiddleLeft; status.AutoEllipsis=true;
            layout.Controls.Add(status,0,4);
            results.Dock=DockStyle.Fill; results.BorderStyle=BorderStyle.None; results.DrawMode=DrawMode.OwnerDrawFixed;
            results.ItemHeight=44; results.IntegralHeight=false; results.AccessibleName="Component search results";
            layout.Controls.Add(results,0,5);
            var footer=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));
            footer.Controls.Add(new Label {Text="↑ ↓ select   Enter add   Esc close",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=detailFont,ForeColor=Color.DimGray},0,0);
            var native=new LinkLabel {Text="Native search ↗",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,LinkColor=Accent};
            native.LinkClicked+=(s,e)=>{ Close(); if(!canvas.IsDisposed) canvas.BeginInvoke(nativeSearch); };
            tips.SetToolTip(native,"Original Grasshopper search, including numeric sliders and special input syntax. Shortcut: Shift + double-click the canvas.");
            footer.Controls.Add(native,1,0); layout.Controls.Add(footer,0,6);

            search.TextChanged+=(s,e)=>{debounce.Stop(); debounce.Start();};
            debounce.Tick+=(s,e)=>{debounce.Stop(); SearchNow();};
            results.DrawItem+=DrawResult;
            results.MouseClick+=ResultClick;
            results.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right) ResultMenu(e.Location);};
            Load+=(s,e)=>{
                var area=Screen.FromPoint(screenPoint).WorkingArea;
                Location=PositionAbove(screenPoint,Size,area);
            };
            Shown+=(s,e)=>search.Focus();
            Deactivate+=(s,e)=>{if(!menuOpen && !inserting && !dragging) Close();};
            RebuildFavorites();
            if(favorites.Warning!=null) SetStatus(favorites.Warning,true);
        }
        internal static Point Clamp(Point at, Size size, Rectangle area)
        {
            return new Point(Math.Max(area.Left,Math.Min(at.X,area.Right-size.Width)),
                Math.Max(area.Top,Math.Min(at.Y,area.Bottom-size.Height)));
        }
        internal static Point PositionAbove(Point cursor, Size size, Rectangle area)
        {
            // Anchor the bottom midpoint to the click, before the first visible
            // frame. Clamp only when a screen edge prevents this placement.
            return Clamp(new Point(cursor.X-size.Width/2,cursor.Y-size.Height),size,area);
        }
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters=base.CreateParams;
                parameters.ClassStyle|=0x00020000; // CS_DROPSHADOW: popup shadow, no title bar.
                parameters.ExStyle|=0x00000080; // WS_EX_TOOLWINDOW: keep it out of Alt+Tab.
                return parameters;
            }
        }
        private void RebuildFavorites()
        {
            favoriteBar.SuspendLayout();
            foreach(Control control in favoriteBar.Controls.Cast<Control>().ToArray()) { favoriteBar.Controls.Remove(control); control.Dispose(); }
            foreach(var id in favorites.Ids)
            {
                var entry=catalog.Get(id);
                var button=new Button {Text=entry?.Name ?? "Unavailable",Tag=id,AutoSize=true,MinimumSize=new Size(100,34),Height=34,
                    FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(241,247,246),ForeColor=Accent,
                    Padding=new Padding(5,2,5,2),Margin=new Padding(0,0,6,6),TextImageRelation=TextImageRelation.ImageBeforeText};
                button.FlatAppearance.BorderColor=Color.FromArgb(211,229,226);
                if(entry!=null) button.Image=entry.Proxy.Icon;
                button.Click+=(s,e)=>SetStatus("Drag Favorites to reorder · right-click to remove",false);
                button.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right) FavoriteMenu(button,id);else if(e.Button==MouseButtons.Left)dragStart=e.Location;};
                button.MouseMove+=(s,e)=>{
                    if(e.Button!=MouseButtons.Left||dragging)return;
                    var threshold=SystemInformation.DragSize;
                    if(Math.Abs(e.X-dragStart.X)<threshold.Width&&Math.Abs(e.Y-dragStart.Y)<threshold.Height)return;
                    dragging=true;
                    try{button.DoDragDrop(new DataObject(FavoriteDragFormat,id.ToString()),DragDropEffects.Move);}
                    finally{dragging=false;}
                };
                button.AllowDrop=true;button.DragEnter+=FavoriteDragOver;button.DragOver+=FavoriteDragOver;button.DragDrop+=FavoriteDrop;
                button.DragLeave+=(s,e)=>{button.FlatAppearance.BorderSize=1;button.FlatAppearance.BorderColor=Color.FromArgb(211,229,226);};
                tips.SetToolTip(button,entry==null ? id.ToString() : entry.Name+"\n"+entry.Category+"\nDrag to reorder · right-click to remove");
                favoriteBar.Controls.Add(button);
            }
            if(favorites.Ids.Count==0) favoriteBar.Controls.Add(new Label {AutoSize=true,Text="Search below, then click ★ to pin a component.",ForeColor=Color.DimGray});
            favoriteBar.ResumeLayout(); results.Invalidate();
        }
        private void FavoriteDragOver(object sender,DragEventArgs e)
        {
            e.Effect=dragging&&e.Data.GetDataPresent(FavoriteDragFormat)?DragDropEffects.Move:DragDropEffects.None;
            if(e.Effect==DragDropEffects.None)return;
            if(sender is Button target)
            {
                target.FlatAppearance.BorderSize=2;target.FlatAppearance.BorderColor=Accent;
                SetStatus((target.PointToClient(new Point(e.X,e.Y)).X<target.Width/2?"Drop before ":"Drop after ")+target.Text,false);
            }
            var point=favoriteBar.PointToClient(new Point(e.X,e.Y));
            if(point.Y<18||point.Y>favoriteBar.ClientSize.Height-18)
            {
                var scroll=favoriteBar.AutoScrollPosition;
                favoriteBar.AutoScrollPosition=new Point(-scroll.X,Math.Max(0,-scroll.Y+(point.Y<18?-12:12)));
            }
        }
        private void FavoriteDrop(object sender,DragEventArgs e)
        {
            if(!dragging||!e.Data.GetDataPresent(FavoriteDragFormat)||!Guid.TryParse(e.Data.GetData(FavoriteDragFormat) as string,out var moving))return;
            int slot=favorites.Ids.Count;
            var target=sender as Button;
            if(target!=null&&target.Tag is Guid targetId)
            {
                slot=favorites.Ids.IndexOf(targetId);
                if(target.PointToClient(new Point(e.X,e.Y)).X>=target.Width/2)slot++;
            }
            Save(FavoritesStore.Reordered(favorites.Ids,moving,slot));
        }
        private void SearchNow()
        {
            if(IsDisposed) return;
            results.BeginUpdate(); results.Items.Clear();
            try
            {
                foreach(var entry in catalog.Search(search.Text)) results.Items.Add(entry);
                if(results.Items.Count>0) results.SelectedIndex=0;
                SetStatus(search.Text.Trim().Length==0 ? "Search components · ★ adds a favorite" :
                    results.Items.Count==0 ? "No components found · try Native search for special inputs" :
                    results.Items.Count+" results · ★ adds a favorite", false);
            }
            catch(Exception ex) {PluginRuntime.Log(ex); SetStatus("Search unavailable. Use Native search below.",true);}
            finally {results.EndUpdate();}
        }
        private void DrawResult(object sender,DrawItemEventArgs e)
        {
            if(e.Index<0 || e.Index>=results.Items.Count) return;
            var entry=(ComponentEntry)results.Items[e.Index]; bool selected=(e.State&DrawItemState.Selected)!=0;
            using(var brush=new SolidBrush(selected ? Color.FromArgb(224,241,238) : Color.White)) e.Graphics.FillRectangle(brush,e.Bounds);
            int scale=Math.Max(24,(int)(24*DeviceDpi/96f));
            var icon=entry.Proxy.Icon;
            if(icon!=null) e.Graphics.DrawImage(icon,new Rectangle(e.Bounds.Left+4,e.Bounds.Top+(e.Bounds.Height-scale)/2,scale,scale));
            int x=e.Bounds.Left+scale+12;
            TextRenderer.DrawText(e.Graphics,entry.Name,resultFont,new Rectangle(x,e.Bounds.Top+3,e.Bounds.Width-x-32,22),Color.FromArgb(35,45,46),TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics,entry.Category,detailFont,new Rectangle(x,e.Bounds.Top+23,e.Bounds.Width-x-32,18),Color.DimGray,TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics,favorites.Ids.Contains(entry.Id)?"★":"☆",resultFont,new Rectangle(e.Bounds.Right-30,e.Bounds.Top,28,e.Bounds.Height),Accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
        private void ResultClick(object sender,MouseEventArgs e)
        {
            if(e.Button!=MouseButtons.Left) return;
            int index=results.IndexFromPoint(e.Location); if(index<0) return;
            results.SelectedIndex=index; var entry=(ComponentEntry)results.Items[index];
            if(e.X>=results.ClientSize.Width-32) ToggleFavorite(entry.Id); else AddFavorite(entry.Id);
        }
        private void AddFavorite(Guid id)
        {
            if(favorites.Ids.Contains(id)){SetStatus("Already in Favorites",false);return;}
            ToggleFavorite(id);
        }
        private void Insert(ComponentEntry entry)
        {
            if(inserting) return;
            if(canvas.IsDisposed || canvas.Document!=document) {SetStatus("The active document changed. Reopen search on that canvas.",true);return;}
            inserting=true;
            try
            {
                // Public GH factory preserves GUID identity, user objects, layout,
                // undo, autosave, solutions and normal creation validators.
                if(canvas.InstantiateNewObject(entry.Id,insertionPoint,true)) {Close();canvas.Focus();canvas.Invalidate();}
                else SetStatus("Grasshopper could not insert this component.",true);
            }
            catch(Exception ex) {PluginRuntime.Log(ex);SetStatus("Could not insert component. See last-error.txt.",true);}
            finally {inserting=false;}
        }
        private void ToggleFavorite(Guid id)
        {
            var next=new List<Guid>(favorites.Ids);
            if(next.Contains(id)) next.Remove(id);
            else {if(next.Count>=FavoritesStore.MaximumFavorites){SetStatus("Favorite limit reached. Remove one first.",true);return;} next.Add(id);}
            Save(next);
        }
        private void Save(IEnumerable<Guid> next)
        {
            try {favorites.Update(next);RebuildFavorites();SetStatus("Favorites saved",false);}
            catch(Exception ex){PluginRuntime.Log(ex);SetStatus("Could not save favorites: "+ex.Message,true);}
        }
        private void FavoriteMenu(Control button,Guid id)
        {
            var menu=NewMenu(); int index=favorites.Ids.IndexOf(id);
            menu.Items.Add("Move earlier",null,(s,e)=>MoveFavorite(id,-1)).Enabled=index>0;
            menu.Items.Add("Move later",null,(s,e)=>MoveFavorite(id,1)).Enabled=index<favorites.Ids.Count-1;
            menu.Items.Add("Remove from Favorites",null,(s,e)=>Save(favorites.Ids.Where(g=>g!=id)));
            menu.Show(button,new Point(0,button.Height));
        }
        private void MoveFavorite(Guid id,int offset)
        {
            var next=new List<Guid>(favorites.Ids); int old=next.IndexOf(id), target=old+offset;
            if(old<0 || target<0 || target>=next.Count) return;
            next.RemoveAt(old);next.Insert(target,id);Save(next);
        }
        private void ResultMenu(Point point)
        {
            int index=results.IndexFromPoint(point);if(index<0)return;
            results.SelectedIndex=index;var entry=(ComponentEntry)results.Items[index];
            var menu=NewMenu();menu.Items.Add(favorites.Ids.Contains(entry.Id)?"Remove from Favorites":"Add to Favorites",null,(s,e)=>ToggleFavorite(entry.Id));
            menu.Show(results,point);
        }
        private ContextMenuStrip NewMenu()
        {
            currentMenu?.Dispose();currentMenu=new ContextMenuStrip();
            currentMenu.Opening+=(s,e)=>menuOpen=true;
            currentMenu.Closed+=(s,e)=>{menuOpen=false;if(!IsDisposed)search.Focus();};
            return currentMenu;
        }
        private void SetStatus(string text,bool error) {status.Text=text;status.ForeColor=error?Color.Firebrick:Color.DimGray;tips.SetToolTip(status,text);}
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
        {
            if(keyData==Keys.Escape){Close();return true;}
            if(keyData==(Keys.Control|Keys.D) && results.SelectedItem is ComponentEntry selected){ToggleFavorite(selected.Id);return true;}
            if(search.Focused || results.Focused)
            {
                if(keyData==Keys.Down || keyData==Keys.Up)
                {
                    if(debounce.Enabled){debounce.Stop();SearchNow();}
                    if(results.Items.Count>0) results.SelectedIndex=Math.Max(0,Math.Min(results.Items.Count-1,results.SelectedIndex+(keyData==Keys.Down?1:-1)));
                    return true;
                }
                if(keyData==Keys.Enter)
                {
                    if(debounce.Enabled){debounce.Stop();SearchNow();}
                    if(results.SelectedItem is ComponentEntry entry) AddFavorite(entry.Id);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg,keyData);
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing){debounce.Dispose();tips.Dispose();currentMenu?.Dispose();detailFont.Dispose();resultFont.Dispose();normalFont.Dispose();titleFont.Dispose();searchFont.Dispose();}
            base.Dispose(disposing);
        }
    }
}
