using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace seamless_loop_music.UI.Controls
{
    /// <summary>
    /// 支持 MVVM 多选绑定的 ListBox 扩展控件。
    /// 通过 SelectedItemsBinding 属性可双向绑定选中项集合。
    /// </summary>
    public class MultiSelectListBox : ListBox
    {
        private bool _isSyncing;

        /// <summary>
        /// SelectedItemsBinding — 可在 XAML 绑定的多选集合属性
        /// </summary>
        public static readonly DependencyProperty SelectedItemsBindingProperty =
            DependencyProperty.Register(
                nameof(SelectedItemsBinding),
                typeof(IList),
                typeof(MultiSelectListBox),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedItemsBindingChanged));

        public IList SelectedItemsBinding
        {
            get => (IList)GetValue(SelectedItemsBindingProperty);
            set => SetValue(SelectedItemsBindingProperty, value);
        }

        private static void OnSelectedItemsBindingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var listBox = (MultiSelectListBox)d;

            // 解除旧集合监听
            if (e.OldValue is INotifyCollectionChanged oldCollection)
                oldCollection.CollectionChanged -= listBox.OnBindingCollectionChanged;

            // 注册新集合监听（如果是可观察集合）
            if (e.NewValue is INotifyCollectionChanged newCollection)
                newCollection.CollectionChanged += listBox.OnBindingCollectionChanged;

            // 初次同步：从 ViewModel 集合 → ListBox.SelectedItems
            listBox.SyncFromBinding();
        }

        private void OnBindingCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SyncFromBinding();
        }

        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            base.OnSelectionChanged(e);
            if (_isSyncing) return;

            // 同步 ListBox.SelectedItems → ViewModel 绑定集合
            var binding = SelectedItemsBinding;
            if (binding == null) return;

            _isSyncing = true;
            try
            {
                binding.Clear();
                foreach (var item in SelectedItems)
                    binding.Add(item);
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void SyncFromBinding()
        {
            if (_isSyncing) return;
            var binding = SelectedItemsBinding;
            if (binding == null) return;

            _isSyncing = true;
            try
            {
                SetSelectedItems(binding);
            }
            finally
            {
                _isSyncing = false;
            }
        }
    }
}
