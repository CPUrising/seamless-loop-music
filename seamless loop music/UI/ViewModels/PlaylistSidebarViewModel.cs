using Prism.Commands;
using Prism.Mvvm;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Data;

namespace seamless_loop_music.UI.ViewModels
{
    public class PlaylistSidebarViewModel : BindableBase
    {
        private readonly IPlayerService _playerService;

        private ObservableCollection<PlaylistFolder> _playlists;
        public ObservableCollection<PlaylistFolder> Playlists
        {
            get => _playlists;
            set => SetProperty(ref _playlists, value);
        }

        private ObservableCollection<MusicTrack> _currentTracks;
        public ObservableCollection<MusicTrack> CurrentTracks
        {
            get => _currentTracks;
            set => SetProperty(ref _currentTracks, value);
        }

        private PlaylistFolder _selectedPlaylist;
        public PlaylistFolder SelectedPlaylist
        {
            get => _selectedPlaylist;
            set
            {
                if (SetProperty(ref _selectedPlaylist, value) && value != null)
                {
                    LoadTracksForPlaylist(value.Id);
                }
            }
        }

        private MusicTrack _selectedTrack;
        public MusicTrack SelectedTrack
        {
            get => _selectedTrack;
            set => SetProperty(ref _selectedTrack, value);
        }

        public DelegateCommand<MusicTrack> PlayTrackCommand { get; }
        public DelegateCommand AddPlaylistCommand { get; }
        public DelegateCommand RefreshPlaylistCommand { get; }
        public DelegateCommand DeletePlaylistCommand { get; }
        public DelegateCommand RenamePlaylistCommand { get; }
        public DelegateCommand AddFilesCommand { get; }
        public DelegateCommand OpenFolderCommand { get; }
        public DelegateCommand RemoveTrackCommand { get; }
        public DelegateCommand BatchAnalysisCommand { get; }

        public PlaylistSidebarViewModel(IPlayerService playerService)
        {
            _playerService = playerService;

            Playlists = new ObservableCollection<PlaylistFolder>();
            CurrentTracks = new ObservableCollection<MusicTrack>();

            PlayTrackCommand = new DelegateCommand<MusicTrack>(ExecutePlayTrack);
            AddPlaylistCommand = new DelegateCommand(ExecuteAddPlaylist);
            RefreshPlaylistCommand = new DelegateCommand(ExecuteRefreshPlaylist);
            DeletePlaylistCommand = new DelegateCommand(ExecuteDeletePlaylist);
            RenamePlaylistCommand = new DelegateCommand(ExecuteRenamePlaylist);
            AddFilesCommand = new DelegateCommand(ExecuteAddFiles);
            OpenFolderCommand = new DelegateCommand(ExecuteOpenFolder);
            RemoveTrackCommand = new DelegateCommand(ExecuteRemoveTrack);
            BatchAnalysisCommand = new DelegateCommand(ExecuteBatchAnalysis);

            LoadPlaylists();

            // 订阅索引变化，以便在列表中同步选中状态
            _playerService.OnIndexChanged += (index) =>
            {
                if (index >= 0 && index < CurrentTracks.Count)
                {
                    SelectedTrack = CurrentTracks[index];
                }
            };
        }

        private void LoadPlaylists()
        {
            var list = _playerService.GetAllPlaylists();
            Playlists.Clear();
            foreach (var p in list) Playlists.Add(p);
            
            if (Playlists.Count > 0 && SelectedPlaylist == null)
            {
                SelectedPlaylist = Playlists[0];
            }
        }

        private void LoadTracksForPlaylist(int playlistId)
        {
            var tracks = _playerService.LoadPlaylistFromDb(playlistId);
            CurrentTracks.Clear();
            foreach (var t in tracks) CurrentTracks.Add(t);
        }

        private void ExecutePlayTrack(MusicTrack track)
        {
            if (track == null) return;
            
            // 找到它在当前列表中的索引
            int index = CurrentTracks.IndexOf(track);
            if (index != -1)
            {
                _playerService.Playlist = CurrentTracks.ToList();
                _playerService.PlayAtIndex(index);
            }
        }

        private void ExecuteAddPlaylist()
        {
            // 这里可能需要弹出对话框，暂时保留逻辑
        }

        private void ExecuteRefreshPlaylist()
        {
            if (SelectedPlaylist != null)
            {
                _playerService.RefreshPlaylist(SelectedPlaylist.Id);
                LoadPlaylists();
                LoadTracksForPlaylist(SelectedPlaylist.Id);
            }
        }

        private void ExecuteDeletePlaylist()
        {
            if (SelectedPlaylist != null)
            {
                // 注意：这里为了简化暂时不弹窗，实际应用建议弹窗确认
                _playerService.DeletePlaylist(SelectedPlaylist.Id);
                LoadPlaylists();
            }
        }

        private void ExecuteRenamePlaylist()
        {
            if (SelectedPlaylist != null)
            {
                // 暂时使用硬编码重命名，后续可接入对话框
                // _playerService.RenamePlaylist(SelectedPlaylist.Id, "New Name");
            }
        }

        private async void ExecuteAddFiles()
        {
            if (SelectedPlaylist != null && !SelectedPlaylist.IsFolderLinked)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Multiselect = true,
                    Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac;*.m4a;*.wma|All Files|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    await _playerService.AddFilesToPlaylist(SelectedPlaylist.Id, dialog.FileNames);
                    LoadTracksForPlaylist(SelectedPlaylist.Id);
                }
            }
        }

        private void ExecuteOpenFolder()
        {
            if (SelectedTrack != null && System.IO.File.Exists(SelectedTrack.FilePath))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{SelectedTrack.FilePath}\"");
            }
        }

        private void ExecuteRemoveTrack()
        {
            if (SelectedTrack != null && SelectedPlaylist != null)
            {
                _playerService.RemoveTrackFromPlaylist(SelectedPlaylist.Id, SelectedTrack.Id);
                LoadTracksForPlaylist(SelectedPlaylist.Id);
            }
        }

        private async void ExecuteBatchAnalysis()
        {
            // 此处通常需要当前列表的所有歌曲
            if (CurrentTracks.Count > 0)
            {
                // 简化调用，实际可传入回调更新 UI
                await _playerService.BatchSmartMatchLoopExternalAsync(CurrentTracks.ToList(), null, null);
            }
        }
    }
}
