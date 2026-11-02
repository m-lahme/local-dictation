using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Diagnostics;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace LocalDictation
{
    public sealed class OverlayWindow : Window
    {
        readonly Border shell; readonly Rectangle[] bars = new Rectangle[5]; readonly DispatcherTimer feedback; readonly Stopwatch clock = Stopwatch.StartNew();
        readonly double[] currentH = {3,3,3,3,3}, currentY = new double[5];
        readonly double[] targetBands=new double[5], smoothedBands=new double[5];
        double targetLevel, smoothedLevel, lastFrame; bool rendering; Color currentColor = Color.FromRgb(130,134,144);
        AppState state; double phase; bool dragging; Point down; double startLeft, startTop; string theme = "dark";
        readonly Grid meter; readonly StackPanel notice; readonly TextBlock noticeIcon, noticeText; bool expanded; double compactLeft, compactTop, expandedLeft, expandedTop;
        const double CompactWidth = 52, CompactHeight = 26, NoticeWidth = 206, NoticeHeight = 36;
        public event Action MenuRequested; public event Action IdleReached;
        public AppState State { get { return state; } }
        public static string Description(AppState value) => value switch {
            AppState.Idle => L.T("Ready"),
            AppState.Recording => L.T("Recording"),
            AppState.Waiting => L.T("Loading model"),
            AppState.Transcribing => L.T("Transcribing"),
            AppState.Success => L.T("Text delivered"),
            AppState.Review => L.T("Text delivered · please review"),
            AppState.Error => L.T("Error, see History or Settings"),
            AppState.Copied => L.T("Not inserted · text is on the clipboard"),
            _ => "Local Dictation"
        };
        static readonly double[] IdleH = { 3, 3, 3, 3, 3 }, IdleY = { 0, 0, 0, 0, 0 }, MeterH = { 7, 13, 19, 13, 7 }, CheckH = { 5, 5, 5, 5, 5 }, CheckY = { 2, 6, 3, -1, -5 }, ErrorH = { 11, 4, 16, 4, 11 };
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
        public OverlayWindow()
        {
            Width = 52; Height = 26; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true; Title = "Local Dictation Status";
            SourceInitialized += (s, e) => { var h = new WindowInteropHelper(this).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x80); };
            shell = new Border { Width = 52, Height = 26, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1) }; Content = shell;
            meter = new Grid { Width = 21, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 5; i++) { bars[i] = new Rectangle { Width = 3, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = new SolidColorBrush(), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(i * 4.5, 0, 0, 0), RenderTransform = new TranslateTransform() }; meter.Children.Add(bars[i]); }
            noticeIcon = new TextBlock { Text = "", FontFamily = Theme.IconFont, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
            noticeText = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            notice = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Children = { noticeIcon, noticeText } };
            shell.Child = new Grid { Children = { meter, notice } };
            MouseLeftButtonDown += (s, e) => { down = PointToScreen(e.GetPosition(this)); startLeft = Left; startTop = Top; dragging = false; CaptureMouse(); };
            MouseMove += (s, e) => { if (!IsMouseCaptured) return; var q = PointToScreen(e.GetPosition(this)); var dpi = VisualTreeHelper.GetDpi(this); double dx = (q.X - down.X) / dpi.DpiScaleX, dy = (q.Y - down.Y) / dpi.DpiScaleY; if (Math.Abs(dx) + Math.Abs(dy) > 4) dragging = true; if (dragging) { Left = startLeft + dx; Top = startTop + dy; } };
            MouseLeftButtonUp += (s, e) => { ReleaseMouseCapture(); if (!dragging) MenuRequested?.Invoke(); }; MouseRightButtonUp += (s, e) => MenuRequested?.Invoke();
            feedback = new DispatcherTimer(); feedback.Tick += (s, e) => { feedback.Stop(); SetState(AppState.Idle, 0); };
            IsVisibleChanged += (s,e) => { if (IsVisible) StartFrames(); else StopFrames(); };
            Closed += (s,e) => { StopFrames(); feedback.Stop(); }; ApplyTheme("dark"); SetState(AppState.Idle, 0);
        }
        public void ApplyTheme(string mode) { theme = mode; bool dark = Theme.IsDark(mode); shell.Background = Theme.Brush(dark ? Color.FromRgb(22, 24, 29) : Color.FromRgb(255, 254, 251)); shell.BorderBrush = Theme.Brush(dark ? Color.FromRgb(49, 52, 61) : Color.FromRgb(214, 210, 202)); noticeIcon.Foreground = Theme.Brush(dark ? Color.FromRgb(91, 156, 240) : Color.FromRgb(37, 99, 190)); noticeText.Foreground = Theme.Brush(dark ? Color.FromRgb(232, 233, 236) : Color.FromRgb(30, 31, 36)); StartFrames(); }
        public void SetState(AppState next, double level) {
            feedback.Stop(); state = next; phase = 0; targetLevel = Math.Clamp(level,0,1);for(int i=0;i<5;i++)targetBands[i]=targetLevel;
            RefreshText(); Layout(next == AppState.Copied, SystemParameters.ClientAreaAnimation); StartFrames();
            if (next == AppState.Success || next == AppState.Review || next == AppState.Error || next == AppState.Copied) { feedback.Interval = TimeSpan.FromMilliseconds(next == AppState.Copied ? 5000 : next == AppState.Error ? 4000 : 2600); feedback.Start(); }
            if (next == AppState.Idle) IdleReached?.Invoke();
        }
        // Also called after a language change, so tooltip, accessible name and notice follow the new language.
        public void RefreshText() { ToolTip = Description(state); System.Windows.Automation.AutomationProperties.SetName(this, "Local Dictation: " + ToolTip); noticeText.Text = L.T("Not inserted · Ctrl+V"); }
        public void SetLevels(double[] levels) {if(state!=AppState.Recording)return;for(int i=0;i<5;i++)targetBands[i]=double.IsFinite(levels[i])?Math.Clamp(levels[i],0,1):0;StartFrames();}
        public void SetLevel(double level) { if (state == AppState.Recording) { targetLevel = double.IsFinite(level) ? Math.Clamp(level,0,1) : 0;double[] shape={.58,.83,1,.65,.9};for(int i=0;i<5;i++)targetBands[i]=targetLevel*shape[i];StartFrames(); } }
        void StartFrames() {
            if (!SystemParameters.ClientAreaAnimation) {Array.Copy(targetBands,smoothedBands,5); Render(state,targetLevel,phase,1); StopFrames(); return; }
            if (rendering || !IsVisible) return;
            lastFrame = clock.Elapsed.TotalSeconds; CompositionTarget.Rendering += Frame; rendering = true;
        }
        void StopFrames() { if (!rendering) return; CompositionTarget.Rendering -= Frame; rendering = false; }
        void Frame(object sender, EventArgs e) {
            double now = clock.Elapsed.TotalSeconds, dt = Math.Clamp(now-lastFrame,0,.05); lastFrame = now; if(dt<=0) return;
            phase += dt * 5.2;
            // Fast attack, gentle release; microphone packet cadence never resets an animation clock.
            smoothedLevel += (targetLevel-smoothedLevel) * (1-Math.Exp(-dt/(targetLevel>smoothedLevel?.045:.15)));
            for(int i=0;i<5;i++)smoothedBands[i]+=(targetBands[i]-smoothedBands[i])*(1-Math.Exp(-dt/(targetBands[i]>smoothedBands[i]?.04+i*.004:.13+i*.012)));
            bool settled = Render(state,smoothedLevel,phase,1-Math.Exp(-dt/.055));
            if(settled && state != AppState.Recording && state != AppState.Waiting && state != AppState.Transcribing) StopFrames();
        }
        // The notice grows around the compact pill: centred horizontally, away from the nearer screen edge vertically, and always inside the work area.
        void Layout(bool show, bool animate)
        {
            if (show == expanded) return; expanded = show;
            if (show)
            {
                compactLeft = Left; compactTop = Top; var area = WorkArea(); bool upward = Top + CompactHeight / 2 > area.Top + area.Height / 2;
                Width = NoticeWidth; Height = NoticeHeight; Left = Math.Max(area.Left, Math.Min(area.Right - NoticeWidth, Left + (CompactWidth - NoticeWidth) / 2)); Top = upward ? Top + CompactHeight - NoticeHeight : Top;
                expandedLeft = Left; expandedTop = Top; shell.VerticalAlignment = upward ? VerticalAlignment.Bottom : VerticalAlignment.Top; shell.CornerRadius = new CornerRadius(NoticeHeight / 2);
                meter.Visibility = Visibility.Collapsed; notice.Visibility = Visibility.Visible; notice.BeginAnimation(OpacityProperty, animate ? new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) : null);
                Grow(NoticeWidth, NoticeHeight, animate, null);
            }
            else
            {
                bool moved = Left != expandedLeft || Top != expandedTop; double left = moved ? Left + (Width - CompactWidth) / 2 : compactLeft, top = moved ? (shell.VerticalAlignment == VerticalAlignment.Bottom ? Top + Height - CompactHeight : Top) : compactTop;
                notice.Visibility = Visibility.Collapsed; meter.Visibility = Visibility.Visible;
                Grow(CompactWidth, CompactHeight, animate, () => { if (expanded) return; Width = CompactWidth; Height = CompactHeight; Left = left; Top = top; shell.CornerRadius = new CornerRadius(13); shell.VerticalAlignment = VerticalAlignment.Center; });
            }
        }
        void Grow(double width, double height, bool animate, Action done)
        {
            if (!animate) { shell.BeginAnimation(WidthProperty, null); shell.BeginAnimation(HeightProperty, null); shell.Width = width; shell.Height = height; done?.Invoke(); return; }
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut }; var grow = new DoubleAnimation(width, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease }; if (done != null) grow.Completed += (s, e) => done();
            shell.BeginAnimation(WidthProperty, grow); shell.BeginAnimation(HeightProperty, new DoubleAnimation(height, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        }
        Rect WorkArea() { var area = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea; var dpi = VisualTreeHelper.GetDpi(this); return new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY, area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY); }
        public void Snapshot(AppState next, double level, double time) { StopFrames(); feedback.Stop(); state=next; Layout(next == AppState.Copied, false);for(int i=0;i<5;i++)smoothedBands[i]=level*new[]{.58,.83,1,.65,.9}[i]; Render(next,level,time,1); }
        bool Render(AppState s, double level, double time, double blend) {
            double[] h = IdleH, y = IdleY; bool dark = Theme.IsDark(theme); Color c = dark ? Color.FromRgb(130,134,144) : Color.FromRgb(118,119,125);
            if(s==AppState.Recording) { h=new double[5];double voice=0;for(int i=0;i<5;i++)voice+=smoothedBands[i]/5;for(int i=0;i<5;i++){double envelope = new double[]{.60,.84,1,.84,.60}[i]; double paired = (smoothedBands[i]+smoothedBands[4-i])*.5; double basis = envelope*(voice*.65+paired*.35); double movement=SystemParameters.ClientAreaAnimation?basis*(.92+.08*Math.Sin(time*.85+i*1.73))+voice*.035*Math.Sin(time*.47+i*2.31):basis;h[i]=3+16*Math.Clamp(movement,0,1);}c=Theme.Accent; }
            else if(s==AppState.Success || s==AppState.Review) { h=CheckH; y=CheckY; c=s==AppState.Review?(dark?Color.FromRgb(245,174,66):Color.FromRgb(180,105,20)):(dark?Color.FromRgb(91,211,154):Color.FromRgb(25,133,84)); }
            else if(s==AppState.Error) { h=ErrorH; c=Color.FromRgb(232,74,88); }
            else if(s==AppState.Waiting || s==AppState.Transcribing) { h=new double[5]; for(int i=0;i<5;i++) h[i]=4+12*(.5+.5*Math.Sin(time+Math.Min(i,4-i)*.8)); c=s==AppState.Waiting?Color.FromRgb(77,148,237):dark?Color.FromRgb(245,174,66):Color.FromRgb(180,105,20); }
            bool settled=true;
            byte Mix(byte a,byte b) => (byte)Math.Round(a+(b-a)*Math.Min(1,blend*1.8));
            currentColor=Color.FromRgb(Mix(currentColor.R,c.R),Mix(currentColor.G,c.G),Mix(currentColor.B,c.B));
            if(Math.Abs(currentColor.R-c.R)+Math.Abs(currentColor.G-c.G)+Math.Abs(currentColor.B-c.B)<4) currentColor=c;
            settled=currentColor==c;
            for(int i=0;i<5;i++) {
                currentH[i]+=(h[i]-currentH[i])*blend; currentY[i]+=(y[i]-currentY[i])*blend;
                if(Math.Abs(h[i]-currentH[i])<.01)currentH[i]=h[i];else settled=false;
                if(Math.Abs(y[i]-currentY[i])<.01)currentY[i]=y[i];else settled=false;
                bars[i].Height=currentH[i]; ((TranslateTransform)bars[i].RenderTransform).Y=currentY[i]; ((SolidColorBrush)bars[i].Fill).Color=currentColor;
            }
            return settled;
        }
        public void Place(string anchor = "bottom-center", string monitor = "") { Layout(false, false); var screen = Array.Find(Forms.Screen.AllScreens, s => s.DeviceName == monitor) ?? Forms.Screen.PrimaryScreen; var area = screen.WorkingArea; var dpi = VisualTreeHelper.GetDpi(this); double left = area.Left / dpi.DpiScaleX, top = area.Top / dpi.DpiScaleY, width = area.Width / dpi.DpiScaleX, height = area.Height / dpi.DpiScaleY; Left = anchor.EndsWith("left") ? left + 4 : anchor.EndsWith("right") ? left + width - Width - 4 : left + (width - Width) / 2; Top = anchor.StartsWith("top") ? top + 4 : top + height - Height - 4; }
    }
}
