using System.Collections.Generic;
using seamless_loop_music.Models;

namespace seamless_loop_music.Data
{
    public interface IDatabaseHelper
    {
        IEnumerable<MusicTrack> GetAllTracks();
        MusicTrack GetTrack(string fullPath, long totalSamples);
        void SaveTrack(MusicTrack track);
        void BulkInsert(IEnumerable<MusicTrack> tracks);

        // 歌单管理
        IEnumerable<PlaylistFolder> GetAllPlaylists();
        int AddPlaylist(string name, string folderPath = null, bool isLinked = false);
        void DeletePlaylist(int playlistId);
        void RenamePlaylist(int playlistId, string newName);
        void BulkSaveTracksAndAddToPlaylist(IEnumerable<MusicTrack> tracks, int playlistId);
        void AddSongToPlaylist(int playlistId, int songId);
        void RemoveSongFromPlaylist(int playlistId, int songId);
        IEnumerable<MusicTrack> GetPlaylistTracks(int playlistId);
        bool IsSongInPlaylist(int playlistId, int songId);
        void UpdatePlaylistsSortOrder(List<int> playlistIds);
        void UpdateTracksSortOrder(int playlistId, List<int> songIds);

        // 文件夹管理
        void AddPlaylistFolder(int playlistId, string folderPath);
        void RemovePlaylistFolder(int playlistId, string folderPath);
        List<string> GetPlaylistFolders(int playlistId);
    }
}
