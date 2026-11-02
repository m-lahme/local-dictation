using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace LocalDictation
{
    public static class ApplicationMenus
    {
        public sealed class Command
        {
            public string Title { get; init; }
            public string Glyph { get; init; }
            public Action Action { get; init; }
            public bool Enabled { get; init; } = true;
        }
        const int WH_MOUSE_LL=14, WM_LBUTTONDOWN=0x0201, WM_RBUTTONDOWN=0x0204, WM_MBUTTONDOWN=0x0207, WM_XBUTTONDOWN=0x020B;
        delegate IntPtr MouseHook(int code,IntPtr message,IntPtr data);
        [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; }
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,MouseHook callback,IntPtr module,uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        sealed class OutsideClickDismissal:IDisposable
        {
            readonly ContextMenu menu;readonly IntPtr window;readonly MouseHook callback;IntPtr hook;
            public OutsideClickDismissal(ContextMenu menu,HwndSource source){this.menu=menu;window=source.Handle;callback=Handle;hook=SetWindowsHookEx(WH_MOUSE_LL,callback,GetModuleHandle(null),0);menu.PreviewKeyDown+=KeyDown;if(hook==IntPtr.Zero)Storage.Log("menu: outside-click hook unavailable: "+Marshal.GetLastWin32Error());}
            void KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;menu.IsOpen=false;}}
            IntPtr Handle(int code,IntPtr message,IntPtr data){int m=message.ToInt32();if(code>=0&&(m==WM_LBUTTONDOWN||m==WM_RBUTTONDOWN||m==WM_MBUTTONDOWN||m==WM_XBUTTONDOWN)&&GetCursorPos(out var p)&&GetWindowRect(window,out var r)&&(p.X<r.Left||p.X>=r.Right||p.Y<r.Top||p.Y>=r.Bottom))menu.Dispatcher.BeginInvoke(new Action(()=>menu.IsOpen=false));return CallNextHookEx(hook,code,message,data);}
            public void Dispose(){menu.PreviewKeyDown-=KeyDown;if(hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}}
        }

        static ContextMenu Shell(Settings settings,double minWidth,PlacementMode placement)
        {
            var menu=new ContextMenu{MinWidth=minWidth,FontFamily=new FontFamily("Segoe UI"),FontSize=13,HasDropShadow=false,StaysOpen=true,Placement=placement};Theme.Apply(menu,settings.Theme);
            OutsideClickDismissal dismissal=null;
            menu.Opened+=(s,e)=>{var source=PresentationSource.FromVisual(menu) as HwndSource;if(source!=null)dismissal=new OutsideClickDismissal(menu,source);menu.Focus();};
            menu.Closed+=(s,e)=>{dismissal?.Dispose();dismissal=null;};
            menu.Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ContextMenu}'><Border Background='{DynamicResource SurfaceRaised}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='11' Padding='5'><StackPanel IsItemsHost='True'/></Border></ControlTemplate>");
            menu.Resources[typeof(MenuItem)]=(Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type MenuItem}'><Setter Property='Foreground' Value='{DynamicResource Text}'/><Setter Property='FontSize' Value='13'/><Setter Property='Height' Value='33'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type MenuItem}'><Border x:Name='Row' CornerRadius='6' Padding='9,0' Background='Transparent'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='26'/><ColumnDefinition/></Grid.ColumnDefinitions><ContentPresenter ContentSource='Icon' VerticalAlignment='Center'/><ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/></Grid></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Row' Property='Background' Value='{DynamicResource Hover}'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
            return menu;
        }
        static void Add(ContextMenu menu,string title,string glyph,Action action,bool enabled=true){var item=new MenuItem{Header=title,Icon=new TextBlock{Text=glyph,FontFamily=Theme.IconFont,FontSize=14,Foreground=(Brush)menu.Resources["Muted"]},IsEnabled=enabled};item.Click+=(s,e)=>{menu.IsOpen=false;action();};menu.Items.Add(item);}
        static void Separate(ContextMenu menu)=>menu.Items.Add(new Separator{Margin=new Thickness(8,4,8,4),Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Border Height='1' Background='{DynamicResource Line}'/></ControlTemplate>")});
        public static ContextMenu Create(Settings settings,bool busy,Action history,Action dictionary,Action visibility,Action preferences,Action quit)
        {
            var menu=Shell(settings,225,PlacementMode.MousePoint);
            Add(menu,"Verlauf","\uE81C",history);Add(menu,"Wörterbuch","\uE8D2",dictionary);Add(menu,settings.OverlayVisible?"Overlay ausblenden":"Overlay anzeigen","\uE890",visibility,!busy);Add(menu,"Einstellungen","\uE713",preferences);
            Separate(menu);Add(menu,"Beenden","\uE7E8",quit);return menu;
        }
        public static ContextMenu CreateRetry(Settings settings,IReadOnlyList<Command> variants,Action<string> retry)
        {
            var menu=Shell(settings,220,PlacementMode.Bottom);
            foreach(var command in variants)Add(menu,command.Title,command.Glyph,command.Action,command.Enabled);
            if(variants.Count>0)Separate(menu);
            Add(menu,"Mit Whisper neu versuchen","\uE72C",()=>retry("whisper"));
            Add(menu,"Mit Parakeet neu versuchen","\uE72C",()=>retry("parakeet"));
            return menu;
        }
    }
}
