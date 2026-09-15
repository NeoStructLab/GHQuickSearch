using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GHQuickSearch
{
    internal sealed class RadialItem
    {
        public Guid Id;
        public string Name;
        public Image Icon;
    }

    // A small independent view: all document operations remain in the host.
    internal sealed class RadialPopup : Form
    {
        internal const int PageSize = 8;
        private readonly IReadOnlyList<RadialItem> items;
        private readonly Point anchor;
        private readonly Func<Guid,bool> insert;
        private readonly Action settings;
        private readonly ToolTip tips=new ToolTip {InitialDelay=250,ReshowDelay=75,AutoPopDelay=6000,ShowAlways=true};
        private readonly Font labelFont=new Font("Segoe UI",8f);
        private int page;
        private bool acting;

        internal RadialPopup(IReadOnlyList<RadialItem> items,Point anchor,Func<Guid,bool> insert,Action settings)
        {
            this.items=items;this.anchor=anchor;this.insert=insert;this.settings=settings;
            Text="GHQuickSearch";Name="GHQuickSearchRadial";
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
            AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            ClientSize=new Size(248,248);BackColor=Color.Magenta;TransparencyKey=Color.Magenta;DoubleBuffered=true;KeyPreview=true;
            Load+=(s,e)=>{
                Shape();BuildButtons();
                Location=SearchPopup.Clamp(new Point(anchor.X-Width/2,anchor.Y-Height/2),Size,Screen.FromPoint(anchor).WorkingArea);
            };
            Deactivate+=(s,e)=>{if(!acting)Close();};
        }
        internal static PointF RingPoint(int index,int count,float diameter)
        {
            if(count<1 || count>PageSize || index<0 || index>=count)throw new ArgumentOutOfRangeException();
            double angle=-Math.PI/2+2*Math.PI*index/count;
            float radius=diameter*0.335f;
            return new PointF(diameter/2+(float)Math.Cos(angle)*radius,diameter/2+(float)Math.Sin(angle)*radius);
        }
        private void Shape()
        {
            using(var path=new GraphicsPath())
            {
                path.AddEllipse(0,0,ClientSize.Width,ClientSize.Height);
                var old=Region;Region=new Region(path);old?.Dispose();
            }
        }
        private int PageCount=>Math.Max(1,(items.Count+PageSize-1)/PageSize);
        private void BuildButtons()
        {
            SuspendLayout();
            while(Controls.Count>0){var old=Controls[0];Controls.Remove(old);old.Dispose();}
            float scale=ClientSize.Width/248f;
            int count=Math.Min(PageSize,items.Count-page*PageSize);
            for(int i=0;i<count;i++)
            {
                var item=items[page*PageSize+i];
                var center=RingPoint(i,count,ClientSize.Width);
                var button=MakeButton(center,42*scale,item.Name);
                button.Icon=item.Icon;button.Glyph=item.Icon==null?"?":null;
                button.Click+=(s,e)=>{
                    acting=true;
                    try{if(insert(item.Id))Close();else tips.Show("Component unavailable or insertion failed.",button,0,button.Height,2500);}
                    finally{acting=false;}
                };
            }
            var gear=MakeButton(new PointF(ClientSize.Width/2f,ClientSize.Height/2f),44*scale,"Settings · Search and edit Favorites");
            gear.Gear=true;
            gear.Click+=(s,e)=>{acting=true;Close();settings();};
            if(PageCount>1)
            {
                var previous=MakeButton(new PointF(ClientSize.Width/2f-27*scale,ClientSize.Height/2f+35*scale),24*scale,"Previous Favorites");
                previous.Glyph="‹";previous.Click+=(s,e)=>ChangePage(-1);
                var next=MakeButton(new PointF(ClientSize.Width/2f+27*scale,ClientSize.Height/2f+35*scale),24*scale,"Next Favorites");
                next.Glyph="›";next.Click+=(s,e)=>ChangePage(1);
                var indicator=new Label {Text=(page+1)+"/"+PageCount,AutoSize=false,TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.FromArgb(45,50,55),
                    BackColor=Color.Transparent,Font=labelFont,Bounds=new Rectangle((int)(ClientSize.Width/2f-15*scale),(int)(ClientSize.Height/2f+26*scale),(int)(30*scale),(int)(18*scale))};
                Controls.Add(indicator);
            }
            ResumeLayout();Invalidate();
        }
        private IconButton MakeButton(PointF center,float diameter,string name)
        {
            var button=new IconButton {Bounds=new Rectangle((int)(center.X-diameter/2),(int)(center.Y-diameter/2),(int)diameter,(int)diameter),AccessibleName=name};
            tips.SetToolTip(button,name);Controls.Add(button);return button;
        }
        private void ChangePage(int delta){page=(page+delta+PageCount)%PageCount;BuildButtons();}
        protected override void OnMouseWheel(MouseEventArgs e){if(PageCount>1)ChangePage(e.Delta<0?1:-1);base.OnMouseWheel(e);}
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
        {
            if(keyData==Keys.Escape){Close();return true;}
            if(keyData==Keys.Right || keyData==Keys.PageDown){ChangePage(1);return true;}
            if(keyData==Keys.Left || keyData==Keys.PageUp){ChangePage(-1);return true;}
            return base.ProcessCmdKey(ref msg,keyData);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            // Color-key only the space between controls. Do not lower Form.Opacity:
            // that would fade the component icons as well as the background.
            base.OnPaint(e);
        }
        protected override CreateParams CreateParams
        {
            get{var p=base.CreateParams;p.ExStyle|=0x80;return p;}
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing){tips.Dispose();labelFont.Dispose();}base.Dispose(disposing);
        }

        private sealed class IconButton : Control
        {
            internal Image Icon;
            internal string Glyph;
            internal bool Gear;
            private bool hover;
            private readonly Font glyphFont=new Font("Segoe UI",15f);
            public IconButton()
            {
                SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor|ControlStyles.Selectable,true);
                SetStyle(ControlStyles.StandardClick|ControlStyles.StandardDoubleClick,false);
                BackColor=Color.Transparent;Cursor=Cursors.Hand;TabStop=true;
            }
            protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left&&ClientRectangle.Contains(e.Location))OnClick(EventArgs.Empty);}
            protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
            protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
            protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
            protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
            protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){OnClick(EventArgs.Empty);e.Handled=true;}base.OnKeyDown(e);}
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
                var rect=new RectangleF(1,1,Width-2,Height-2);
                // Opaque light badges retain contrast for dark native icons. Draw
                // their outer edge without blending into the transparent key color.
                g.SmoothingMode=SmoothingMode.None;
                using(var fill=new SolidBrush(Gear?Color.FromArgb(66,72,80):hover||Focused?Color.FromArgb(204,235,231):Color.FromArgb(237,240,242)))g.FillEllipse(fill,rect);
                using(var pen=new Pen(hover||Focused?Color.FromArgb(55,144,135):Color.FromArgb(129,142,150)))g.DrawEllipse(pen,rect);
                g.SmoothingMode=SmoothingMode.AntiAlias;
                if(Icon!=null)
                {
                    int edge=(int)(Width*0.66f);g.DrawImage(Icon,new Rectangle((Width-edge)/2,(Height-edge)/2,edge,edge));
                }
                else if(Gear)
                {
                    var points=new PointF[48];float cx=Width/2f,cy=Height/2f;
                    for(int i=0;i<points.Length;i++)
                    {
                        double a=2*Math.PI*i/points.Length;float r=Width*((i%6==0||i%6==5)?0.21f:0.28f);
                        points[i]=new PointF(cx+(float)Math.Cos(a)*r,cy+(float)Math.Sin(a)*r);
                    }
                    using(var brush=new SolidBrush(Color.FromArgb(222,229,235)))g.FillPolygon(brush,points);
                    using(var brush=new SolidBrush(Color.FromArgb(57,66,74)))g.FillEllipse(brush,cx-Width*0.105f,cy-Height*0.105f,Width*0.21f,Height*0.21f);
                }
                else if(Glyph!=null)TextRenderer.DrawText(g,Glyph,glyphFont,ClientRectangle,Color.FromArgb(40,50,58),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            }
            protected override void Dispose(bool disposing){if(disposing)glyphFont.Dispose();base.Dispose(disposing);}
        }
    }
}
