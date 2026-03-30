using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public interface IPlaylistManagerService
    {
        

        List<Playlist> GetAllPlaylists();
        int CreatePlaylist(string name, string folderPath = null, bool isLinked = false);
        void RenamePlaylist(int playlistId, string newName);
        void DeletePlaylist(int playlistId);
        
        void AddTrackToPlaylist(int playlistId, MusicTrack track);
        void AddTracksToPlaylist(int playlistId, List<MusicTrack> tracks);
        void RemoveTrackFromPlaylist(int playlistId, int songId);
        
        Task AddFilesToPlaylistAsync(int playlistId, string[] filePaths);
        Task AddFolderToPlaylistAsync(int playlistId, string folderPath);
        Task RemoveFolderFromPlaylistAsync(int playlistId, string folderPath);
        List<string> GetPlaylists(int playlistId);
        Task RefreshPlaylistAsync(int playlistId);
        
        List<MusicTrack> LoadPlaylistFromDb(int playlistId);
        MusicTrack GetStoredTrackInfo(string filePath);
        void UpdateOfflineTrack(MusicTrack track);
        
        long GetTotalSamples(string filePath);
        void UpdatePlaylistsSortOrder(List<int> playlistIds);
        void UpdateTracksSortOrder(int playlistId, List<int> songIds);
    }
}

