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
        private readonly IPlayerService _playerService;
        private readonly int _playlistId;
        private readonly string _playlistName;

        public FolderManagerWindow(IPlayerService service, int playlistId, string playlistName)
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
            Title = "Folder Manager";
            lblTitle.Text = "Linked Folders for: " + _playlistName;
        }

        private void LoadFolders()
        {
            var folders = _playerService.GetPlaylistFolders(_playlistId);
            lstFolders.ItemsSource = folders;
        }

        private async void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Title = "Select Folder (Select any file)";
            if (dialog.ShowDialog() == true)
            {
                string path = Path.GetDirectoryName(dialog.FileName);
                await _playerService.AddFolderToPlaylist(_playlistId, path);
                LoadFolders();
            }
        }

        private async void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (lstFolders.SelectedItem is string path)
            {
                await _playerService.RemoveFolderFromPlaylist(_playlistId, path);
                LoadFolders();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
