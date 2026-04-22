using System.Windows;

namespace seamless_loop_music.UI.Converters
{
    /// <summary>
    /// WPF 绑定代理：利用 Freezable 可继承 DataContext 的特性，
    /// 将 ViewModel 暴露给 ContextMenu 等不在可视树中的元素。
    /// 用法：在 Resources 中声明 <local:BindingProxy x:Key="Proxy" Data="{Binding}"/>
    ///       在 ContextMenu 中使用 Command="{Binding Data.SomeCommand, Source={StaticResource Proxy}}"
    /// </summary>
    public class BindingProxy : Freezable
    {
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy));

        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        protected override Freezable CreateInstanceCore() => new BindingProxy();
    }
}
