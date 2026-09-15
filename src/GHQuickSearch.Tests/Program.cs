using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using GHQuickSearch;

class Program
{
    static readonly string RhinoPath=@"C:\Program Files\Rhino 8\System";
    static readonly string GhPath=@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper";
    [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    [STAThread] static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            var name=new AssemblyName(e.Name).Name;
            foreach(var dir in new[]{AppDomain.CurrentDomain.BaseDirectory,RhinoPath,GhPath,Path.Combine(GhPath,"Components")})
                foreach(var ext in new[]{".dll",".gha"}){var path=Path.Combine(dir,name+ext);if(File.Exists(path))return Assembly.LoadFrom(path);}
            return null;
        };
        SetDllDirectory(RhinoPath);
        Environment.SetEnvironmentVariable("PATH",RhinoPath+";"+Environment.GetEnvironmentVariable("PATH"));
        try {StoreTests(); CompactTests(); if(args.Contains("--integration") || args.Contains("--preview")) HostTests(args.Contains("--preview"));return 0;}
        catch(Exception ex){Console.WriteLine(ex);return 1;}
    }
    static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);}
    [MethodImpl(MethodImplOptions.NoInlining)] static void CompactTests()
    {
        var items=new System.Collections.Generic.List<CompactItem>();
        for(int i=0;i<13;i++)items.Add(new CompactItem{Id=Guid.NewGuid(),Name="Favorite "+i,Detail="Test component"});
        Check(CompactPopup.GridRows(6)==1&&CompactPopup.GridRows(7)==2&&CompactPopup.GridRows(13)==3,"favorites grow by full icon rows");
        var anchor=new Point(500,300);int firstHeight=0,firstBottom=0;Guid inserted=Guid.Empty;int settings=0;
        Action<Form> load=f=>typeof(Form).GetMethod("OnLoad",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,new object[]{EventArgs.Empty});
        Action<Control> click=c=>typeof(Control).GetMethod("OnClick",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(c,new object[]{EventArgs.Empty});
        using(var form=new CompactPopup(items.Take(6).ToList(),anchor,q=>q=="move"?new System.Collections.Generic.List<CompactItem>{items[0]}:new System.Collections.Generic.List<CompactItem>(),id=>{inserted=id;return true;},()=>settings++))
        {
            load(form);firstHeight=form.Height;firstBottom=form.Top;
            var search=(TextBox)form.Controls["SearchBox"];search.Text="move";
            typeof(CompactPopup).GetMethod("SearchNow",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,null);
            Check(((ListBox)form.Controls["SearchResults"]).Items.Count==1,"inline query populates search results");
            search.Text="";typeof(CompactPopup).GetMethod("SearchNow",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,null);
            Check(form.Height==firstHeight,"clearing query restores favorite grid dimensions");
            click(form.Controls["FavoritesGrid"].Controls[0]);Check(inserted==items[0].Id,"favorite inserts correct GUID");
        }
        using(var form=new CompactPopup(items,anchor,q=>new System.Collections.Generic.List<CompactItem>(),id=>true,()=>settings++))
        {
            load(form);Check(form.Height>firstHeight&&form.Top+(form.Height-28)/2+1==anchor.Y,"cursor stays at favorites center as rows grow");
            Check(form.Controls["FavoritesGrid"].Controls.Count==13,"all favorites are present without pagination");
            form.PerformLayout();form.Controls["FavoritesGrid"].PerformLayout();
            var buttons=form.Controls["FavoritesGrid"].Controls;
            Check(buttons[0].Top==buttons[5].Top&&buttons[6].Top>buttons[5].Top,"six full-size icons fit before wrapping");
            var grid=form.Controls["FavoritesGrid"];
            int leftGap=grid.Left+buttons[0].Left-2;
            int rightGap=form.ClientSize.Width-2-grid.Left-buttons[5].Right;
            int topGap=grid.Top+buttons[0].Top-2;
            int bottomGap=form.Controls["SearchBox"].Top-5-grid.Top-buttons[12].Bottom;
            Check(Math.Abs(leftGap-rightGap)<=1&&Math.Abs(leftGap-topGap)<=1&&Math.Abs(topGap-bottomGap)<=1,"grid outer padding is balanced on all sides");
            using(var icon=new Bitmap(24,24))using(var painted=new Bitmap(30,30))
            {
                using(var g=Graphics.FromImage(icon))g.Clear(Color.Magenta);
                var button=(Button)buttons[0];button.Image=icon;
                typeof(Control).GetMethod("OnMouseEnter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(button,new object[]{EventArgs.Empty});
                button.DrawToBitmap(painted,new Rectangle(0,0,30,30));
                Check(painted.GetPixel(2,2).ToArgb()==Color.Magenta.ToArgb()&&painted.GetPixel(25,25).ToArgb()==Color.Magenta.ToArgb()&&painted.GetPixel(1,2).ToArgb()!=Color.Magenta.ToArgb()&&painted.GetPixel(26,25).ToArgb()!=Color.Magenta.ToArgb(),"hover icon remains centered within one pixel");
                button.Image=null;
            }
            Check(form.Controls["SettingsBar"]==null,"separate settings removed");
        }
        using(var form=new CompactPopup(items,anchor,q=>items,id=>true,()=>{}))
        {
            load(form);int width=form.Width;var originalBottom=form.Bottom;int searchY=form.Top+form.Controls["SearchBox"].Top;
            ((TextBox)form.Controls["SearchBox"]).Text="ranked";
            typeof(CompactPopup).GetMethod("SearchNow",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,null);
            var results=(ListBox)form.Controls["SearchResults"];
            Check(form.Width==width,"favorites and search keep identical width");
            Check(form.Bottom==originalBottom&&form.Top+form.Controls["SearchBox"].Top==searchY,"search field stays fixed while results expand upward");
            Check(((CompactItem)results.SelectedItem).Id==items[0].Id&&results.SelectedIndex==12,"best result selected at bottom");
            Check(results.TopIndex>0,"long results scroll to best matches near search field");
            using(var bitmap=new Bitmap(form.Width,form.Height))
            {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"compact-preview.png"));}
        }
        System.Collections.Generic.IReadOnlyList<Guid> saved=null;
        using(var form=new CompactPopup(items.Take(1).ToList(),anchor,q=>items,id=>true,null,idsToSave=>saved=idsToSave))
        {
            load(form);
            var toggle=typeof(CompactPopup).GetMethod("ToggleFavorite",BindingFlags.NonPublic|BindingFlags.Instance);
            toggle.Invoke(form,new object[]{items[1]});
            Check(saved.SequenceEqual(new[]{items[0].Id,items[1].Id})&&form.Controls["FavoritesGrid"].Controls.Count==2,"star saves favorite and refreshes grid immediately");
            toggle.Invoke(form,new object[]{items[1]});
            Check(saved.Count==1,"filled star removes favorite");
        }
        int drops=0;Point lastDrop=Point.Empty;
        using(var form=new CompactPopup(items,anchor,q=>items,id=>{throw new Exception("Drop used click insertion");},null,null,(id,point)=>{drops++;lastDrop=point;return true;}))
        {
            load(form);
            var method=typeof(CompactPopup).GetMethod("DropOnCanvas",BindingFlags.NonPublic|BindingFlags.Instance);
            for(int i=0;i<3;i++)method.Invoke(form,new object[]{items[i].Id,new Point(800+i*50,400)});
            Check(drops==3&&lastDrop==new Point(900,400)&&!form.IsDisposed,"three drops retain popup and forward release coordinates");
            using(var canvas=new Panel())
            {
                typeof(Form).GetMethod("OnActivated",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{EventArgs.Empty});
                var press=Message.Create(canvas.Handle,0x201,IntPtr.Zero,IntPtr.Zero);
                Check(!form.PreFilterMessage(ref press)&&form.IsDisposed,"first canvas press dismisses popup without consuming the press");
            }
        }
        using(var form=new CompactPopup(items,anchor,q=>items,id=>true,null))
        {
            load(form);
            var activation=Message.Create(form.Handle,0x21,IntPtr.Zero,IntPtr.Zero);
            Check((IntPtr)typeof(CompactPopup).GetField("mouseHook",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form)!=IntPtr.Zero,"native mouse listener is ready before first Shown event");
            object[] messageArgs={activation};
            typeof(CompactPopup).GetMethod("WndProc",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,messageArgs);
            Check(((Message)messageArgs[0]).Result==new IntPtr(1),"inactive popup activates without discarding first mouse press");
            var data=Marshal.AllocHGlobal(32);
            try
            {
                Marshal.WriteInt32(data,form.Right+100);Marshal.WriteInt32(data,4,form.Bottom+100);
                typeof(CompactPopup).GetMethod("ObserveMouse",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{0,new IntPtr(0x201),data});
                Check(form.IsDisposed,"native mouse route dismisses popup without managed message filter");
            }
            finally{Marshal.FreeHGlobal(data);}
        }
        using(var form=new CompactPopup(items.Take(6).ToList(),anchor,q=>items,id=>true,null))
        {
            load(form);int fixedBottom=form.Bottom;int oldTop=form.Top;
            var many=Enumerable.Range(0,36).Select(i=>new CompactItem{Id=Guid.NewGuid(),Name="Item "+i}).ToList();
            typeof(CompactPopup).GetMethod("CommitFavorites",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{many});
            var grid=(Panel)form.Controls["FavoritesGrid"];
            Check(!grid.AutoScroll&&!grid.VerticalScroll.Visible&&!grid.HorizontalScroll.Visible,"favorites never show scrollbars");
            Check(form.Bottom==fixedBottom&&form.Top<oldTop,"adding favorites grows upwards with fixed search field");
            Check(grid.Controls[35].Bottom<=grid.ClientSize.Height&&grid.Controls[5].Right<=grid.ClientSize.Width,"all 36 favorites fit in six complete rows");
        }
        var ids=items.Take(3).Select(i=>i.Id).ToList();
        Check(FavoritesStore.Reordered(ids,ids[0],3).SequenceEqual(new[]{ids[1],ids[2],ids[0]}),"drag first favorite to end");
        Check(FavoritesStore.Reordered(ids,ids[2],0).SequenceEqual(new[]{ids[2],ids[0],ids[1]}),"drag last favorite to beginning");
        Check(FavoritesStore.Reordered(ids,ids[1],2).SequenceEqual(ids),"drop on own slot preserves order");
        Console.WriteLine("COMPACT POPUP AND REORDER TESTS PASSED");
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static void RadialTests()
    {
        for(int count=1;count<=8;count++)
            for(int i=0;i<count;i++)
            {
                var p=RadialPopup.RingPoint(i,count,248);
                Check(p.X>=21&&p.X<=227&&p.Y>=21&&p.Y<=227,"ring icon fits: "+count+"/"+i);
                for(int j=0;j<i;j++){var q=RadialPopup.RingPoint(j,count,248);Check(Math.Pow(p.X-q.X,2)+Math.Pow(p.Y-q.Y,2)>42*42,"ring icons do not overlap");}
            }
        var items=new System.Collections.Generic.List<RadialItem>();
        for(int i=0;i<17;i++)items.Add(new RadialItem{Id=Guid.NewGuid(),Name="Favorite "+(i+1)});
        int settingsCount=0;Guid inserted=Guid.Empty;
        Action<Control> click=c=>typeof(Control).GetMethod("OnClick",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(c,new object[]{EventArgs.Empty});
        using(var popup=new RadialPopup(items,new Point(500,500),id=>{inserted=id;return true;},()=>settingsCount++))
        {
            typeof(Form).GetMethod("OnLoad",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(popup,new object[]{EventArgs.Empty});
            Check(popup.FormBorderStyle==FormBorderStyle.None,"radial popup has no window title bar");
            Check(popup.Controls.Cast<Control>().Count(c=>c.AccessibleName!=null&&c.AccessibleName.StartsWith("Favorite "))==8,"first page shows eight icons");
            click(popup.Controls.Cast<Control>().First(c=>c.AccessibleName=="Next Favorites"));
            Check(popup.Controls.Cast<Control>().Any(c=>c.AccessibleName=="Favorite 9"),"next page shows next favorites");
            click(popup.Controls.Cast<Control>().First(c=>c.AccessibleName=="Favorite 9"));
            Check(inserted==items[8].Id,"icon click passes the correct component GUID");
        }
        using(var popup=new RadialPopup(new RadialItem[0],new Point(500,500),id=>false,()=>settingsCount++))
        {
            typeof(Form).GetMethod("OnLoad",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(popup,new object[]{EventArgs.Empty});
            click(popup.Controls.Cast<Control>().Single(c=>c.AccessibleName.StartsWith("Settings")));
            Check(settingsCount==1,"settings works even with no favorites");
        }
        Console.WriteLine("RADIAL VIEW TESTS PASSED");
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static void StoreTests()
    {
        var dir=Path.Combine(Path.GetTempPath(),"GHQuickSearch-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var a=Guid.NewGuid();var b=Guid.NewGuid();var c=Guid.NewGuid();
        var path=Path.Combine(dir,"favorites.json");var store=new FavoritesStore(path);store.Load(new[]{a,b});
        Check(store.Ids.SequenceEqual(new[]{a,b}),"initial favorites retain order");
        store.Update(new[]{c,b,c,a});
        var read=new FavoritesStore(path);read.Load(new[]{a});Check(read.Ids.SequenceEqual(new[]{c,b,a}),"favorites persist, reorder and deduplicate");
        read.Update(new Guid[0]);var empty=new FavoritesStore(path);empty.Load(new[]{a});Check(empty.Ids.Count==0,"empty favorites stay empty after restart");
        File.WriteAllText(path,"corrupted");var broken=new FavoritesStore(path);broken.Load(new[]{b});Check(broken.Ids.SequenceEqual(new[]{b})&&broken.Warning!=null,"corrupt JSON falls back to defaults with warning");
        Check(File.ReadAllText(path)=="corrupted","corrupt user file is not silently overwritten");
        broken.Update(new[]{a});Check(File.ReadAllText(path+".bak")=="corrupted","explicit save preserves old file as backup");
        string blocked=Path.Combine(dir,"blocked");Directory.CreateDirectory(blocked);var failed=new FavoritesStore(blocked);failed.Load(new[]{a});
        try {failed.Update(new[]{b});throw new Exception("Expected write failure");}catch(IOException){}
        Check(failed.Ids.SequenceEqual(new[]{a}),"failed save does not mutate in-memory favorites");
        Check(SearchPopup.Clamp(new Point(-5,1050),new Size(450,470),new Rectangle(-1920,0,1920,1080))==new Point(-450,610),"popup clamps on negative-coordinate second monitor");
        Check(SearchPopup.PositionAbove(new Point(960,700),new Size(450,470),new Rectangle(0,0,1920,1080))==new Point(735,230),"popup bottom midpoint is exactly at the click");
        Check(SearchPopup.PositionAbove(new Point(-960,700),new Size(450,470),new Rectangle(-1920,0,1920,1080))==new Point(-1185,230),"above-cursor anchor works on the second monitor");
        Check(SearchPopup.PositionAbove(new Point(50,100),new Size(450,470),new Rectangle(0,0,1920,1080))==new Point(0,0),"top and left edges keep the popup visible");
        Check(SearchPopup.PositionAbove(new Point(1900,900),new Size(450,470),new Rectangle(0,0,1920,1080))==new Point(1470,430),"right edge keeps the popup visible");
        Console.WriteLine("STORE AND PLACEMENT TESTS PASSED");
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static void HostTests(bool preview)
    {
        using(var core=new Rhino.Runtime.InProcess.RhinoCore(new[]{"/nosplash","/notemplate"},Rhino.Runtime.InProcess.WindowStyle.NoWindow))
        { Integration(preview); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static void Integration(bool preview)
    {
        var catalog=new ComponentCatalog();var defaults=catalog.Defaults();
        Check(defaults.Count==5,"all five requested native favorites resolve");
        foreach(var id in defaults) Console.WriteLine(catalog.Get(id).Name+" | "+id+" | "+catalog.Get(id).Proxy.Location);
        Check(catalog.Search("move").Any(p=>p.Name=="Move"),"native search finds Move");
        Check(catalog.Search("list item").Any(p=>p.Name=="List Item"),"native multiword search finds List Item");
        Check(catalog.Search("zzzznonexistent123456").Count==0,"no-match search is empty");
        Check(catalog.Get(Guid.NewGuid())==null,"missing plugin favorite is handled");
        using(var window=new Form {Text="GHQuickSearch Test Canvas",Size=new Size(1150,800),StartPosition=FormStartPosition.CenterScreen})
        using(var canvas=new GH_Canvas {Dock=DockStyle.Fill})
        using(var doc=new GH_Document())
        {
            window.Controls.Add(canvas);canvas.Document=doc;doc.Enabled=true;GH_Document.EnableSolutions=true;
            window.CreateControl();canvas.CreateControl();
            // Native insertion API must preserve component identity, requested location and undo.
            var panel=catalog.Get(defaults[0]);
            int before=doc.Objects.Count;
            Check(canvas.InstantiateNewObject(panel.Id,new PointF(100,100),false),"native instantiation succeeds");
            Check(doc.Objects.Count==before+1&&doc.Objects.Last().ComponentGuid==panel.Id,"correct GUID inserted once");
            doc.Objects.Last().Attributes.PerformLayout();
            var bounds=doc.Objects.Last().Attributes.Bounds;
            Check(Math.Abs(bounds.Left+bounds.Width/2-100)<2&&Math.Abs(bounds.Top+bounds.Height/2-100)<2,"native layout centers component at captured canvas point");
            PluginRuntime.Attach(canvas);PluginRuntime.Attach(canvas);
            Check(canvas.Validator.CanNavigateCanvas(),"navigation remains allowed");
            Check(canvas.Validator.CanDeleteObject(doc.Objects.Last()),"native object deletion remains allowed");
            if(preview){window.Show();Application.Run(window);}
        }
        Console.WriteLine("GRASSHOPPER INTEGRATION TESTS PASSED");
    }
}





