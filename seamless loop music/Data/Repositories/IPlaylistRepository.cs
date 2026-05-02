using System.Collections.Generic;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Data.Repositories
{
    public interface IPlaylistRepository
    {
        // Removed: IEnumerable<Playlist> GetAll();
        int Add(string name, string folderPath = null, bool isLinked = false);
        void Delete(int id);
        void Rename(int id, string newName);
        void UpdateSortOrder(IEnumerable<int> ids);
        
        Task<List<Playlist>> GetAllAsync(); // Replaced GetAll() and changed return type
        Task<List<MusicTrack>> GetTracksInPlaylistAsync(int playlistId); // Replaced GetTracks() and changed return type
        void AddTrack(int playlistId, int trackId);
        void RemoveTrack(int playlistId, int trackId);
        void UpdateTracksSortOrder(int playlistId, IEnumerable<int> trackIds);
        bool IsTrackInPlaylist(int playlistId, int trackId);

        // Folder management
        void AddFolder(int playlistId, string folderPath);
        void RemoveFolder(int playlistId, string folderPath);
        IEnumerable<string> GetFolders(int playlistId);
    }
}

