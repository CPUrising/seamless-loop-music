using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using Prism.Events;
using seamless_loop_music.Events;

using System.Threading.Tasks;

namespace seamless_loop_music.UI.Views
{
    /// <summary>
    /// 添加到播放列表对话框的交互逻辑。
    /// 显示所有现有歌单，允许用户选中并将曲目添加进去，也可新建歌单。
    /// </summary>
    public partial class AddToPlaylistDialog : Window
    {
        private readonly IPlaylistManager _playlistManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly List<MusicTrack> _tracks;
        private List<Playlist> _playlists;

        /// <summary>
        /// 用户最终选择的歌单（确认后）
        /// </summary>
        public Playlist SelectedPlaylist { get; private set; }

        public AddToPlaylistDialog(IPlaylistManager playlistManager, IEventAggregator eventAggregator, List<MusicTrack> tracks)
        {
            InitializeComponent();
            _playlistManager = playlistManager;
            _eventAggregator = eventAggregator;
            _tracks = tracks;

            int count = tracks.Count;
            TxtSubtitle.Text = count == 1
                ? string.Format(seamless_loop_music.Properties.Resources.MsgAddSingleTrackTo, tracks[0].Title)
                : string.Format(seamless_loop_music.Properties.Resources.MsgAddMultipleTracksTo, count);

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


        private async void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            await HandleConfirmAction();
        }

        private async System.Threading.Tasks.Task HandleConfirmAction()
        {
            var newName = TxtNewPlaylistName.Text?.Trim();
            
            // 如果展开了新建面板且输入了内容，则优先创建新歌单
            if (NewPlaylistExpander.IsExpanded && !string.IsNullOrEmpty(newName))
            {
                // 查重逻辑
                var allPlaylists = await _playlistManager.GetAllPlaylistsAsync();
                if (allPlaylists.Any(p => string.Equals(p.Name, newName, System.StringComparison.OrdinalIgnoreCase)))
                {
                    var msg = string.Format(seamless_loop_music.Properties.Resources.MsgPlaylistExists, newName);
                    MessageBox.Show(msg, seamless_loop_music.Properties.Resources.DialogNewPlaylist, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SelectedPlaylist = await _playlistManager.CreatePlaylistAsync(newName);
            }
            else
            {
                // 否则使用选中的现有歌单
                SelectedPlaylist = PlaylistListBox.SelectedItem as Playlist;
            }

            if (SelectedPlaylist == null)
            {
                MessageBox.Show(seamless_loop_music.Properties.Resources.MsgNameEmpty, seamless_loop_music.Properties.Resources.DialogSelectPlaylist, MessageBoxButton.OK, MessageBoxImage.Information);
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
            
            // 发送成功反馈
            var message = _tracks.Count == 1 
                ? string.Format(seamless_loop_music.Properties.Resources.StatusAddedSingleTo, _tracks[0].Title, SelectedPlaylist.Name)
                : string.Format(seamless_loop_music.Properties.Resources.StatusAddedMultipleTo, _tracks.Count, SelectedPlaylist.Name);
            _eventAggregator.GetEvent<StatusMessageEvent>().Publish(message);
        }
    }
}
