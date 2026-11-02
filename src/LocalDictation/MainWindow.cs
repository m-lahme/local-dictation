using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Forms = System.Windows.Forms;

namespace LocalDictation
{
    public sealed class MainWindow : Window
    {
        readonly Settings settings; readonly string dataDir; readonly Action<string> changed, engineChanged; readonly Action<HistoryEntry,string> retranscribe;
        Grid root, content; StackPanel nav; TextBlock status; string page = "history", category = "model"; string statusText;
        string dictionaryQuery="", historyQuery="";
        Action renderMicrophones, renderRules, pendingRender; bool rowDragging; object focusedRow; List<MicrophoneInfo> microphoneScan = new List<MicrophoneInfo>(); System.Windows.Threading.DispatcherTimer ruleSave;
        public ComboBox FirstSelector; public bool Busy; public event Action RetryRequested; public event Action MicrophonesChanged; public string ActiveMicrophone = "";
        Brush R(string name) => (Brush)Resources[name];
        public MainWindow(Settings s, string dir, Action<string> onChange, Action<string> onEngine, Action<HistoryEntry,string> onRetranscribe=null) { settings = s; dataDir = dir; changed = onChange; engineChanged = onEngine; retranscribe=onRetranscribe; Title = "Local Dictation"; var area = SystemParameters.WorkArea; Width = Math.Min(1040, area.Width - 32); Height = Math.Min(740, area.Height - 32); MinWidth = Math.Min(800, Width); MinHeight = Math.Min(580, Height); WindowStartupLocation = WindowStartupLocation.CenterScreen; FontFamily = new FontFamily("Segoe UI"); FontSize = 14; Theme.Apply(this, s.Theme); SourceInitialized += (a, b) => WindowTheme.Apply(this, settings.Theme); Build(); ShowHistory(); }
        TextBlock T(string text, double size = 14, string color = "Text") => new TextBlock { Text = text, FontSize = size, Foreground = R(color), TextWrapping = TextWrapping.Wrap };
        Button B(string text, Action action) { var b = new Button { Content = text, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 7, 0) }; AutomationProperties.SetName(b, text); b.Click += (s, e) => { try { action(); } catch (Exception ex) { SetStatus(L.T("The action could not be completed.")); MessageBox.Show(this, ex.Message, "Local Dictation"); } }; return b; }
        void Save() { ruleSave?.Stop(); Storage.SaveSettings(settings); changed(settings.Theme); }
        // Typing in a rule saves after a short pause, when the field loses focus and on every page change; the settings object is always current.
        void QueueRuleSave() { if (ruleSave == null) { ruleSave = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; ruleSave.Tick += (s, e) => SaveRules(); } ruleSave.Stop(); ruleSave.Start(); }
        void SaveRules() { if (ruleSave == null || !ruleSave.IsEnabled) return; ruleSave.Stop(); try { Storage.SaveSettings(settings); } catch (Exception ex) { Storage.Log("dictionary save: " + ex.Message); SetStatus(L.T("The dictionary could not be saved. The change is kept and will be included in the next save.")); } }
        void Build()
        {
            root = new Grid { Background = R("Bg") }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(202) }); root.ColumnDefinitions.Add(new ColumnDefinition());
            var side = new DockPanel { Margin = new Thickness(18, 26, 18, 20) };
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 28) }; brand.Children.Add(Brand.Logo(28)); var name = T("Local Dictation", 15); name.Margin = new Thickness(10, 3, 0, 0); brand.Children.Add(name); DockPanel.SetDock(brand, Dock.Top); side.Children.Add(brand);
            var footer = new StackPanel(); status = T(statusText ?? L.T("Starting…"), 11, "Muted"); status.Margin = new Thickness(8, 12, 4, 12); footer.Children.Add(status); var quit = B(L.T("Quit"), () => Application.Current.Shutdown()); quit.HorizontalContentAlignment = HorizontalAlignment.Left;quit.BorderThickness=new Thickness(0);quit.Background=Brushes.Transparent;quit.Padding=new Thickness(12,8,8,8);var quitLabel=new StackPanel{Orientation=Orientation.Horizontal};quitLabel.Children.Add(new TextBlock{Text="\uE7E8",FontFamily=Theme.IconFont,FontSize=14,Foreground=R("Muted"),Margin=new Thickness(0,2,10,0)});quitLabel.Children.Add(T(L.T("Quit"),13,"Muted"));quit.Content=quitLabel;footer.Children.Add(quit); DockPanel.SetDock(footer, Dock.Bottom); side.Children.Add(footer);
            nav = new StackPanel(); side.Children.Add(nav); root.Children.Add(new Border { Background = R("Surface"), BorderBrush = R("Line"), BorderThickness = new Thickness(0, 0, 1, 0), Child = side });
            content = new Grid { Margin = new Thickness(24, 22, 24, 22) }; Grid.SetColumn(content, 1); root.Children.Add(content); Content = root; Navigation();
        }
        void Navigation() { nav.Children.Clear(); if (page == "settings") { Nav("‹  " + L.T("Back"), "back", ShowHistory); Nav(L.T("Models"), "model", () => SettingsPage("model")); Nav(L.T("Audio and hotkeys"), "audio", () => SettingsPage("audio")); Nav(L.T("Text output"), "output", () => SettingsPage("output")); Nav(L.T("Appearance"), "appearance", () => SettingsPage("appearance")); Nav(L.T("General"), "general", () => SettingsPage("general")); } else { Nav("◷  " + L.T("History"), "history", ShowHistory); Nav("Aa  " + L.T("Dictionary"), "dictionary", ShowDictionary); Nav("▥  " + L.T("Statistics"), "statistics", ShowStatistics); Nav("⚙  " + L.T("Settings"), "settings", ShowSettings); } }
        void Nav(string title, string key, Action action) { var b = B(title, action); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 3, 0, 3); b.Padding = new Thickness(12, 11, 8, 11); b.BorderThickness = new Thickness(0); b.Background = (key == page || page == "settings" && key == category) ? new SolidColorBrush(Color.FromArgb(40, 224, 91, 91)) : Brushes.Transparent; nav.Children.Add(b); }
        StackPanel Page(string title, string subtitle) { SaveRules(); FirstSelector = null; Navigation(); content.Children.Clear(); var p = new StackPanel(); p.Children.Add(T(title, 28)); var sub = T(subtitle, 13, "Muted"); sub.Margin = new Thickness(0, 8, 0, 24); p.Children.Add(sub); content.Children.Add(Scroll(p)); return p; }
        Border Card(UIElement child) => new Border { Child = child, Background = R("Surface"), BorderBrush = R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 12) };
        public void SetStatus(string value) { statusText = value; if (status != null) status.Text = value; }
        public void RefreshTheme() { Theme.Apply(this, settings.Theme); WindowTheme.Apply(this, settings.Theme); Build(); if (page == "dictionary") ShowDictionary(); else if(page=="statistics")ShowStatistics(); else if (page == "settings") SettingsPage(category); else ShowHistory(); }
        public void AddHistory(HistoryEntry entry) { var list = Storage.LoadHistory(dataDir); list.Insert(0, entry); Storage.Write(Path.Combine(dataDir, "history.json"), list); if (IsVisible && page == "history") ShowHistory(); }
        ScrollViewer Scroll(UIElement child) => Smooth(new ScrollViewer{Content=child,CanContentScroll=false});
        // The scrollbar has no arrow buttons; WPF derives the minimum thumb length from their height, which keeps the thumb grabbable in long lists.
        ScrollViewer Smooth(ScrollViewer scroll) { scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;scroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;scroll.Padding=new Thickness(0,0,5,0);scroll.Resources[SystemParameters.VerticalScrollBarWidthKey]=7.0;scroll.Resources[SystemParameters.VerticalScrollBarButtonHeightKey]=72.0;scroll.PreviewMouseWheel+=(s,e)=>{scroll.ScrollToVerticalOffset(scroll.VerticalOffset-e.Delta*.25);e.Handled=true;};return scroll; }
        StackPanel DensePage(string title,string subtitle,out StackPanel toolbar) {var rows=new StackPanel();DensePage(title,subtitle,out toolbar,Scroll(rows));return rows;}
        void DensePage(string title,string subtitle,out StackPanel toolbar,UIElement body) {
            SaveRules();Navigation();content.Children.Clear();var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());
            var heading=new StackPanel{Margin=new Thickness(0,0,0,12)};heading.Children.Add(T(title,24));var hint=T(subtitle,12,"Muted");hint.Margin=new Thickness(0,4,0,0);heading.Children.Add(hint);grid.Children.Add(heading);
            toolbar=new StackPanel{Margin=new Thickness(0,0,0,10)};Grid.SetRow(toolbar,1);grid.Children.Add(toolbar);
            Grid.SetRow(body,2);grid.Children.Add(body);content.Children.Add(grid);
        }
        TextBox Search(StackPanel parent,string hint,string initial) {
            var host=new Grid{Height=30,Width=320,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,5)};var box=new TextBox{Text=initial,Padding=new Thickness(28,5,8,4),FontSize=13};AutomationProperties.SetName(box,hint);box.ToolTip=hint;host.Children.Add(box);
            var icon=new TextBlock{Text="\uE721",FontFamily=Theme.IconFont,Foreground=R("Muted"),Margin=new Thickness(9,0,0,0),VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};host.Children.Add(icon);
            var placeholder=T(hint,13,"Muted");placeholder.Margin=new Thickness(29,0,0,0);placeholder.VerticalAlignment=VerticalAlignment.Center;placeholder.IsHitTestVisible=false;host.Children.Add(placeholder);void Update()=>placeholder.Visibility=box.Text.Length==0?Visibility.Visible:Visibility.Collapsed;box.TextChanged+=(s,e)=>Update();Update();parent.Children.Add(host);return box;
        }
        Button IconButton(string glyph,string label,Action action) {var button=B(glyph,action);button.FontFamily=Theme.IconFont;button.FontSize=13;button.Width=26;button.Height=26;button.Padding=new Thickness(0);button.Margin=new Thickness(1,0,0,0);button.BorderThickness=new Thickness(0);button.Background=Brushes.Transparent;button.ToolTip=label;AutomationProperties.SetName(button,label);return button;}
        public void ShowHistory()
        {
            page="history";var rows=new VirtualRows<HistoryEntry>(HistoryRow);Smooth(rows.Scroll);var empty=T("",13,"Muted");var body=new Grid();body.Children.Add(rows);body.Children.Add(empty);DensePage(L.T("History"),L.T("Dictations from the last 30 days · Recordings stay available for one hour"),out var toolbar,body);var search=Search(toolbar,L.T("Search history…"),historyQuery);var entries=Storage.LoadHistory(dataDir);
            Action render=()=>
            {
                historyQuery=search.Text;var shown=entries.Where(x=>x.Text.Contains(search.Text,StringComparison.OrdinalIgnoreCase)||x.Variants.Any(v=>v.Text.Contains(search.Text,StringComparison.OrdinalIgnoreCase))).ToList();rows.ItemsSource=shown;rows.Scroll.ScrollToTop();
                empty.Text=entries.Count==0?L.T("No dictations yet. Start one with your hotkey."):L.T("No matching dictations.");empty.Visibility=shown.Count==0?Visibility.Visible:Visibility.Collapsed;
            };search.TextChanged+=(s,e)=>render();render();
        }
        FrameworkElement HistoryRow(HistoryEntry h)
        {
            bool problem=h.Status!="complete";var stack=new StackPanel();var head=new DockPanel{LastChildFill=true};var body=new TextBox{Text=h.Text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Foreground=R("Text"),FontSize=13,Padding=new Thickness(0),Margin=new Thickness(0,2,0,1)};
            var copy=IconButton("\uE8C8",L.T("Copy dictation"),()=>Clipboard.SetText(body.Text));DockPanel.SetDock(copy,Dock.Right);head.Children.Add(copy);
            bool audioAvailable=!String.IsNullOrWhiteSpace(h.AudioFile)&&h.AudioExpiresAtUtc>DateTime.UtcNow;
            var variants=h.Variants.Count>0?h.Variants:new List<TranscriptVariant>{new TranscriptVariant{Engine=h.Engine,RawText=h.RawText,Text=h.Text,CreatedAtUtc=h.Time.ToUniversalTime(),Confidence=h.Confidence,ReviewRecommended=h.ReviewRecommended}};
            if(audioAvailable&&retranscribe!=null){Button retryButton=null;retryButton=IconButton("\uE72C",L.T("Transcribe again"),()=>{var commands=variants.Select((v,i)=>new ApplicationMenus.Command{Title=i==variants.Count-1?L.F("{0} · current version",SidebarStatus.ModelName(v.Engine)):L.F("{0} · version {1}",SidebarStatus.ModelName(v.Engine),i+1),Glyph=i==variants.Count-1?"\uE73E":"\uE81C",Action=()=>body.Text=v.Text}).ToList();var retryMenu=ApplicationMenus.CreateRetry(settings,commands,engine=>retranscribe?.Invoke(h,engine));retryMenu.PlacementTarget=retryButton;retryMenu.IsOpen=true;});DockPanel.SetDock(retryButton,Dock.Right);head.Children.Add(retryButton);}
            var meta=T(h.Time.ToString(L.T("MMM d · HH:mm"),L.Culture)+"  ·  "+(h.DurationMs/1000.0).ToString("0.0",L.Culture)+" s  ·  "+SidebarStatus.ModelName(h.Engine)+(audioAvailable?"  ·  "+L.F("Audio expires in {0} min",Math.Max(1,(int)Math.Ceiling((h.AudioExpiresAtUtc.Value-DateTime.UtcNow).TotalMinutes))):""),11,"Muted");meta.VerticalAlignment=VerticalAlignment.Center;head.Children.Add(meta);stack.Children.Add(head);stack.Children.Add(body);
            if(h.ReviewRecommended){var note=T(L.T("Please review"),11,"Muted");note.FontWeight=FontWeights.SemiBold;stack.Children.Add(new Border{Child=note,Background=new SolidColorBrush(Color.FromArgb(32,245,174,66)),CornerRadius=new CornerRadius(5),Padding=new Thickness(7,4,7,4),Margin=new Thickness(0,4,0,0),HorizontalAlignment=HorizontalAlignment.Left});}
            if(problem){var note=T(h.Status=="not-transcribed"?L.T("Not transcribed · use ↻ to transcribe again while the recording is available"):h.Status=="incomplete"?L.T("Recording interrupted · text was not inserted"):L.T("Text was not inserted"),11,"Accent");note.FontWeight=FontWeights.SemiBold;stack.Children.Add(new Border{Child=note,Background=new SolidColorBrush(Color.FromArgb(24,224,91,91)),CornerRadius=new CornerRadius(5),Padding=new Thickness(7,4,7,4),Margin=new Thickness(0,4,0,0),HorizontalAlignment=HorizontalAlignment.Left});}
            return new Border{Child=stack,Background=R("Surface"),BorderBrush=R("Line"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(10,5,8,7),Margin=new Thickness(0,0,0,6)};
        }
        public void ShowDictionary() {
            page="dictionary";var list=DensePage(L.T("Dictionary"),L.T("Replacements are applied from top to bottom."),out var toolbar);var search=Search(toolbar,L.T("Search dictionary…"),dictionaryQuery);var actions=new DockPanel();var count=T(settings.Rules.Count==1?L.T("1 rule"):L.F("{0} rules",settings.Rules.Count),11,"Muted");count.VerticalAlignment=VerticalAlignment.Center;actions.Children.Add(count);var add=B(L.T("+ New rule"),()=>{if(settings.Rules.Count>=1000)throw new InvalidOperationException(L.T("At most 1,000 rules are allowed."));settings.Rules.Add(new ReplacementRule());dictionaryQuery="";Save();ShowDictionary();});add.Padding=new Thickness(8,4,8,4);add.Margin=new Thickness(10,0,0,0);add.HorizontalAlignment=HorizontalAlignment.Right;actions.Children.Add(add);toolbar.Children.Add(actions);
            renderRules=()=>{dictionaryQuery=search.Text;list.Children.Clear();for(int i=0;i<settings.Rules.Count;i++){var r=settings.Rules[i];if(r.Source.Contains(dictionaryQuery,StringComparison.OrdinalIgnoreCase)||r.Target.Contains(dictionaryQuery,StringComparison.OrdinalIgnoreCase))Rule(list,i);}if(list.Children.Count==0)list.Children.Add(T(L.T("No matching rules."),13,"Muted"));};search.TextChanged+=(s,e)=>renderRules();renderRules();
        }
        void Rule(StackPanel p,int index) {
            var r=settings.Rules[index];var row=new Grid{Height=30};
            foreach(double width in new[]{26.0,-1,20,-1,53,39,26})row.ColumnDefinitions.Add(new ColumnDefinition{Width=width<0?new GridLength(1,GridUnitType.Star):new GridLength(width)});
            void Put(UIElement child,int column){Grid.SetColumn(child,column);row.Children.Add(child);}
            var handle=Grip(L.F("Move rule {0}",index+1),6);
            Put(handle,0);var arrow=T("→",12,"Muted");arrow.HorizontalAlignment=HorizontalAlignment.Center;arrow.VerticalAlignment=VerticalAlignment.Center;Put(arrow,2);
            for(int n=0;n<2;n++){bool source=n==0;var box=new TextBox{Text=source?r.Source:r.Target,Height=28,VerticalContentAlignment=VerticalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Padding=new Thickness(6,4,6,3),FontSize=13,ToolTip=source?L.T("Replace"):L.T("With")};AutomationProperties.SetName(box,source?L.F("Replace {0}",index+1):L.F("With {0}",index+1));box.TextChanged+=(s,e)=>{if(source)r.Source=box.Text;else r.Target=box.Text;QueueRuleSave();};box.LostFocus+=(s,e)=>SaveRules();Put(box,n==0?1:3);}
            var mode=B(r.Whole?L.T("Word"):L.T("Part"),()=>{});mode.Width=48;mode.Height=26;mode.Padding=new Thickness(2);mode.Margin=new Thickness(4,0,0,0);mode.FontSize=11;mode.ToolTip=L.T("Whole word or part of a word");mode.Click+=(s,e)=>{r.Whole=!r.Whole;mode.Content=r.Whole?L.T("Word"):L.T("Part");Save();};Put(mode,4);
            var exact=B("Aa",()=>{});exact.Width=31;exact.Height=26;exact.Margin=new Thickness(4,0,0,0);exact.Padding=new Thickness(0);exact.FontSize=12;exact.ToolTip=L.T("Match case");AutomationProperties.SetName(exact,L.F("Match case {0}",index+1));void CaseStyle(){exact.Background=r.CaseSensitive?new SolidColorBrush(Color.FromArgb(55,224,91,91)):Brushes.Transparent;exact.BorderThickness=r.CaseSensitive?new Thickness(1):new Thickness(0);}CaseStyle();exact.Click+=(s,e)=>{r.CaseSensitive=!r.CaseSensitive;CaseStyle();Save();};Put(exact,5);
            Put(IconButton("\uE74D",L.F("Delete rule {0}",index+1),()=>{settings.Rules.Remove(r);Save();ShowDictionary();}),6);
            var card=new Border{Child=row,Background=Brushes.Transparent,CornerRadius=new CornerRadius(6),BorderThickness=new Thickness(1),BorderBrush=Brushes.Transparent};var host=new Border{Child=card,Margin=new Thickness(0,0,0,2)};
            Reorderable(p,host,card,handle,r,settings.Rules,Brushes.Transparent,()=>{Storage.SaveSettings(settings);renderRules?.Invoke();});
            p.Children.Add(host);
        }
        public void ShowCorrection(string original,string corrected,CorrectionAnalysis analysis)
        {
            page="correction";var p=Page(L.T("Learn correction"),L.T("Review the suggestions. Rules are added to the dictionary only when you click the button."));
            var comparison=new Grid();comparison.ColumnDefinitions.Add(new ColumnDefinition());comparison.ColumnDefinitions.Add(new ColumnDefinition());
            TextBox Box(string title,string value,int column){var s=new StackPanel{Margin=new Thickness(column==0?0:6,0,column==0?6:0,0)};s.Children.Add(T(title,12,"Muted"));var b=new TextBox{Text=value,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MinHeight=72,Margin=new Thickness(0,5,0,0),Padding=new Thickness(8)};s.Children.Add(b);Grid.SetColumn(s,column);comparison.Children.Add(s);return b;}
            Box(L.T("Inserted"),original,0);Box(L.T("Corrected"),corrected,1);p.Children.Add(Card(comparison));
            var rows=new StackPanel();var controls=new List<(RuleSuggestion suggestion,CheckBox selected,TextBox source,TextBox target,Button mode)>();
            foreach(var suggestion in analysis.Suggestions){var row=new Grid{Margin=new Thickness(0,0,0,7)};foreach(double width in new[]{28.0,-1,24,-1,58,40})row.ColumnDefinitions.Add(new ColumnDefinition{Width=width<0?new GridLength(1,GridUnitType.Star):new GridLength(width)});var check=new CheckBox{IsChecked=suggestion.Selected,VerticalAlignment=VerticalAlignment.Center,ToolTip=suggestion.Reason};var source=new TextBox{Text=suggestion.Source,Padding=new Thickness(6,4,6,4)};var arrow=T("→",12,"Muted");arrow.HorizontalAlignment=HorizontalAlignment.Center;arrow.VerticalAlignment=VerticalAlignment.Center;var target=new TextBox{Text=suggestion.Target,Padding=new Thickness(6,4,6,4)};var mode=B(suggestion.Whole?L.T("Word"):L.T("Part"),()=>{});mode.Padding=new Thickness(4);mode.Margin=new Thickness(5,0,0,0);mode.Click+=(s,e)=>{suggestion.Whole=!suggestion.Whole;mode.Content=suggestion.Whole?L.T("Word"):L.T("Part");};var exact=B("Aa",()=>{});exact.Width=32;exact.Padding=new Thickness(0);exact.Margin=new Thickness(5,0,0,0);exact.ToolTip=L.T("Match capitalization");void ExactStyle(){exact.Background=suggestion.CaseSensitive?new SolidColorBrush(Color.FromArgb(55,224,91,91)):Brushes.Transparent;exact.BorderThickness=suggestion.CaseSensitive?new Thickness(1):new Thickness(0);}ExactStyle();exact.Click+=(s,e)=>{suggestion.CaseSensitive=!suggestion.CaseSensitive;ExactStyle();};UIElement[] cells={check,source,arrow,target,mode,exact};for(int i=0;i<cells.Length;i++){Grid.SetColumn(cells[i],i);row.Children.Add(cells[i]);}rows.Children.Add(row);controls.Add((suggestion,check,source,target,mode));}
            p.Children.Add(Card(rows));var buttons=new WrapPanel();buttons.Children.Add(B(L.T("Add selected rules"),()=>{foreach(var item in controls.Where(x=>x.selected.IsChecked==true)){item.suggestion.Source=item.source.Text;item.suggestion.Target=item.target.Text;if(String.IsNullOrWhiteSpace(item.suggestion.Source))continue;var existing=settings.Rules.FirstOrDefault(x=>x.Whole==item.suggestion.Whole&&x.CaseSensitive==item.suggestion.CaseSensitive&&String.Equals(x.Source,item.suggestion.Source,StringComparison.OrdinalIgnoreCase));if(existing!=null)existing.Target=item.suggestion.Target;else if(settings.Rules.Count<1000)settings.Rules.Add(item.suggestion.ToRule());}Save();ShowDictionary();}));buttons.Children.Add(B(L.T("Cancel"),ShowHistory));p.Children.Add(buttons);
        }
        public void ShowStatistics()
        {
            page="statistics";var p=Page(L.T("Statistics"),L.T("Your local usage in plain numbers. The time saved is an estimate."));var ledger=UsageLedgerStore.Load(dataDir);var periods=new[]{(L.T("Today"),1),(L.T("7 days"),7),(L.T("30 days"),30),(L.T("All time"),0)};var selector=new ComboBox{ItemsSource=periods.Select(x=>x.Item1),SelectedIndex=2,Width=180,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,12)};p.Children.Add(selector);var cards=new WrapPanel();p.Children.Add(cards);
            void Render(){cards.Children.Clear();var totals=UsageLedgerStore.ForPeriod(ledger,periods[selector.SelectedIndex].Item2,DateTime.Now);void Metric(string value,string label){var s=new StackPanel{Width=175};s.Children.Add(T(value,24));var l=T(label,12,"Muted");l.Margin=new Thickness(0,5,0,0);s.Children.Add(l);var card=Card(s);card.Margin=new Thickness(0,0,10,10);cards.Children.Add(card);}Metric(totals.Words.ToString("N0",L.Culture),L.T("Words transcribed"));Metric(L.F("{0:0.0} min",TimeSpan.FromMilliseconds(totals.DictationMs).TotalMinutes),L.T("Dictation time"));var saved=UsageStatistics.EstimateTimeSaved(totals,settings.TypingWpm);Metric(saved.TotalMinutes<60?L.F("{0:0} min",saved.TotalMinutes):L.F("{0:0.0} h",saved.TotalHours),L.T("estimated time saved"));Metric(totals.Dictations.ToString("N0",L.Culture),L.T("Dictations"));Metric(totals.Dictations==0?"0":((double)totals.Words/totals.Dictations).ToString("0",L.Culture),L.T("Words per dictation"));Metric(UsageStatistics.DictationWordsPerMinute(totals).ToString("0",L.Culture),L.T("Words per dictation minute"));}selector.SelectionChanged+=(s,e)=>Render();Render();
            var speed=new StackPanel();speed.Children.Add(T(L.T("Your typing speed"),16));var line=new WrapPanel{Margin=new Thickness(0,9,0,0)};line.Children.Add(T(L.T("For the estimate:"),13,"Muted"));var wpm=new TextBox{Text=settings.TypingWpm.ToString(),Width=58,Margin=new Thickness(8,0,6,0),Padding=new Thickness(5,3,5,3)};line.Children.Add(wpm);line.Children.Add(T(L.T("words per minute"),13,"Muted"));speed.Children.Add(line);wpm.LostFocus+=(s,e)=>{if(int.TryParse(wpm.Text,out int value)){settings.TypingWpm=Math.Clamp(value,10,200);Save();ShowStatistics();}else wpm.Text=settings.TypingWpm.ToString();};
            var prompt=L.T("Fast dictation saves time when thoughts are turned into text directly and clearly.");var hint=T(L.T("Type this sentence. Timing starts with the first character:"),12,"Muted");hint.Margin=new Thickness(0,14,0,5);speed.Children.Add(hint);speed.Children.Add(T(prompt,13));var test=new TextBox{Margin=new Thickness(0,8,0,0),Padding=new Thickness(8),MinHeight=36};speed.Children.Add(test);var result=T("",12,"Muted");result.Margin=new Thickness(0,6,0,0);speed.Children.Add(result);var watch=new Stopwatch();test.PreviewTextInput+=(s,e)=>{if(!watch.IsRunning)watch.Start();};DataObject.AddPastingHandler(test,(s,e)=>e.CancelCommand());test.TextChanged+=(s,e)=>{if(test.Text==prompt&&watch.IsRunning){watch.Stop();settings.TypingWpm=Math.Clamp((int)Math.Round(UsageStatistics.CalculateTypingWordsPerMinute(prompt.Length,watch.Elapsed)),10,200);Save();result.Text=L.F("Measured: {0} words per minute",settings.TypingWpm);}};p.Children.Add(Card(speed));
        }
        public void ShowSettings() { ShowSettings(category); }
        public void ShowSettings(string key) { page = "settings"; SettingsPage(key); }
        void SettingsPage(string key)
        {
            category = key; page = "settings"; var p = Page(key == "model" ? L.T("Models") : key == "audio" ? L.T("Audio and hotkeys") : key == "output" ? L.T("Text output") : key == "appearance" ? L.T("Appearance") : L.T("General"), L.T("Settings are saved automatically."));
            if (key == "model")
            {
                Select(p, L.T("Automatic model switching"), L.T("ASUS Silent, PD Silent and Windows Power saver use Parakeet on the CPU. ASUS Performance, Turbo, PD Performance, PD Turbo and Windows High performance use Whisper on the GPU. Balanced and unknown profiles do not change the model."), new[] { L.T("Off"), L.T("By power mode") }, settings.AutomaticPowerModel ? 1 : 0, i => { settings.AutomaticPowerModel = i == 1; Save(); SettingsPage(key); });
                Select(p, L.T("Active model"), L.T("Whisper 3 Turbo uses the dedicated GPU. Parakeet runs on the CPU. Both run entirely on this device."), new[] { "Whisper 3 Turbo · GPU", "Parakeet TDT 0.6B v3 · CPU" }, settings.Engine == "parakeet" ? 1 : 0, i => { if (Busy) { SetStatus(L.T("Finish the current dictation first.")); SettingsPage(key); return; } settings.Engine = i == 1 ? "parakeet" : "whisper"; settings.Device = i == 1 ? "cpu" : "gpu"; Save(); engineChanged(settings.Engine); }, !settings.AutomaticPowerModel);
                var idleValues = new[] { 0, 3, 5, 10 }; int idleIndex = Array.IndexOf(idleValues, settings.WhisperIdleMinutes);
                Select(p, L.T("Whisper when idle"), L.T("Frees graphics memory after the selected time without dictation. The next recording starts immediately; if Whisper is still loading, transcription waits briefly after the recording."), new[] { L.T("Keep loaded"), L.F("Unload after {0} minutes", 3), L.F("Unload after {0} minutes", 5), L.F("Unload after {0} minutes", 10) }, Math.Max(0, idleIndex), i => { settings.WhisperIdleMinutes = idleValues[i]; Save(); });
                Info(p, L.T("Where models run"), L.T("Whisper runs only on the dedicated GPU. If Vulkan or the GPU fails, Parakeet takes over on the CPU; a CPU fallback for Whisper is disabled."));
                foreach (string name in new[] { "whisper", "parakeet" }) { string engine = name; var s = new StackPanel(); s.Children.Add(T(engine == "whisper" ? "Whisper 3 Turbo · 548 MiB" : "Parakeet v3 · 640 MiB", 16)); var detail = T(Models.Installed(engine) ? L.T("Installed") : L.T("Not installed yet"), 12, "Muted"); detail.Margin = new Thickness(0, 8, 0, 12); s.Children.Add(detail); var buttons = new WrapPanel(); var download = B(L.T("Download / verify"), () => { }); download.Click += async (a, b) => { download.IsEnabled = false; try { await Models.Ensure(engine, x => Dispatcher.Invoke(() => detail.Text = x), default); await Models.Verify(engine); detail.Text = L.T("Installed and SHA-256 verified"); } catch (Exception ex) { detail.Text = ex.Message; } finally { download.IsEnabled = true; } }; buttons.Children.Add(download); var remove = B(L.T("Remove model"), () => { if (Busy || engine == settings.Engine) throw new InvalidOperationException(L.T("Select another model first.")); Models.Remove(engine); SettingsPage(key); }); remove.IsEnabled = engine != settings.Engine; buttons.Children.Add(remove); s.Children.Add(buttons); p.Children.Add(Card(s)); }
                p.Children.Add(B(L.T("Restart model"), () => RetryRequested?.Invoke()));
            }
            else if (key == "audio")
            {
                Microphones(p);
                Select(p, L.T("Dictation hotkey"), L.T("A short press starts or stops a dictation. Holding past the threshold and then releasing ends push-to-talk. Escape cancels recording and transcription."), new[] { L.T("Ctrl + Windows"), L.T("Ctrl + Shift + Space") }, settings.Hotkey == "ctrl-win" ? 0 : 1, i => { settings.Hotkey = i == 0 ? "ctrl-win" : "ctrl-shift-space"; Save(); });
                Info(p,L.T("Learn corrections"),L.T("Correct the most recently inserted text, select all of it and tap the recording hotkey twice in quick succession. The app then suggests editable dictionary rules."));
                Select(p, L.T("Hold threshold"), L.T("Recording starts when the key is pressed."), new[] { "300 ms", "400 ms", "600 ms" }, settings.HoldMs == 300 ? 0 : settings.HoldMs == 600 ? 2 : 1, i => { settings.HoldMs = new[] { 300, 400, 600 }[i]; Save(); });
                Info(p, L.T("Test microphone"), L.T("Start a short dictation with the hotkey. The five red bars show the recording level. Escape discards the test without a history entry."));
            }
            else if (key == "output")
            {
                Select(p, L.T("After dictation"), L.T("“Insert only” uses Ctrl+V for formats that can be copied safely and restores the clipboard afterwards. Temporary content is excluded from Windows clipboard history. For unknown formats, individual characters are sent. Newly copied content takes priority."), new[] { L.T("Insert only"), L.T("Clipboard only"), L.T("Insert and clipboard") }, (int)settings.Output, i => { settings.Output = (OutputMode)i; Save(); });
                Select(p,L.T("Voice commands"),L.T("At the start of a sentence or dictation, “bullet point”, “new line” and “new paragraph” are turned into formatting. Saying “literally bullet point” keeps the words."),new[]{L.T("On"),L.T("Off")},settings.VoiceCommands?0:1,i=>{settings.VoiceCommands=i==0;Save();});
            }
            else if (key == "appearance")
            {
                Select(p, L.T("Language"), L.T("“System” follows the Windows display language. Other languages use English."), new[] { "System (" + (L.SystemLanguage == "de" ? "Deutsch" : "English") + ")", "Deutsch", "English" }, settings.Language == "de" ? 1 : settings.Language == "en" ? 2 : 0, i => { settings.Language = i == 1 ? "de" : i == 2 ? "en" : "system"; L.Language = L.Resolve(settings.Language); Save(); RefreshTheme(); });
                Select(p, L.T("Theme"), L.T("“System” follows the Windows color mode."), new[] { "System", L.T("Light"), L.T("Dark") }, settings.Theme == "light" ? 1 : settings.Theme == "dark" ? 2 : 0, i => { settings.Theme = i == 1 ? "light" : i == 2 ? "dark" : "system"; Save(); RefreshTheme(); });
                Select(p, L.T("Overlay position"), L.T("Dragging moves the overlay for this session."), new[] { L.T("Bottom center"), L.T("Bottom left"), L.T("Bottom right"), L.T("Top center"), L.T("Top left"), L.T("Top right") }, Array.IndexOf(Anchors, settings.Anchor), i => { settings.Anchor = Anchors[i]; Save(); });
                var screens = Forms.Screen.AllScreens; Select(p, L.T("Display"), L.T("If the display is missing, the app temporarily uses the main display."), new[] { L.T("Main display") }.Concat(screens.Select(x => x.DeviceName)).ToArray(), Math.Max(0, Array.FindIndex(screens, x => x.DeviceName == settings.Monitor) + 1), i => { settings.Monitor = i == 0 ? "" : screens[i - 1].DeviceName; Save(); });
                Select(p, L.T("Overlay when idle"), L.T("It always appears during a dictation."), new[] { L.T("Show"), L.T("Hide") }, settings.OverlayVisible ? 0 : 1, i => { settings.OverlayVisible = i == 0; Save(); });
            }
            else
            {
                string other = Autostart.Other; Select(p, L.T("Start with Windows"), L.T("Autostart only opens the overlay and tray icon. You can also turn it off in Windows.") + (other == null ? "" : " " + (File.Exists(other) ? L.F("Windows currently starts another copy: {0}. “On” starts this copy instead.", other) : L.F("Autostart points to a copy that no longer exists: {0}. “On” starts this copy instead.", other))), new[] { L.T("Off"), L.T("On") }, Autostart.Enabled ? 1 : 0, i => { Autostart.Set(i == 1); SettingsPage(key); });
                Info(p, "Local Dictation " + AppVersion, L.T("Local dictation app for Windows. Speech is transcribed entirely on this device. Closing the window keeps the app running in the background. Open source under the MIT License · github.com/m-lahme/local-dictation"));
                Info(p, L.T("Data and updates"), L.T("Settings and history are stored in LocalAppData\\LocalDictation. Model packages are kept when the app is updated."));
                var licenses = new StackPanel(); licenses.Children.Add(T(L.T("Models and licenses"), 15)); var notice = T(L.T("Local Dictation: MIT. Whisper: OpenAI, MIT. Parakeet TDT 0.6B v3: NVIDIA, CC BY 4.0; ONNX conversion by sherpa-onnx. Runtimes: whisper.cpp and Whisper.net, MIT; sherpa-onnx, Apache 2.0; ONNX Runtime, MIT; NAudio, MIT; .NET, MIT. Sources, model links and license texts are in the program folder in THIRD-PARTY-NOTICES.md and in the licenses folder."), 12, "Muted"); notice.Margin = new Thickness(0, 7, 0, 12); licenses.Children.Add(notice); var open = new WrapPanel(); open.Children.Add(B(L.T("Open license notices"), OpenLicenses)); licenses.Children.Add(open); p.Children.Add(Card(licenses));
            }
        }
        static readonly string[] Anchors = { "bottom-center", "bottom-left", "bottom-right", "top-center", "top-left", "top-right" };
        public static string AppVersion => VersionLabel(typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? typeof(MainWindow).Assembly.GetName().Version?.ToString(3));
        // The SDK appends "+<commit>" to the informational version; the UI shows only the release number.
        public static string VersionLabel(string version) { version ??= ""; int plus = version.IndexOf('+'); return (plus < 0 ? version : version.Substring(0, plus)).Trim(); }
        void OpenLicenses()
        {
            string notices = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md"), folder = Path.Combine(AppContext.BaseDirectory, "licenses");
            if (File.Exists(notices)) Process.Start("explorer.exe", "/select,\"" + notices + "\"")?.Dispose();
            else if (Directory.Exists(folder)) Process.Start("explorer.exe", "\"" + folder + "\"")?.Dispose();
            else throw new FileNotFoundException(L.T("The license notices are missing from this program folder. They are in the source repository in THIRD-PARTY-NOTICES.md and in the licenses folder."));
        }
        public void RefreshModelSettings() { if (page == "settings" && category == "model") SettingsPage("model"); }
        public void RefreshMicrophones(List<MicrophoneInfo> found = null) { if (found != null) microphoneScan = found; if (page == "settings" && category == "audio") renderMicrophones?.Invoke(); }
        void Microphones(StackPanel p)
        {
            var s = new StackPanel(); s.Children.Add(T(L.T("Microphones"), 15)); var d = T(L.T("The topmost connected microphone is used. Drag to change the order. When a microphone is connected or disconnected, the app switches in the background, never during a dictation. The recording stream and the Windows microphone indicator are only active while dictating."), 12, "Muted"); d.Margin = new Thickness(0, 6, 0, 12); s.Children.Add(d);
            var rows = new StackPanel(); s.Children.Add(rows); p.Children.Add(Card(s));
            renderMicrophones = () =>
            {
                if (rowDragging) { pendingRender = renderMicrophones; return; }
                rows.Children.Clear(); for (int i = 0; i < settings.Microphones.Count; i++) MicrophoneRow(rows, settings.Microphones[i], i);
                if (rows.Children.Count == 0) rows.Children.Add(T(L.T("No microphone detected yet."), 13, "Muted"));
            };
            try { microphoneScan = MicrophoneCatalog.Scan(); } catch (Exception ex) { Storage.Log("microphone list: " + ex.Message); }
            renderMicrophones();
        }
        void MicrophoneRow(StackPanel p, MicrophoneEntry m, int index)
        {
            var live = microphoneScan.Where(x => m.Ids.Contains(x.Id)).ToList(); bool connected = live.Any(x => x.Active), used = ActiveMicrophone.Length > 0 && m.Ids.Contains(ActiveMicrophone), standard = live.Any(x => x.Active && x.IsDefault);
            string state = used ? L.T("In use") : connected ? L.T("Ready") : live.Any(x => x.State == NAudio.CoreAudioApi.DeviceState.Unplugged) ? L.T("Unplugged") : live.Any(x => x.State == NAudio.CoreAudioApi.DeviceState.Disabled) ? L.T("Disabled in Windows") : L.T("Not connected");
            string title = MicrophoneCatalog.Title(m); var row = new Grid { MinHeight = 48 };
            foreach (double width in new[] { 26.0, 36, -1, 0, 30 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = width < 0 ? new GridLength(1, GridUnitType.Star) : width == 0 ? GridLength.Auto : new GridLength(width) });
            void Put(UIElement child, int column) { Grid.SetColumn(child, column); row.Children.Add(child); }
            var handle = Grip(L.F("Move {0}, priority {1}", title, index + 1), 15);
            Put(handle, 0);
            var icon = new TextBlock { Text = MicrophoneCatalog.Glyph(m.Kind), FontFamily = Theme.IconFont, FontSize = 20, Foreground = used ? R("Accent") : R("Text"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Opacity = connected ? 1 : .5 }; Put(icon, 1);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0), Opacity = connected ? 1 : .6 }; var name = T(title, 14); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; text.Children.Add(name);
            var details = T(MicrophoneCatalog.Details(m) + (standard ? " · " + L.T("Windows default") : ""), 12, "Muted"); details.Margin = new Thickness(0, 2, 0, 0); text.Children.Add(details); Put(text, 2);
            var label = T(state, 11, used ? "Text" : "Muted"); if (used) label.FontWeight = FontWeights.SemiBold;
            Put(new Border { Child = label, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 3), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), BorderBrush = used ? R("Accent") : R("Line"), Background = used ? new SolidColorBrush(Color.FromArgb(40, 224, 91, 91)) : Brushes.Transparent }, 3);
            if (!connected) Put(IconButton("", L.F("Remove {0} from the list", title), () => { settings.Microphones.Remove(m); Storage.SaveSettings(settings); renderMicrophones?.Invoke(); }), 4);
            row.ToolTip = m.Name;
            Brush rest = used ? R("SurfaceRaised") : Brushes.Transparent; var card = new Border { Child = row, Background = rest, CornerRadius = new CornerRadius(8), Padding = new Thickness(0, 2, 2, 2), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent };
            var host = new Border { Child = card, Margin = new Thickness(0, 0, 0, 3) };
            Reorderable(p, host, card, handle, m, settings.Microphones, rest, MicrophonesReordered);
            p.Children.Add(host);
        }
        TextBlock Grip(string name, double top) { var grip = new TextBlock { Text = "", FontFamily = Theme.IconFont, FontSize = 16, Foreground = R("Muted"), VerticalAlignment = VerticalAlignment.Stretch, TextAlignment = TextAlignment.Center, Padding = new Thickness(0, top, 0, 0), Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeNS, ToolTip = L.T("Drag · Alt+Arrow to move"), Focusable = true }; AutomationProperties.SetName(grip, name); return grip; }
        // Pointer-driven reordering: the grabbed row follows the cursor while the others slide aside to show where it will land.
        void Reorderable<T>(StackPanel rows, Border host, Border card, TextBlock handle, T item, List<T> list, Brush rest, Action reordered) where T : class
        {
            List<FrameworkElement> hosts = null; List<double> sizes = null; int from = -1, to = -1; double start = 0, grab = 0, top = 0, height = 0, total = 0; bool pressed = false; ScrollViewer scroll = null;
            host.Tag = item; host.RenderTransform = new TranslateTransform();
            var edge = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            List<FrameworkElement> Rows() => rows.Children.OfType<FrameworkElement>().Where(x => x.Tag is T).ToList();
            bool Place(List<FrameworkElement> order, int source, int target) { if (!RuleOrder.Move(list, item, (T)order[target].Tag, target > source)) return false; focusedRow = item; reordered(); return true; }
            double Size(FrameworkElement x) => x.ActualHeight + x.Margin.Top + x.Margin.Bottom;
            void Slide(FrameworkElement x, double y, Action done = null) { var animation = new DoubleAnimation(y, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; if (done != null) animation.Completed += (s, e) => done(); x.RenderTransform.BeginAnimation(TranslateTransform.YProperty, animation); }
            void Track()
            {
                double offset = Math.Clamp(System.Windows.Input.Mouse.GetPosition(rows).Y - grab - top, -top, total - height - top); ((TranslateTransform)host.RenderTransform).Y = offset;
                int target = RuleOrder.Slot(sizes, from, offset); if (target == to) return; to = target;
                for (int i = 0; i < hosts.Count; i++) if (i != from) Slide(hosts[i], i > from && i <= to ? -height : i < from && i >= to ? height : 0);
            }
            void Begin()
            {
                rowDragging = true; hosts = Rows(); sizes = hosts.Select(Size).ToList(); from = to = hosts.IndexOf(host); top = sizes.Take(from).Sum(); height = sizes[from]; total = sizes.Sum(); grab = start - top;
                foreach (var x in hosts) { x.RenderTransform.BeginAnimation(TranslateTransform.YProperty, null); if (x.RenderTransform is TranslateTransform t) t.Y = 0; }
                Panel.SetZIndex(host, 1); card.Background = R("SurfaceRaised"); card.BorderBrush = R("Accent"); host.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = .3 };
                scroll = null; for (DependencyObject x = rows; x != null; x = VisualTreeHelper.GetParent(x)) if (x is ScrollViewer v) { scroll = v; break; }
                edge.Start();
            }
            void End(bool commit)
            {
                edge.Stop(); if (hosts == null) return;
                bool moved = commit && to != from; double slot = to > from ? sizes.Skip(from + 1).Take(to - from).Sum() : -sizes.Skip(to).Take(from - to).Sum();
                if (!moved) foreach (var x in hosts) if (x != host) Slide(x, 0);
                var order = hosts; int source = from, target = to; hosts = null;
                Slide(host, moved ? slot : 0, () =>
                {
                    host.Effect = null; Panel.SetZIndex(host, 0); rowDragging = false; var later = pendingRender; pendingRender = null;
                    if (moved && Place(order, source, target)) return;
                    foreach (var x in order) if (x != host) Slide(x, 0);
                    card.Background = card.IsMouseOver ? R("Hover") : rest; card.BorderBrush = Brushes.Transparent; later?.Invoke();
                });
            }
            edge.Tick += (s, e) => { if (scroll == null || hosts == null) return; double y = System.Windows.Input.Mouse.GetPosition(scroll).Y, speed = y < 40 ? -Math.Min(18, (40 - y) * .5) : y > scroll.ActualHeight - 40 ? Math.Min(18, (y - scroll.ActualHeight + 40) * .5) : 0; if (speed != 0) { scroll.ScrollToVerticalOffset(scroll.VerticalOffset + speed); scroll.UpdateLayout(); } Track(); };
            handle.MouseLeftButtonDown += (s, e) => { if (rowDragging) return; start = e.GetPosition(rows).Y; pressed = true; handle.Focus(); handle.CaptureMouse(); e.Handled = true; };
            handle.MouseMove += (s, e) => { if (!pressed) return; if (hosts == null) { if (Math.Abs(e.GetPosition(rows).Y - start) < SystemParameters.MinimumVerticalDragDistance) return; Begin(); } Track(); };
            handle.MouseLeftButtonUp += (s, e) => { if (!pressed) return; pressed = false; End(true); handle.ReleaseMouseCapture(); e.Handled = true; };
            handle.LostMouseCapture += (s, e) => { if (!pressed) return; pressed = false; End(false); };
            handle.PreviewKeyDown += (s, e) => { if (e.Key != System.Windows.Input.Key.Escape || !pressed) return; pressed = false; End(false); handle.ReleaseMouseCapture(); e.Handled = true; };
            handle.KeyDown += (s, e) => { if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Alt || rowDragging) return; var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key; if (key != System.Windows.Input.Key.Up && key != System.Windows.Input.Key.Down) return; var order = Rows(); int i = order.IndexOf(host), j = i + (key == System.Windows.Input.Key.Up ? -1 : 1); if (j >= 0 && j < order.Count) Place(order, i, j); e.Handled = true; };
            if (item == focusedRow) { focusedRow = null; handle.Loaded += (s, e) => handle.Focus(); }
            card.MouseEnter += (s, e) => { if (rowDragging) return; card.Background = R("Hover"); handle.Foreground = R("Text"); };
            card.MouseLeave += (s, e) => { if (rowDragging) return; card.Background = rest; handle.Foreground = R("Muted"); };
        }
        void MicrophonesReordered() { Storage.SaveSettings(settings); MicrophonesChanged?.Invoke(); renderMicrophones?.Invoke(); }
        void Select(StackPanel p, string title, string desc, string[] values, int selected, Action<int> onChange, bool enabled = true) { var s = new StackPanel(); s.Children.Add(T(title, 15)); var d = T(desc, 12, "Muted"); d.Margin = new Thickness(0, 6, 0, 14); s.Children.Add(d); var combo = new ComboBox { ItemsSource = values, SelectedIndex = Math.Max(0, selected), MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = enabled }; if (FirstSelector == null) FirstSelector = combo; AutomationProperties.SetName(combo, title); combo.SelectionChanged += (a, b) => { if (combo.SelectedIndex >= 0) try { onChange(combo.SelectedIndex); } catch { SetStatus(L.T("The setting could not be changed.")); } }; s.Children.Add(combo); p.Children.Add(Card(s)); }
        void Info(StackPanel p, string title, string desc) { var s = new StackPanel(); s.Children.Add(T(title, 15)); var d = T(desc, 12, "Muted"); d.Margin = new Thickness(0, 7, 0, 0); s.Children.Add(d); p.Children.Add(Card(s)); }
    }
}
