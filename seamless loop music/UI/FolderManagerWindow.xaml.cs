using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using seamless_loop_music.Services;
using System.IO;

namespace seamless_loop_music.UI
{
    public partial class FolderManagerWindow : Window
    {
        private readonly PlayerService _playerService;
        private readonly int _playlistId;
        private readonly string _playlistName;

        public FolderManagerWindow(PlayerService service, int playlistId, string playlistName)
        {
            InitializeComponent();
            _playerService = service;
            _playlistId = playlistId;
            _playlistName = playlistName;

            ApplyLanguage();
            LoadFolders();
        }

        private void ApplyLanguage()
        {
            bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
            Title = isZh ? "管理关联文件夹" : "Folder Manager";
            lblTitle.Text = (isZh ? "歌单关联文件夹: " : "Linked Folders for: ") + _playlistName;
            btnAdd.Content = isZh ? "+ 添加文件夹" : "+ Add Folder";
            btnClose.Content = isZh ? "关闭" : "Close";
        }

        private void LoadFolders()
        {
            var folders = _playerService.GetPlaylistFolders(_playlistId);
            lstFolders.ItemsSource = folders;
        }

        private async void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            if (picker.ShowDialog(this))
            {
                await _playerService.AddFolderToPlaylist(_playlistId, picker.ResultPath);
                LoadFolders();
            }
        }

        private async void btnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                bool isZh = Properties.Resources.Culture?.Name.StartsWith("zh") ?? false;
                var result = MessageBox.Show(
                    isZh ? $"确定要移除此文件夹关联吗？\n{path}\n(磁盘上的文件不会被删除)" 
                         : $"Are you sure to remove this folder link?\n{path}\n(Files on disk won't be deleted)",
                    isZh ? "确认移除" : "Confirm Removal",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _playerService.RemoveFolderFromPlaylist(_playlistId, path);
                    LoadFolders();
                }
            }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
