using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using TagLib;
using seamless_loop_music.Models;
using seamless_loop_music.Data;

namespace seamless_loop_music.Services
{
    public class PlayerService : IPlayerService
    {
        private readonly IDatabaseHelper _databaseHelper;
        private readonly IPlaybackService _playbackService;
        private readonly ILoopAnalysisService _loopAnalysisService;
        private readonly TrackMetadataService _metadataService;
        private readonly IPlaylistManagerService _playlistManager;

        public List<MusicTrack> Playlist { get; set; } = new List<MusicTrack>();
        public int CurrentIndex { get; set; } = -1;
        public MusicTrack CurrentTrack => _playbackService.CurrentTrack;
        public PlaybackState PlaybackState => _playbackService.PlaybackState;
        
        public long LoopStartSample { get => CurrentTrack?.LoopStart ?? 0; set => _playbackService.SetLoopPoints(value, LoopEndSample); }
        public long LoopEndSample { get => CurrentTrack?.LoopEnd ?? 0; set => _playbackService.SetLoopPoints(LoopStartSample, value); }
        public int SampleRate => _playbackService.SampleRate;

        public float Volume { get => _playbackService.Volume; set => _playbackService.Volume = value; }
        public bool IsSeamlessLoopEnabled { get => _playbackService.IsSeamlessLoopEnabled; set => _playbackService.IsSeamlessLoopEnabled = value; }
        public bool IsFeatureLoopEnabled { get => _playbackService.IsFeatureLoopEnabled; set => _playbackService.IsFeatureLoopEnabled = value; }
        public double MatchWindowSize { get; set; } = 1.0;
        public double MatchSearchRadius { get; set; } = 5.0;

        public PlayerService(IDatabaseHelper databaseHelper, IPlaybackService playbackService, ILoopAnalysisService loopAnalysisService, TrackMetadataService metadataService, IPlaylistManagerService playlistManager)
        {
            _databaseHelper = databaseHelper;
            _playbackService = playbackService;
            _loopAnalysisService = loopAnalysisService;
            _metadataService = metadataService;
            _playlistManager = playlistManager;
        }

        public void Play() => _playbackService.Play();
        public void Pause() => _playbackService.Pause();
        public void Stop() => _playbackService.Stop();
        public void Seek(double percent) => _playbackService.Seek(TimeSpan.FromSeconds(percent * _playbackService.TotalTime.TotalSeconds));
        public void SeekToSample(long sample) => _playbackService.SeekToSample(sample);

        public List<Playlist> GetAllPlaylists() => _databaseHelper.GetAllPlaylists();
        public int CreatePlaylist(string name, string folderPath = null, bool isLinked = false) => _databaseHelper.AddPlaylist(name, folderPath, isLinked);
        public void RenamePlaylist(int playlistId, string newName) => _databaseHelper.RenamePlaylist(playlistId, newName);
        public void DeletePlaylist(int playlistId) => _databaseHelper.DeletePlaylist(playlistId);

        public void AddTrackToPlaylist(int playlistId, MusicTrack track) => _databaseHelper.AddSongToPlaylist(playlistId, track.Id);
        public void AddTracksToPlaylist(int playlistId, List<MusicTrack> tracks) { foreach(var t in tracks) AddTrackToPlaylist(playlistId, t); }
        public void RemoveTrackFromPlaylist(int playlistId, int songId) => _databaseHelper.RemoveSongFromPlaylist(playlistId, songId);

        public Task AddFilesToPlaylist(int playlistId, string[] filePaths) => Task.CompletedTask;
        public Task AddFolderToPlaylist(int playlistId, string folderPath) { _databaseHelper.AddPlaylist(playlistId, folderPath); return Task.CompletedTask; }
        public Task RemoveFolderFromPlaylist(int playlistId, string folderPath) => Task.CompletedTask;
        public List<string> GetPlaylistFolders(int playlistId) => _databaseHelper.GetPlaylists(playlistId);
        public Task RefreshPlaylist(int playlistId) => _playlistManager.RefreshPlaylistAsync(playlistId);
        public List<MusicTrack> LoadPlaylistFromDb(int playlistId) => _databaseHelper.GetPlaylistTracks(playlistId).ToList();

        public void UpdatePlaylistsSortOrder(List<int> playlistIds) => _databaseHelper.UpdatePlaylistsSortOrder(playlistIds);
        public void UpdateTracksSortOrder(int playlistId, List<int> songIds) => _databaseHelper.UpdateTracksSortOrder(playlistId, songIds);

        public void SetLoopStart(long sample) => _playbackService.SetLoopPoints(sample, LoopEndSample);
        public void SetLoopEnd(long sample) => _playbackService.SetLoopPoints(LoopStartSample, sample);
        public void ApplyLoopCandidate(LoopCandidate candidate) { if (candidate != null) _playbackService.SetLoopPoints(candidate.LoopStart, candidate.LoopEnd); }
        public void ResetABLoopPoints() => _playbackService.ResetABLoopPoints();

        public async Task<List<LoopCandidate>> GetLoopCandidatesAsync()
        {
            if (CurrentTrack == null) return new List<LoopCandidate>();
            return await _loopAnalysisService.FetchTopLoopCandidatesAsync(CurrentTrack.FilePath);
        }

        public async Task<int> CheckPyMusicLooperStatusAsync()
        {
            return await _loopAnalysisService.CheckEnvironmentAsync();
        }

