using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using seamless_loop_music.Models;
using seamless_loop_music.Services;

namespace seamless_loop_music.UI.Views
{
    /// <summary>
    /// 添加到播放列表对话框的交互逻辑。
    /// 显示所有现有歌单，允许用户选中并将曲目添加进去，也可新建歌单。
    /// </summary>
    public partial class AddToPlaylistDialog : Window
    {
        private readonly IPlaylistManager _playlistManager;
        private readonly List<MusicTrack> _tracks;
        private List<Playlist> _playlists;

        /// <summary>
        /// 用户最终选择的歌单（确认后）
        /// </summary>
        public Playlist SelectedPlaylist { get; private set; }

        public AddToPlaylistDialog(IPlaylistManager playlistManager, List<MusicTrack> tracks)
        {
            InitializeComponent();
            _playlistManager = playlistManager;
            _tracks = tracks;

            int count = tracks.Count;
            TxtSubtitle.Text = count == 1
                ? $"将「{tracks[0].Title}」添加到："
                : $"将 {count} 首曲目添加到：";

            Loaded += async (_, __) =>
            {
                _playlists = await _playlistManager.GetAllPlaylistsAsync();

                // 过滤掉系统保留歌单（Id < 0）
                _playlists = _playlists.FindAll(p => p.Id > 0);

                PlaylistListBox.ItemsSource = _playlists;
                PlaylistListBox.DisplayMemberPath = "Name";

                if (_playlists.Count > 0)
                    PlaylistListBox.SelectedIndex = 0;
            };
        }

        private async void TxtNewPlaylistName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var name = TxtNewPlaylistName.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var newPlaylist = await _playlistManager.CreatePlaylistAsync(name);
            SelectedPlaylist = newPlaylist;
            await AddTracksToPlaylist();
            DialogResult = true;
        }

        private async void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            SelectedPlaylist = PlaylistListBox.SelectedItem as Playlist;
            if (SelectedPlaylist == null)
            {
                MessageBox.Show("请先选择一个播放列表。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await AddTracksToPlaylist();
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private async System.Threading.Tasks.Task AddTracksToPlaylist()
        {
            if (SelectedPlaylist == null) return;
            foreach (var track in _tracks)
                await _playlistManager.AddTrackToPlaylistAsync(SelectedPlaylist.Id, track);
        }
    }
}
