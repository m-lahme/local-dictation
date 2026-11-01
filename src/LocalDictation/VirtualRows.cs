using System;
using System.Windows;
using System.Windows.Controls;
namespace LocalDictation
{
    // Scrollable list that builds rows only for items in view, so long lists open and filter quickly.
    sealed class VirtualRows<T> : ItemsControl where T : class
    {
        readonly Func<T, UIElement> build;
        public readonly ScrollViewer Scroll;
        public VirtualRows(Func<T, UIElement> build)
        {
            this.build = build; var scroll = new FrameworkElementFactory(typeof(ScrollViewer), "scroll"); scroll.SetValue(ScrollViewer.CanContentScrollProperty, true); scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            Template = new ControlTemplate(typeof(ItemsControl)) { VisualTree = scroll }; ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            VirtualizingPanel.SetIsVirtualizing(this, true); VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling); VirtualizingPanel.SetScrollUnit(this, ScrollUnit.Pixel);
            ApplyTemplate(); Scroll = (ScrollViewer)Template.FindName("scroll", this);
        }
        protected override void PrepareContainerForItemOverride(DependencyObject element, object item) { base.PrepareContainerForItemOverride(element, item); ((ContentPresenter)element).Content = build((T)item); }
    }
}