        public async Task UpdateTrackLoopCandidatesAsync(MusicTrack track, List<LoopCandidate> candidates)
        {
            if (track == null || candidates == null) return;
            
            // Serialize
            track.LoopCandidatesJson = _loopAnalysisService.SerializeLoopCandidates(candidates);
            
            // Update DB
            await Task.Run(() => _databaseHelper.UpdateTrackAnalysis(track));
        }

        public async Task AnalyzeTracksAsync(IEnumerable<MusicTrack> tracks, IProgress<(int current, int total, string fileName)> progress = null)
        {
            var trackList = tracks.ToList();
            int total = trackList.Count;
            int current = 0;

            foreach (var track in trackList)
            {
                current++;
                progress?.Report((current, total, track.FileName));

                try
                {
                    // 获取候选点
                    var candidates = await _loopAnalysisService.FetchTopLoopCandidatesAsync(track.FilePath);
                    if (candidates != null && candidates.Any())
                    {
                        // 序列化
                        track.LoopCandidatesJson = _loopAnalysisService.SerializeLoopCandidates(candidates);

                        // 如果当前没设置过循环点 (即 0 -> 总采样数)，则自动应用最佳点
                        if (track.LoopStart == 0 && track.LoopEnd == track.TotalSamples)
                        {
                            var best = candidates.First();
                            track.LoopStart = best.LoopStart;
                            track.LoopEnd = best.LoopEnd;
                        }

                        // 保存到数据库
                        await Task.Run(() => _databaseHelper.UpdateTrackAnalysis(track));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BatchAnalysis Error] {track.FileName}: {ex.Message}");
                }
            }
        }

        private static readonly string[] SupportedExtensions = { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".wma", ".aiff", ".opus" };

        public List<string> GetMusicFolders() => _databaseHelper.GetMusicFolders();
        public void AddMusicFolder(string folderPath) => _databaseHelper.AddMusicFolder(folderPath);
        public void RemoveMusicFolder(string folderPath) => _databaseHelper.RemoveMusicFolder(folderPath);

        public async Task ScanMusicFoldersAsync()
        {
            var folders = GetMusicFolders();
            var existingTracks = _databaseHelper.GetAllTracks().ToDictionary(t => t.FilePath, t => t);
            
            var allFiles = folders.Where(System.IO.Directory.Exists)
                .SelectMany(folder => System.IO.Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Distinct()
                .ToList();

            // --- A/B 融合探测与过滤 ---
            var filesToIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var abPairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // A -> B

            foreach (var f in allFiles)
            {
                string partB = _metadataService.FindPartB(f);
                if (!string.IsNullOrEmpty(partB) && allFiles.Contains(partB, StringComparer.OrdinalIgnoreCase))
                {
                    filesToIgnore.Add(partB);
                    abPairs[f] = partB;
                }
            }

            var filteredFiles = allFiles.Where(f => !filesToIgnore.Contains(f)).ToList();

            var filesToScan = filteredFiles.Where(f => 
                !existingTracks.TryGetValue(f, out var existing) || 
                System.IO.File.GetLastWriteTime(f) > existing.LastModified ||
                (abPairs.ContainsKey(f) && existing.LoopStart == 0) // 如果是 A/B 但之前没设置过循环点，也重新扫一次
            ).ToList();

            if (filesToScan.Count == 0) return;

            var newOrUpdatedTracks = await Task.Run(() => 
                filesToScan.AsParallel()
                .WithDegreeOfParallelism(Environment.ProcessorCount)
                .Select(f => CreateTrackFromFile(f, abPairs.ContainsKey(f) ? abPairs[f] : null))
                .Where(t => t != null)
                .ToList()
            );

            if (newOrUpdatedTracks.Count > 0)
            {
                _databaseHelper.BulkInsert(newOrUpdatedTracks);
            }
        }

        private MusicTrack CreateTrackFromFile(string filePath, string partBPath = null)
        {
            try
            {
                using var audioFile = TagLib.File.Create(filePath);
                long samplesA = (long)(audioFile.Properties.AudioSampleRate * audioFile.Properties.Duration.TotalSeconds);
                long totalSamples = samplesA;
                long loopStart = 0;

                if (!string.IsNullOrEmpty(partBPath) && System.IO.File.Exists(partBPath))
                {
                    using var partBFile = TagLib.File.Create(partBPath);
                    long samplesB = (long)(partBFile.Properties.AudioSampleRate * partBFile.Properties.Duration.TotalSeconds);
                    totalSamples += samplesB;
                    loopStart = samplesA; // 默认循环起点设在衔接处
                }

                var track = new MusicTrack
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    DisplayName = !string.IsNullOrEmpty(audioFile.Tag.Title) ? audioFile.Tag.Title : Path.GetFileNameWithoutExtension(filePath),
                    Artist = audioFile.Tag.FirstPerformer,
                    Album = audioFile.Tag.Album,
                    AlbumArtist = audioFile.Tag.FirstAlbumArtist,
                    TotalSamples = totalSamples,
                    LoopStart = loopStart,
                    LoopEnd = totalSamples,
                    LastModified = System.IO.File.GetLastWriteTime(filePath)
                };

                // 获取封面
                track.CoverPath = _metadataService.GetOrExtractCover(track);

                return track;
            }
            catch
            {
                return null;
            }
        }

        public async Task<(int tracks, int playlists)> SyncDatabaseAsync(string externalDbPath)
        {
            if (string.IsNullOrEmpty(externalDbPath) || !System.IO.File.Exists(externalDbPath))
                return (0, 0);

            return await Task.Run(() => _databaseHelper.SyncWithExternalDatabase(externalDbPath));
        }

        public void Dispose() { }
    }
}
