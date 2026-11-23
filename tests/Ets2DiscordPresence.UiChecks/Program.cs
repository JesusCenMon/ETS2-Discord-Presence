using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using System.Collections;
internal static class Verify
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    [STAThread] static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Segoe UI", 9));
        Application.SetColorMode(args[1] == "day" ? SystemColorMode.Classic : SystemColorMode.Dark);
        if (args.Length < 2) throw new ArgumentException("Usage: UiChecks <output-directory> <day|night> [es|en] [100..200]");
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var language = args.Length > 2 ? args[2] : "en";
        var percent = args.Length > 3 ? int.Parse(args[3]) : 100;
        var folder = Path.Combine(AppContext.BaseDirectory, "ETS2PresenceData");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "portable"), "");
        File.Delete(Path.Combine(folder, "view-state.json"));
        File.WriteAllText(Path.Combine(folder, "settings.json"), JsonSerializer.Serialize(new {
            Language=language,DiscordLanguage=language,TextPercent=percent,GuideDismissed=true,Sharing=false,
            Theme=args[1],Options=new {ReduceMotion=args[1] == "day",AccentPreset="amber"}}));
        var entries=Enumerable.Range(0,1800).Select(i=> new {
            Id=Guid.NewGuid(),Origin="Lyon",Destination=i % 2 == 0 ? "Turin":"Paris",Cargo="Machinery",
            Finished=DateTimeOffset.UtcNow.AddMinutes(-i),Result="delivered",Km=220.0,DrivingSeconds=5400.0}).ToArray();
        File.WriteAllText(Path.Combine(folder,"trips.json"), JsonSerializer.Serialize(new {Entries=entries,Pending=Array.Empty<object>()}));
        var recordings=Directory.CreateDirectory(Path.Combine(folder,"recordings")).FullName;
        var type=Assembly.Load("ETS2DiscordPresence").GetType("Ets2DiscordPresence.MainForm",true)!;
        var sample=type.GetField("Sample",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null);
        var path=Path.Combine(recordings,"ETS2-quality.jsonl");
        using(var writer=new StreamWriter(path)) {
            writer.WriteLine(JsonSerializer.Serialize(new {SchemaVersion=2,AppVersion="0.8.7",Created=DateTimeOffset.UtcNow,
                StartedAt=DateTimeOffset.UtcNow,Duration=29500,Origin="Lyon",Destination="Turin",FrameCount=60,MarkersCount=1,JourneyType="delivery"}));
            for(var i=0;i<60;i++) writer.WriteLine(JsonSerializer.Serialize(new {ElapsedMs=i*500,Trip=sample,Event=i==0?"pickup":null,Marker=i==10?"Marker":null}));
        }
        Console.WriteLine("Creating test window"); Console.Out.Flush();
        using var form=(Form)Activator.CreateInstance(type,[false])!;
        Console.WriteLine("Window built"); Console.Out.Flush();
        var events=(System.ComponentModel.EventHandlerList)typeof(Control).GetProperty("Events",Flags)!.GetValue(form)!;
        var shown=typeof(Form).GetFields(BindingFlags.Static|BindingFlags.NonPublic).Single(f=>f.Name.Contains("shown",StringComparison.OrdinalIgnoreCase)).GetValue(null)!;
        events.RemoveHandler(shown,events[shown]);
        foreach(var field in new[]{"timer","presenceTimer"}) ((System.Windows.Forms.Timer)type.GetField(field,Flags)!.GetValue(form)!).Stop();
        type.GetField("nextGameCheck",Flags)!.SetValue(form,long.MaxValue);
        form.Opacity=1; form.ShowInTaskbar=false; form.Show(); form.ClientSize=new(1280,900); CallMonitor();
        void CallMonitor()=>type.GetMethod("StartQualityMonitor",Flags)!.Invoke(form,null);
        object? Call(string name,params object[] values)=>type.GetMethod(name,Flags)!.Invoke(form,values);
        object? Field(string name)=>type.GetField(name,Flags)!.GetValue(form);
        IEnumerable<Control> All(Control root)=>root.Controls.Cast<Control>().SelectMany(c=>All(c).Prepend(c));
        void Check(bool value,string message){if(!value)throw new Exception(message); Console.WriteLine("PASS "+message);Console.Out.Flush();}
        void Pump(int ms=360) {var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(5);}}
        void Wait(Func<bool> test,string message){var clock=Stopwatch.StartNew();while(!test() && clock.ElapsedMilliseconds<10000)Pump(20);Check(test(),message);}
        void Save(string name){Pump();using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(output,name+"-"+args[1]+"-"+language+"-"+percent+".png"));}
        type.GetField("demoScenario",Flags)!.SetValue(form,"coach"); Call("RefreshPresence");Pump();
        Save("home");
        Call("Navigate","trip",false);Save("trip");
        Call("Navigate","journal",false);Pump();
        Wait(()=>All(form).OfType<DataGridView>().Any(c=>c.Visible && c.RowCount==1800),"Journal finished background loading");
        var grid=All(form).OfType<DataGridView>().Single(c=>c.Visible);
        Check(grid.VirtualMode && grid.RowCount==1800,"Virtual journal displays 1800 trips");
        Check((string?)grid.Rows[0].Cells["origin"].Value=="Lyon","Virtual cells expose their values");
        grid.ClearSelection();grid.Rows[0].Selected=true;grid.Rows[1].Selected=true;
        Check(All(form).OfType<Button>().Any(b=>b.Visible && b.Text.StartsWith(language=="en"?"Compare (2/":"Comparar (2/") && b.Enabled),"Multi-selection comparison remains available");
        grid.FirstDisplayedScrollingRowIndex=55;
        Call("Navigate","home",false);Call("Navigate","journal",false);Pump();
        Check(ReferenceEquals(grid,All(form).OfType<DataGridView>().Single(c=>c.Visible)),"Journal root reused");
        Check(grid.SelectedRows.Count==2 && grid.FirstDisplayedScrollingRowIndex==55,"Journal selection and scroll restored");
        var query=All(form).OfType<TextBox>().Single(c=>c.Visible);
        query.Text="Paris";Pump(450);Check(grid.RowCount==900,"Journal filtering remains functional");
        query.Clear();Pump(450);Check(grid.RowCount==1800,"Journal filter resets");
        var detail=All(form).OfType<Button>().Single(c=>c.Visible && c.Text==(language=="en"?"Show details":"Ver detalles"));
        grid.ClearSelection();grid.Rows[0].Selected=true;detail.PerformClick();Pump(100);
        Check(All(form).OfType<Button>().Any(c=>c.Visible && c.Text==(language=="en"?"Open full summary":"Abrir resumen completo")),"Journal detail pane opens beside the list");
        Save("journal-detail");
        detail.PerformClick();Pump(60);
        Save("journal");
        Call("Navigate","replay",false);
        Wait(()=>All(form).OfType<ListBox>().Any(c=>c.Visible && c.Items.Count>0),"Recording catalog loaded");
        var list=All(form).OfType<ListBox>().Single(c=>c.Visible);
        var first=list.Items[0];
        Call("Navigate","home",false);Call("Navigate","replay",false);Pump();
        Check(ReferenceEquals(list,All(form).OfType<ListBox>().Single(c=>c.Visible)),"Recording root reused");
        Check(Equals(first,list.Items[0]),"Recording metadata reused");
        Save("recordings");
        var watch=Stopwatch.StartNew();Call("ShowPlayback",path,form);var openTime=watch.ElapsedMilliseconds;
        Console.WriteLine("Playback opening returned in "+openTime+" ms");
        Wait(()=>All(form).OfType<TrackBar>().Any(c=>c.Visible && c.Maximum==29500),"Playback prepared asynchronously");
        var seek=All(form).OfType<TrackBar>().Single(c=>c.Visible && c.Maximum==29500);
        seek.Value=5000;Pump(80);
        Check((bool)Field("replayOpen")!,"Playback suspends live publication");
        Wait(()=>All(form).Any(c=>c.Visible && c.GetType().Name=="SpeedChart" && c.GetType().GetField("plot",Flags)!.GetValue(c) is Bitmap),"Replay graph finishes on a background worker");
        var transportPlay=All(form).OfType<Button>().Single(c=>c.Visible && c.Text==(language=="en"?"Pause":"Pausar"));
        var transportPoint=transportPlay.PointToScreen(Point.Empty);
        Check(form.RectangleToScreen(form.ClientRectangle).Contains(transportPoint),"Replay transport stays in the visible window");
        Save("playback");
        Call("GoBackInLibrary");Pump();Check(!(bool)Field("replayOpen")!,"Back stops playback");
        Call("ShowPlayback",path,form);Call("Navigate","home",false);Pump(500);
        Check(!(bool)Field("replayOpen")!,"Leaving loading playback cancels it safely");
        Call("ShowSettings","display");Pump();
        Check(Application.OpenForms.Cast<Form>().Count(f=>f.TopLevel)==1,"Settings stays inside main window");
        var pages=Field("settingsNavigation")!;
        var panels=((IEnumerable)pages.GetType().GetProperty("Pages")!.GetValue(pages)!).Cast<Panel>().ToArray();
        Check(panels.Count(p=>p.Parent is not null)<=2,"Unvisited settings sections remain detached");
        var settingsButtons=All(form).OfType<Button>().Where(c=>c.Visible).ToArray();
        var switchControl=All(form).OfType<CheckBox>().First(c=>c.Visible);
        switchControl.Focus();
        Check(switchControl.Focused,"Switch accepts keyboard focus");
        var before=switchControl.Checked;switchControl.Checked=!before;Pump();Check(switchControl.Checked!=before,"Switch responds");
        Save("settings");
        var findButton=All(form).OfType<Button>().Single(c=>c.Visible && c.Text==(language=="en"?"Search settings":"Buscar en ajustes"));
        findButton.PerformClick();Pump(60);
        var settingsSearch=All(form).OfType<TextBox>().Single(c=>c.Visible && c.AccessibleName==(language=="en"?"Find a setting":"Buscar un ajuste"));
        Check(settingsSearch.Focused,"Settings search receives focus");
        settingsSearch.Text=language=="en"?"Activity language":"Idioma de la actividad";Pump(100);
        var result=All(form).OfType<ComboBox>().Single(c=>c.Visible && c.AccessibleName==(language=="en"?"Search results":"Resultados de búsqueda"));
        Check(result.Items.Count>0,"Settings search finds controls in deferred sections");
        typeof(ComboBox).GetMethod("OnSelectionChangeCommitted",Flags)!.Invoke(result,[EventArgs.Empty]);Pump(120);
        Check(All(form).OfType<ComboBox>().Any(c=>c.Visible && c.Focused && c.AccessibleName==(language=="en"?"Activity language":"Idioma de la actividad")),"Search opens deferred section and focuses its control");
        Call("ShowSettings","display");Pump();
        switchControl.Checked=before;
        Call("Navigate","home",false);Call("Navigate","settings",false);Pump();
        Check(ReferenceEquals(pages,Field("settingsNavigation")),"Settings root reused");
        form.ClientSize=new(600,560);Pump(450);Save("narrow-settings");
        var tripBar=(Control)Field("currentTripBar")!;
        var metadata=(Label)tripBar.GetType().GetField("Metadata",Flags)!.GetValue(tripBar)!;
        Check(metadata.Height>=metadata.Font.Height,"Trip bar fits enlarged text vertically");
        var searchAction=All(form).OfType<Button>().Single(c=>c.Visible && c.AccessibleName==(language=="en"?"Close search":"Cerrar búsqueda"));
        Check(searchAction.Right<=searchAction.Parent!.ClientSize.Width,"Settings search action fits the narrow header");
        Check(All(form).Any(c=>c.Visible && c is ScrollableControl {AutoScroll:true}),"Narrow settings retains scrolling");
        form.ClientSize=new(1280,900);Pump();
        All(form).OfType<Button>().Single(c=>c.Visible && c.Text==(language=="en"?"Cancel":"Cancelar")).PerformClick();Pump();
        Check((string?)Field("currentPage")=="home","Cancel returns to previous section");
        Call("ShowSettings","display");Pump();
        Check(!ReferenceEquals(pages,Field("settingsNavigation")),"Cancelled settings discard unapplied edits");
        All(form).OfType<Button>().Single(c=>c.Visible && c.Text==(language=="en"?"Save changes":"Guardar cambios")).PerformClick();Pump(300);
        Check((string?)Field("currentPage")=="home","Saving settings returns without opening a window");
        Call("ShowSettings","display");Pump();
        var draftSwitch=All(form).OfType<CheckBox>().First(c=>c.Visible);
        draftSwitch.Checked=!draftSwitch.Checked;
        Call("Navigate","journal",false);Pump(300);
        Check((string?)Field("currentPage")=="settings","Unsaved edits prevent accidental navigation");
        Save("settings-protection");
        All(form).OfType<Button>().Single(c=>c.Visible && c.Text==(language=="en"?"Keep editing":"Seguir editando")).PerformClick();Pump(60);
        Check((string?)Field("currentPage")=="settings","Keep editing stays in Settings");
        Call("Navigate","journal",false);Pump(300);
        All(form).OfType<Button>().Single(c=>c.Visible && c.Text==(language=="en"?"Save changes":"Guardar cambios")).PerformClick();Pump(300);
        Check((string?)Field("currentPage")=="journal","Save and continue honors the requested destination");
        Call("Navigate","home",false);
        Call("ShowSettings","display");Pump();
        draftSwitch=All(form).OfType<CheckBox>().First(c=>c.Visible);var originalDraft=draftSwitch.Checked;
        draftSwitch.Checked=!originalDraft;Pump(300);
        All(form).OfType<Button>().Single(c=>c.Visible && c.Text==(language=="en"?"Undo section edits":"Deshacer cambios del apartado")).PerformClick();Pump(300);
        Check(draftSwitch.Checked==originalDraft,"Undo section edits restores the original inputs");
        All(form).OfType<Button>().Single(c=>c.Visible && c.Text==(language=="en"?"Cancel":"Cancelar")).PerformClick();Pump(300);
        var host=(Control)Field("pageHost")!;var count=host.Controls.Count;
        var timings=new List<long>();GC.Collect();GC.WaitForPendingFinalizers();
        var handles=Process.GetCurrentProcess().HandleCount;var allocated=GC.GetTotalAllocatedBytes(true);
        var cycles=args.Length>4?Math.Clamp(int.Parse(args[4]),40,2000):40;
        for(var i=0;i<cycles;i++){watch.Restart();Call("Navigate","journal",false);Call("Navigate","replay",false);Call("Navigate","home",false);timings.Add(watch.ElapsedMilliseconds);Pump(5);}
        Pump(500);
        Check(host.Controls.Count==count,"120 section changes keep a bounded page count");
        Check(host.Controls.Cast<Control>().Count(c=>c.Visible)==1,"Exactly one page visible after rapid navigation");
        GC.Collect(); GC.WaitForPendingFinalizers(); Pump(100);
        Check(Process.GetCurrentProcess().HandleCount-handles<24,"Repeated navigation keeps handle growth bounded");
        Console.WriteLine($"{cycles*3} transitions allocated {(GC.GetTotalAllocatedBytes(true)-allocated)/1048576d:N1} MiB, handle delta {Process.GetCurrentProcess().HandleCount-handles}, three-section median {timings.Order().ElementAt(timings.Count/2)} ms");
        var queued=type.GetMethod("QueueNavigation",Flags)!;
        queued.Invoke(form,["journal"]);queued.Invoke(form,["replay"]);queued.Invoke(form,["trip"]);Pump(400);
        Check((string?)Field("currentPage")=="trip","Last navigation request wins");
        Call("Navigate","help",false); Pump();
        Check((string?)Field("currentPage")=="help","Help and diagnostics stays in the main window");
        Check(All(form).OfType<TextBox>().Any(c=>c.Visible && c.Text.Contains("Navigation:")),"Help exposes real runtime timings");
        var country=All(form).OfType<ComboBox>().Single(c=>c.Visible);
        country.SelectedIndex=country.Items.Count-1; Pump(40);
        Check(All(form).OfType<PictureBox>().Any(c=>c.Visible && c.Image is not null),"Country artwork preview uses bundled resources");
        Save("help");
        Call("ExportDiagnostics",form);Pump();
        Check(All(form).OfType<TextBox>().Any(c=>c.Visible && c.Text.Contains("No account names")),"Diagnostic report is reviewable before export");
        Call("GoBackInLibrary");Call("Navigate","home",false);
        var metrics=Field("uiPerformance")!;
        var metricCount=((IEnumerable)metrics.GetType().GetProperty("Samples",Flags)!.GetValue(metrics)!).Cast<object>().Count();
        Check(metricCount>0 && metricCount<=120,"Runtime performance history stays bounded");
        File.WriteAllText(Path.Combine(output,"timings-"+args[1]+"-"+language+"-"+percent+".txt"),(string)Call("BuildDiagnosticReport")!);
        form.Close();
        Console.WriteLine("UI batch complete");
    }
}
