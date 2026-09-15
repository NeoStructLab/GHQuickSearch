using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GHQuickSearch
{
    internal sealed class CompactItem
    {
        public Guid Id;
        public string Name;
        public string Detail;
        public Image Icon;
    }

    internal sealed class CompactPopup : Form, IMessageFilter
    {
        private readonly List<CompactItem> favorites;
        private readonly Action<IReadOnlyList<Guid>> save;
        private bool dragging;
        private Point dragStart;
        private bool dragMoved;
        private bool editDrag;
        private readonly Func<Guid,Point,bool> drop;
        private readonly Func<string,List<CompactItem>> find;
        private readonly Func<Guid,bool> insert;
        private readonly Action settings;
        private readonly Point anchor;
        private readonly Label header=new Label();
        private readonly Panel grid=new Panel();
        private readonly TextBox query=new TextBox();
        private readonly ListBox hits=new ListBox();
        private readonly Label empty=new Label {Text="No matching components",TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.DimGray,Visible=false};
        private readonly ToolTip tips=new ToolTip {InitialDelay=300,ReshowDelay=80,AutoPopDelay=8000};
        private readonly Timer debounce=new Timer {Interval=120};
        private readonly Font textFont=new Font("Segoe UI",9f);
        private bool acting,loaded;
        private int hovered=-1;
        private float scale=1;
        private int favoritesCenter;
        private Point? favoritesLocation;
        private int favoritesBottom;
        private Cursor deleteCursor;
        private IntPtr deleteCursorHandle;
        private IntPtr mouseHook;
        private MouseHookProc mouseHookProc;
        private delegate IntPtr MouseHookProc(int code,IntPtr message,IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id,MouseHookProc callback,IntPtr module,uint thread);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        internal const int Columns=6;

        internal CompactPopup(IReadOnlyList<CompactItem> favorites,Point anchor,Func<string,List<CompactItem>> find,Func<Guid,bool> insert,Action settings,Action<IReadOnlyList<Guid>> save=null,Func<Guid,Point,bool> drop=null)
        {
            this.favorites=favorites.ToList();this.anchor=anchor;this.find=find;this.insert=insert;this.settings=settings;this.save=save;
            this.drop=drop;
            Name="GHQuickSearchCompact";Text="GHQuickSearch";FormBorderStyle=FormBorderStyle.None;
            ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;
            BackColor=Color.FromArgb(235,237,240);Font=textFont;KeyPreview=true;Location=anchor;
            header.Name="SettingsBar";header.AccessibleName="Favorites settings";header.BackColor=Color.FromArgb(178,181,185);header.Cursor=Cursors.Hand;
            header.Click+=(s,e)=>{acting=true;Close();settings();};tips.SetToolTip(header,"Favorites settings · add, remove and drag to reorder");
            grid.Name="FavoritesGrid";grid.AutoScroll=false;grid.Margin=Padding.Empty;
            query.Name="SearchBox";query.AccessibleName="Search components";query.BorderStyle=BorderStyle.None;
            DoubleBuffered=true;
            ResizeRedraw=true;
            header.BackColor=Color.Transparent;
            header.Paint+=(s,e)=>DrawGear(e.Graphics,header.ClientRectangle);
            hits.Name="SearchResults";hits.AccessibleName="Search results";hits.BorderStyle=BorderStyle.None;hits.IntegralHeight=false;
            hits.DrawMode=DrawMode.OwnerDrawFixed;hits.BackColor=BackColor;hits.Visible=false;
            Controls.AddRange(new Control[]{grid,hits,empty,query});
            query.TextChanged+=(s,e)=>{debounce.Stop();debounce.Start();};
            debounce.Tick+=(s,e)=>{debounce.Stop();SearchNow();};
            hits.DrawItem+=DrawHit;
            hits.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left){int i=hits.IndexFromPoint(e.Location);if(i>=0){var item=(CompactItem)hits.Items[i];if(e.X>=hits.ClientSize.Width-Px(28))ToggleFavorite(item);else Insert(item);}}};
            hits.MouseMove+=(s,e)=>{int i=hits.IndexFromPoint(e.Location);if(i!=hovered){hovered=i;tips.SetToolTip(hits,i<0?"":Describe((CompactItem)hits.Items[i]));}};
            Load+=(s,e)=>{scale=DeviceDpi/96f;loaded=true;BuildGrid();Fit();};
            Shown+=(s,e)=>query.Focus();
            Deactivate+=(s,e)=>{if(!acting&&!dragging)Close();};
        }
        private int Px(int value)=>(int)Math.Round(value*scale);
        internal static int GridRows(int count,int columns=Columns)=>Math.Max(1,(count+columns-1)/columns);
        private static string Describe(CompactItem item)=>item.Name+(string.IsNullOrWhiteSpace(item.Detail)?"":"\n"+item.Detail);
        private void BuildGrid()
        {
            while(grid.Controls.Count>0)grid.Controls[0].Dispose();
            foreach(var item in favorites)
            {
                var button=new IconButton {Name="FavoriteIcon",Tag=item.Id,AccessibleName=item.Name,Size=new Size(Px(29),Px(29)),Margin=Padding.Empty,
                    FlatStyle=FlatStyle.Flat,BackColor=BackColor,Image=item.Icon,Cursor=Cursors.Hand,TabStop=true};
                button.FlatAppearance.BorderSize=0;button.FlatAppearance.MouseOverBackColor=Color.FromArgb(200,220,224);
                if(item.Icon==null)button.Text="?";
                tips.SetToolTip(button,Describe(item));button.Click+=(s,e)=>Insert(item);grid.Controls.Add(button);
                button.MouseDown+=(s,e)=>{
                    if(e.Button!=MouseButtons.Left)return;
                    editDrag=(ModifierKeys&Keys.Control)!=0;
                    button.SuppressClick=editDrag;
                    dragging=true;dragMoved=false;dragStart=Cursor.Position;button.Capture=true;
                };
                button.MouseMove+=(s,e)=>{
                    if(!dragging)return;
                    if(Math.Abs(Cursor.Position.X-dragStart.X)>SystemInformation.DragSize.Width/2||Math.Abs(Cursor.Position.Y-dragStart.Y)>SystemInformation.DragSize.Height/2)dragMoved=true;
                    if(dragMoved){button.SuppressClick=true;button.Cursor=editDrag?(RectangleToScreen(ClientRectangle).Contains(Cursor.Position)?Cursors.SizeAll:GetDeleteCursor()):Cursors.Cross;}
                };
                button.MouseUp+=(s,e)=>{
                    if(!dragging)return;
                    dragging=false;button.Capture=false;button.Cursor=Cursors.Hand;
                    if(!dragMoved)return;
                    if(!editDrag)
                    {
                        var screen=Cursor.Position;
                        if(!RectangleToScreen(ClientRectangle).Contains(screen))
                        {
                            DropOnCanvas(item.Id,screen);
                        }
                        return;
                    }
                    var point=PointToClient(Cursor.Position);
                    var next=favorites.ToList();
                    if(!ClientRectangle.Contains(point))next.RemoveAll(f=>f.Id==item.Id);
                    else
                    {
                        var local=grid.PointToClient(Cursor.Position);int slot=favorites.Count;
                        for(int j=0;j<grid.Controls.Count;j++)
                        {var b=grid.Controls[j];if(local.Y<b.Bottom&&(local.Y<b.Top||local.X<b.Left+b.Width/2)){slot=j;break;}}
                        var order=FavoritesStore.Reordered(favorites.Select(f=>f.Id).ToList(),item.Id,slot);
                        next=order.Select(id=>favorites.First(f=>f.Id==id)).ToList();
                    }
                    BeginInvoke((Action)(()=>CommitFavorites(next)));
                };
            }
            if(favorites.Count==0)grid.Controls.Add(new Label {Text="Search and click ☆ to add favorites",AutoSize=true,ForeColor=Color.DimGray,Margin=new Padding(Px(4))});
        }
        private void DropOnCanvas(Guid id,Point screen)
        {
            acting=true;
            try
            {
                drop?.Invoke(id,screen);
                if(!IsDisposed){Activate();query.Focus();}
            }
            catch(Exception ex){PluginRuntime.Log(ex);}
            finally{acting=false;dragging=false;dragMoved=false;}
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Shown is deferred by WinForms. Rhino's initial native message loop
            // need not dispatch it before the first drag starts.
            Application.RemoveMessageFilter(this);
            Application.AddMessageFilter(this);
            mouseHookProc=ObserveMouse;
            mouseHook=SetWindowsHookEx(7,mouseHookProc,IntPtr.Zero,GetCurrentThreadId());
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            if(mouseHook!=IntPtr.Zero){UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}
            Application.RemoveMessageFilter(this);
            base.OnHandleDestroyed(e);
        }
        public bool PreFilterMessage(ref Message message)
        {
            // Observe the original click before dispatch; never consume or replay
            // it, so canvas selection and dragging begin on this same press.
            if(!acting&&!dragging&&!IsDisposed&&
                (message.Msg==0x201||message.Msg==0x204||message.Msg==0x207||
                 message.Msg==0x20B||message.Msg==0xA1||message.Msg==0xA4||message.Msg==0xA7))
            {
                var target=Control.FromHandle(message.HWnd);
                if(target!=this&&(target==null||!Contains(target))&&
                    !RectangleToScreen(ClientRectangle).Contains(Cursor.Position))Close();
            }
            return false;
        }
        private IntPtr ObserveMouse(int code,IntPtr message,IntPtr data)
        {
            var hook=mouseHook;
            if(code>=0&&!acting&&!dragging&&!IsDisposed)
            {
                int kind=message.ToInt32();
                if(kind==0x201||kind==0x204||kind==0x207||kind==0x20B||kind==0xA1)
                {
                    // WH_MOUSE runs in Rhino's native message loop too. The first
                    // two fields of MOUSEHOOKSTRUCT are screen coordinates.
                    var point=new Point(Marshal.ReadInt32(data),Marshal.ReadInt32(data,4));
                    if(!RectangleToScreen(ClientRectangle).Contains(point))Close();
                }
            }
            return CallNextHookEx(hook,code,message,data);
        }
        protected override void WndProc(ref Message message)
        {
            // Activate on the same press without discarding the drag's MouseDown.
            if(message.Msg==0x21){message.Result=new IntPtr(1);return;}
            base.WndProc(ref message);
        }
        private void ToggleFavorite(CompactItem item)
        {
            var next=favorites.ToList();
            if(next.Any(f=>f.Id==item.Id))next.RemoveAll(f=>f.Id==item.Id);else next.Add(item);
            CommitFavorites(next);
        }
        private void CommitFavorites(List<CompactItem> next)
        {
            try{save?.Invoke(next.Select(f=>f.Id).ToList());favorites.Clear();favorites.AddRange(next);BuildGrid();Fit();hits.Invalidate();}
            catch(Exception ex){PluginRuntime.Log(ex);tips.Show("Could not save favorites.",query,2500);}
        }
        private void Fit()
        {
            if(!loaded)return;
            bool searching=query.Text.Trim().Length>0;
            var area=Screen.FromPoint(anchor).WorkingArea;
            int desired=searching?Math.Max(1,Math.Min(9,hits.Items.Count))*Px(32)+Px(6):GridRows(favorites.Count)*Px(29)+Px(8);
            int chrome=Px(26+2);
            int maxBody=Math.Max(Px(38),searching && favoritesLocation.HasValue ? favoritesBottom-area.Top-chrome : area.Height-chrome-Px(16));
            int body=searching?Math.Max(Px(38),Math.Min(desired,maxBody)):desired;
            ClientSize=new Size(Columns*Px(29)+Px(10),chrome+body);
            grid.Bounds=hits.Bounds=empty.Bounds=new Rectangle(Px(6),Px(5),ClientSize.Width-Px(12),body-Px(5));
            grid.Bounds=new Rectangle(Px(4),Px(5),ClientSize.Width-Px(8),body-Px(5));
            grid.SuspendLayout();
            int left=(grid.Width-Columns*Px(29))/2;
            grid.Padding=Padding.Empty;
            for(int i=0;i<grid.Controls.Count;i++)
            {
                grid.Controls[i].Margin=Padding.Empty;
                grid.Controls[i].Location=new Point(left+(i%Columns)*Px(29),(i/Columns)*Px(29));
            }
            grid.ResumeLayout(true);
            query.Bounds=new Rectangle(Px(6),body+Px(5),ClientSize.Width-Px(12),Px(18));
            hits.ItemHeight=Px(32);grid.Visible=!searching;hits.Visible=searching&&hits.Items.Count>0;empty.Visible=searching&&hits.Items.Count==0;
            if(!favoritesLocation.HasValue)
            {
                favoritesCenter=body/2+Px(1);
                favoritesLocation=PositionAtFavorites(anchor,Size,area,favoritesCenter);
                favoritesBottom=favoritesLocation.Value.Y+Height;
            }
            Location=new Point(favoritesLocation.Value.X,favoritesBottom-Height);
            using(var shape=Rounded(new RectangleF(0,0,ClientSize.Width,ClientSize.Height),Px(6)))
            using(var tab=Rounded(new RectangleF((ClientSize.Width-Px(38))/2f,0,Px(38),Px(28)),Px(4)))
            {
                var region=new Region(shape);
                var previous=Region;Region=region;previous?.Dispose();
            }
            Invalidate(true);
        }
        internal static Point PositionAtFavorites(Point cursor,Size size,Rectangle area,int centerY)
        {
            return SearchPopup.Clamp(new Point(cursor.X-size.Width/2,cursor.Y-centerY),size,area);
        }
        private GraphicsPath Rounded(RectangleF r,float radius)
        {
            var path=new GraphicsPath();float d=radius*2;
            path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);
            path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        // Paint the icon and hover from the same bounds; native Button painting
        // can shift the image when hovered, focused or pressed.
        private sealed class IconButton : Button
        {
            internal bool SuppressClick;
            protected override void OnClick(EventArgs e){if(!SuppressClick)base.OnClick(e);}
            private bool hover,pressed;
            internal IconButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
            protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
            protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
            protected override void OnMouseDown(MouseEventArgs e){pressed=e.Button==MouseButtons.Left;Invalidate();base.OnMouseDown(e);}
            protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                var bounds=ClientRectangle;
                if(hover||Focused)
                {
                    using(var fill=new SolidBrush(pressed?Color.FromArgb(184,207,215):Color.FromArgb(210,225,230)))
                        e.Graphics.FillRectangle(fill,bounds);
                }
                if(Image!=null)
                {
                    int size=(int)Math.Round(24*DeviceDpi/96f);
                    e.Graphics.DrawImage(Image,new Rectangle((Width-size)/2,(Height-size)/2,size,size));
                }
                else TextRenderer.DrawText(e.Graphics,"?",Font,bounds,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            }
        }
        private GraphicsPath BodyPath()=>Rounded(new RectangleF(Px(2),Px(2),ClientSize.Width-Px(4),ClientSize.Height-Px(4)),Px(5));
        private GraphicsPath TabPath()=>Rounded(new RectangleF((ClientSize.Width-Px(38))/2f+Px(2),Px(2),Px(34),Px(26)),Px(3));
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var body=BodyPath())using(var tab=TabPath())
            using(var pen=new Pen(Color.FromArgb(105,105,105),1.2f*scale))
            using(var gray=new SolidBrush(Color.FromArgb(196,198,200)))using(var fill=new SolidBrush(BackColor))
            {
                g.FillPath(fill,body);
                var state=g.Save();g.SetClip(body);
                int y=query.Top-Px(5);g.FillRectangle(Brushes.White,0,y,Width,Height-y);
                using(var divider=new Pen(Color.FromArgb(145,145,145),scale))g.DrawLine(divider,0,y,Width,y);
                g.Restore(state);g.DrawPath(pen,body);
            }
        }
        private void DrawGear(Graphics g,Rectangle bounds)
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            float cx=bounds.Width/2f,cy=bounds.Height/2f;
            var points=new PointF[64];
            for(int i=0;i<points.Length;i++)
            {
                double a=i*Math.PI*2/points.Length;float r=Px(i%8<4?7:5);
                points[i]=new PointF(cx+(float)Math.Cos(a)*r,cy+(float)Math.Sin(a)*r);
            }
            using(var path=new GraphicsPath(FillMode.Alternate))using(var brush=new SolidBrush(Color.FromArgb(75,78,81)))
            {path.AddPolygon(points);float r=Px(2);path.AddEllipse(cx-r,cy-r,r*2,r*2);g.FillPath(brush,path);}
        }
        private void SearchNow()
        {
            hits.BeginUpdate();hits.Items.Clear();hovered=-1;tips.SetToolTip(hits,"");
            try
            {
                var results=find(query.Text);
                for(int i=results.Count-1;i>=0;i--)hits.Items.Add(results[i]);
                if(hits.Items.Count>0)hits.SelectedIndex=hits.Items.Count-1;
            }
            catch(Exception ex){PluginRuntime.Log(ex);tips.SetToolTip(query,"Search unavailable. Use the native double-click search.");}
            finally
            {
                hits.EndUpdate();Fit();
                if(hits.Items.Count>0)
                    hits.TopIndex=Math.Max(0,hits.Items.Count-Math.Max(1,hits.ClientSize.Height/hits.ItemHeight));
            }
        }
        private void DrawHit(object sender,DrawItemEventArgs e)
        {
            if(e.Index<0)return;var item=(CompactItem)hits.Items[e.Index];
            using(var fill=new SolidBrush((e.State&DrawItemState.Selected)!=0?Color.FromArgb(202,219,226):BackColor))e.Graphics.FillRectangle(fill,e.Bounds);
            if(item.Icon!=null)e.Graphics.DrawImage(item.Icon,new Rectangle(Px(4),e.Bounds.Top+Px(4),Px(24),Px(24)));
            TextRenderer.DrawText(e.Graphics,item.Name,textFont,new Rectangle(Px(35),e.Bounds.Top,e.Bounds.Width-Px(66),e.Bounds.Height),Color.FromArgb(45,50,57),TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            bool starred=favorites.Any(f=>f.Id==item.Id);
            var star=new PointF[10];float cx=e.Bounds.Right-Px(15),cy=e.Bounds.Top+e.Bounds.Height/2f;
            for(int i=0;i<10;i++){double a=-Math.PI/2+i*Math.PI/5;float r=Px(i%2==0?8:3);star[i]=new PointF(cx+(float)Math.Cos(a)*r,cy+(float)Math.Sin(a)*r);}
            var state=e.Graphics.Save();e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            if(starred)using(var fill=new SolidBrush(Color.FromArgb(235,180,45)))e.Graphics.FillPolygon(fill,star);
            using(var outline=new Pen(starred?Color.FromArgb(192,143,30):Color.FromArgb(162,171,178),Math.Max(1,scale)))e.Graphics.DrawPolygon(outline,star);
            e.Graphics.Restore(state);
        }
        [StructLayout(LayoutKind.Sequential)] private struct IconInfo
        { public bool IsIcon;public int XHotspot,YHotspot;public IntPtr Mask,Color; }
        [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon,out IconInfo info);
        [DllImport("user32.dll")] private static extern IntPtr CreateIconIndirect(ref IconInfo info);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] private static extern bool DestroyCursor(IntPtr cursor);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        private Cursor GetDeleteCursor()
        {
            if(deleteCursor!=null)return deleteCursor;
            using(var bitmap=new Bitmap(Px(64),Px(48)))
            {
                using(var g=Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
                    using(var halo=new Pen(Color.White,Px(5)))using(var ink=new Pen(Color.FromArgb(175,55,50),Px(2)))
                    {
                        g.DrawLine(halo,Px(26),Px(3),Px(38),Px(15));g.DrawLine(halo,Px(38),Px(3),Px(26),Px(15));
                        g.DrawLine(ink,Px(26),Px(3),Px(38),Px(15));g.DrawLine(ink,Px(38),Px(3),Px(26),Px(15));
                    }
                    using(var font=new Font("Segoe UI",9,FontStyle.Bold))
                    using(var format=new StringFormat{Alignment=StringAlignment.Center})
                    {g.FillRectangle(Brushes.White,0,Px(19),Px(64),Px(22));g.DrawString("Delete",font,Brushes.DimGray,new RectangleF(0,Px(20),Px(64),Px(24)),format);}
                }
                var icon=bitmap.GetHicon();
                try
                {
                    if(!GetIconInfo(icon,out var info))return Cursors.Cross;
                    try{info.IsIcon=false;info.XHotspot=Px(32);info.YHotspot=Px(9);deleteCursorHandle=CreateIconIndirect(ref info);}
                    finally{DeleteObject(info.Mask);DeleteObject(info.Color);}
                }
                finally{DestroyIcon(icon);}
            }
            if(deleteCursorHandle==IntPtr.Zero)return Cursors.Cross;
            return deleteCursor=new Cursor(deleteCursorHandle);
        }
        private void Insert(CompactItem item)
        {
            acting=true;
            try{if(insert(item.Id))Close();else tips.Show("Component unavailable or insertion failed.",query,0,-Px(24),2500);}
            finally{acting=false;}
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keys)
        {
            if(keys==Keys.Escape){Close();return true;}
            if(keys==Keys.Enter && (query.Focused||hits.Focused))
            {
                if(debounce.Enabled){debounce.Stop();SearchNow();}
                if(hits.SelectedItem is CompactItem item)Insert(item);return true;
            }
            if((keys==Keys.Up||keys==Keys.Down)&&(query.Focused||hits.Focused))
            {
                if(debounce.Enabled){debounce.Stop();SearchNow();}
                if(hits.Items.Count>0)hits.SelectedIndex=Math.Max(0,Math.Min(hits.Items.Count-1,hits.SelectedIndex+(keys==Keys.Up?-1:1)));return true;
            }
            return base.ProcessCmdKey(ref msg,keys);
        }
        protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80;return p;}}
        protected override void Dispose(bool disposing){if(disposing){if(mouseHook!=IntPtr.Zero){UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}Application.RemoveMessageFilter(this);tips.Dispose();debounce.Dispose();textFont.Dispose();deleteCursor?.Dispose();if(deleteCursorHandle!=IntPtr.Zero){DestroyCursor(deleteCursorHandle);deleteCursorHandle=IntPtr.Zero;}}base.Dispose(disposing);}
    }
}







