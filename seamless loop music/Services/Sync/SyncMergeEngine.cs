using System;
using System.Collections.Generic;
using System.Linq;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    /// <summary>
    /// Merges two SyncSnapshot instances with last-writer-wins (LWW) semantics.
    /// </summary>
    public static class SyncMergeEngine
    {
        /// <summary>
        /// Merge two snapshots. The result is a new snapshot that can be applied.
        /// baseSnapshot is the local state; incomingSnapshot is the remote/phone state.
        /// </summary>
        public static SyncMergeResult Merge(SyncSnapshot baseSnapshot, SyncSnapshot incomingSnapshot)
        {
            if (baseSnapshot == null)
                throw new ArgumentNullException(nameof(baseSnapshot));
            if (incomingSnapshot == null)
                throw new ArgumentNullException(nameof(incomingSnapshot));

            if (baseSnapshot.SchemaVersion != 2)
                throw new FormatException($"Unsupported schemaVersion: {baseSnapshot.SchemaVersion}. Expected: 2.");
            if (incomingSnapshot.SchemaVersion != 2)
                throw new FormatException($"Unsupported schemaVersion: {incomingSnapshot.SchemaVersion}. Expected: 2.");

            var conflicts = new List<SyncMergeConflict>();
            var merged = new SyncSnapshot
            {
                SchemaVersion = 2,
                DeviceId = baseSnapshot.DeviceId ?? incomingSnapshot.DeviceId,
                ExportedAt = Math.Max(baseSnapshot.ExportedAt, incomingSnapshot.ExportedAt),
                Playlists = new List<SyncPlaylist>(),
                LoopPoints = new List<SyncLoopPointEntry>(),
                Ratings = new List<SyncRatingEntry>(),
                PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Empty()
            };

            // ── 1. Merge loop points ────────────────────────────────────────
            MergeLoopPoints(baseSnapshot.LoopPoints, incomingSnapshot.LoopPoints, merged.LoopPoints, conflicts);

            // ── 2. Merge ratings ────────────────────────────────────────────
            MergeRatings(baseSnapshot.Ratings, incomingSnapshot.Ratings, merged.Ratings, conflicts);

            // ── 3. Merge playlists ──────────────────────────────────────────
            MergePlaylists(baseSnapshot.Playlists, incomingSnapshot.Playlists, merged.Playlists, conflicts);

            merged.PlaybackStatistics = PlaybackStatisticsSyncCanonicalizer.Merge(
                baseSnapshot.PlaybackStatistics,
                incomingSnapshot.PlaybackStatistics);

            return new SyncMergeResult { Merged = merged, Conflicts = conflicts };
        }

        // ────────────────────────────────────────────────────────────────
        //  Merge helpers
        // ────────────────────────────────────────────────────────────────

        private static void MergeLoopPoints(
            List<SyncLoopPointEntry> baseList,
            List<SyncLoopPointEntry> incomingList,
            List<SyncLoopPointEntry> result,
            List<SyncMergeConflict> conflicts)
        {
            baseList = baseList ?? new List<SyncLoopPointEntry>();
            incomingList = incomingList ?? new List<SyncLoopPointEntry>();
            var baseById = baseList.ToDictionary(e => GetIdentityKey(e.Song));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var incoming in incomingList)
            {
                var key = GetIdentityKey(incoming.Song);
                seen.Add(key);

                if (!baseById.TryGetValue(key, out var baseEntry))
                {
                    // New entry from incoming
                    result.Add(Clone(incoming));
                    continue;
                }

                // Both present — resolve (pass song totalSamples context)
                    long? songTotalSamples = incoming.Song?.TotalSamples ?? baseEntry.Song?.TotalSamples;
                    var resolved = ResolveLoopPoint(baseEntry.LoopPoint, incoming.LoopPoint, songTotalSamples, conflicts);
                if (resolved != null)
                    result.Add(new SyncLoopPointEntry
                    {
                        Song = Clone(incoming.Song),
                        LoopPoint = Clone(resolved)
                    });
            }

            // Add base entries not in incoming
            foreach (var kvp in baseById)
            {
                if (!seen.Contains(kvp.Key))
                    result.Add(Clone(kvp.Value));
            }
        }

        private static SyncLoopPoint ResolveLoopPoint(
            SyncLoopPoint baseLp, SyncLoopPoint incomingLp,
            long? songTotalSamples,
            List<SyncMergeConflict> conflicts)
        {
            bool baseSubstantial = IsSubstantialLoop(baseLp, songTotalSamples);
            bool incomingSubstantial = IsSubstantialLoop(incomingLp, songTotalSamples);

            if (!baseSubstantial && !incomingSubstantial)
            {
                // Both unset — keep whichever has lastModified (export will skip anyway)
                return incomingLp.LastModified >= baseLp.LastModified ? incomingLp : baseLp;
            }

            if (!baseSubstantial && incomingSubstantial)
                return incomingLp; // incoming has data, base doesn't

            if (baseSubstantial && !incomingSubstantial)
                return baseLp; // base has data, incoming unset can't overwrite

            // Both substantial — LWW
            if (incomingLp.LastModified >= baseLp.LastModified)
                return incomingLp;

            conflicts.Add(new SyncMergeConflict
            {
                Field = "loopPoint",
                Description = $"Keeping local loopPoint: localLastModified={baseLp.LastModified} > incomingLastModified={incomingLp.LastModified}"
            });
            return baseLp;
        }

        private static void MergeRatings(
            List<SyncRatingEntry> baseList,
            List<SyncRatingEntry> incomingList,
            List<SyncRatingEntry> result,
            List<SyncMergeConflict> conflicts)
        {
            baseList = baseList ?? new List<SyncRatingEntry>();
            incomingList = incomingList ?? new List<SyncRatingEntry>();
            var baseById = baseList.ToDictionary(e => GetIdentityKey(e.Song));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var incoming in incomingList)
            {
                var key = GetIdentityKey(incoming.Song);
                seen.Add(key);

                if (!baseById.TryGetValue(key, out var baseEntry))
                {
                    result.Add(Clone(incoming));
                    continue;
                }

                var resolved = ResolveRating(baseEntry.Rating, incoming.Rating, conflicts);
                if (resolved != null)
                    result.Add(new SyncRatingEntry
                    {
                        Song = Clone(incoming.Song),
                        Rating = Clone(resolved)
                    });
            }

            foreach (var kvp in baseById)
            {
                if (!seen.Contains(kvp.Key))
                    result.Add(Clone(kvp.Value));
            }
        }

        private static SyncRating ResolveRating(
            SyncRating baseRating, SyncRating incomingRating,
            List<SyncMergeConflict> conflicts)
        {
            bool baseNonZero = baseRating.RatingValue != 0;
            bool incomingNonZero = incomingRating.RatingValue != 0;

            if (!baseNonZero && !incomingNonZero)
            {
                // Both zero — keep whichever has lastModified (export skips zero anyway)
                return incomingRating.LastModified >= baseRating.LastModified ? incomingRating : baseRating;
            }

            if (!baseNonZero && incomingNonZero)
                return incomingRating; // incoming has rating, base doesn't

            if (baseNonZero && !incomingNonZero)
                return baseRating; // base has rating, incoming zero can't overwrite

            // Both non-zero — LWW
            if (incomingRating.LastModified >= baseRating.LastModified)
                return incomingRating;

            conflicts.Add(new SyncMergeConflict
            {
                Field = "rating",
                Description = $"Keeping local rating: localLastModified={baseRating.LastModified} > incomingLastModified={incomingRating.LastModified}"
            });
            return baseRating;
        }

        private static void MergePlaylists(
            List<SyncPlaylist> baseList,
            List<SyncPlaylist> incomingList,
            List<SyncPlaylist> result,
            List<SyncMergeConflict> conflicts)
        {
            baseList = baseList ?? new List<SyncPlaylist>();
            incomingList = incomingList ?? new List<SyncPlaylist>();
            var baseById = new Dictionary<string, SyncPlaylist>(StringComparer.OrdinalIgnoreCase);
            foreach (var pl in baseList)
                if (!string.IsNullOrEmpty(pl.Id))
                    baseById[pl.Id] = pl;

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var incoming in incomingList)
            {
                if (string.IsNullOrEmpty(incoming.Id)) continue;

                seenIds.Add(incoming.Id);

                if (!baseById.TryGetValue(incoming.Id, out var basePl))
                {
                    // New playlist from incoming
                    result.Add(Clone(incoming));
                    continue;
                }

                // Merge same-id playlists
                var merged = MergeSinglePlaylist(basePl, incoming, conflicts);
                result.Add(merged);
            }

            // Add base playlists not in incoming
            foreach (var kvp in baseById)
            {
                if (!seenIds.Contains(kvp.Key))
                    result.Add(Clone(kvp.Value));
            }
        }

        private static SyncPlaylist MergeSinglePlaylist(
            SyncPlaylist basePl, SyncPlaylist incomingPl,
            List<SyncMergeConflict> conflicts)
        {
            // Metadata LWW by ModifiedAt
            SyncPlaylist winner, loser;
            if (incomingPl.ModifiedAt >= basePl.ModifiedAt)
            {
                winner = incomingPl;
                loser = basePl;
            }
            else
            {
                winner = basePl;
                loser = incomingPl;
                conflicts.Add(new SyncMergeConflict
                {
                    Field = "playlist",
                    Description = $"Keeping local playlist '{basePl.Name}': localModifiedAt={basePl.ModifiedAt} > incomingModifiedAt={incomingPl.ModifiedAt}"
                });
            }

            // Items: start with winner's items, then append loser songs not in winner
            var winnerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in winner.Items ?? Enumerable.Empty<SyncPlaylistItem>())
                if (item.Song != null)
                    winnerKeys.Add(GetIdentityKey(item.Song));

            var mergedItems = (winner.Items ?? new List<SyncPlaylistItem>()).Select(Clone).ToList();

            if (loser.Items != null)
            {
                foreach (var item in loser.Items)
                {
                    if (item.Song != null && !winnerKeys.Contains(GetIdentityKey(item.Song)))
                    {
                        mergedItems.Add(new SyncPlaylistItem
                        {
                            Song = Clone(item.Song),
                            SortOrder = mergedItems.Count // appended
                        });
                    }
                }
            }

            // Prefer incoming song identity for stability (avoid totalSamples jitter)
            var incomingSongById = new Dictionary<string, SyncSongIdentity>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in incomingPl.Items ?? Enumerable.Empty<SyncPlaylistItem>())
                if (item.Song != null)
                    incomingSongById[GetIdentityKey(item.Song)] = item.Song;

            for (int i = 0; i < mergedItems.Count; i++)
            {
                if (mergedItems[i].Song != null)
                {
                    var key = GetIdentityKey(mergedItems[i].Song);
                    if (incomingSongById.TryGetValue(key, out var incomingSong))
                    {
                        // Keep incoming totalSamples, use merged item's sortOrder
                        mergedItems[i] = new SyncPlaylistItem
                        {
                            Song = Clone(incomingSong),
                            SortOrder = mergedItems[i].SortOrder
                        };
                    }
                }
            }

            // Re-normalize sortOrder from 0
            for (int i = 0; i < mergedItems.Count; i++)
                mergedItems[i].SortOrder = i;

            return new SyncPlaylist
            {
                Id = winner.Id,
                Name = winner.Name,
                CreatedAt = Math.Min(basePl.CreatedAt, incomingPl.CreatedAt),
                ModifiedAt = Math.Max(basePl.ModifiedAt, incomingPl.ModifiedAt),
                Items = mergedItems
            };
        }

        // ────────────────────────────────────────────────────────────────
        //  Utilities
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the loop point has a meaningful non-default value.
        /// Both 0/0 and 0→totalSamples (full-track default) are treated as unset.
        /// </summary>
        public static bool IsSubstantialLoop(SyncLoopPoint lp, long? songTotalSamples = null)
        {
            if (lp == null) return false;
            if (lp.LoopStart == 0 && lp.LoopEnd == 0) return false;
            // 0→TotalSamples is the desktop default / unset
            if (songTotalSamples.HasValue && songTotalSamples.Value > 0 &&
                lp.LoopStart == 0 && lp.LoopEnd == songTotalSamples.Value)
                return false;
            return true;
        }

        /// <summary>
        /// Build an identity key for song dedup.
        /// Prefer fileName.lower + durationMs, then fileName.lower + totalSamples, then just fileName.lower.
        /// </summary>
        public static string GetIdentityKey(SyncSongIdentity song)
        {
            if (song == null) return string.Empty;
            var fn = (song.FileName ?? "").ToLowerInvariant();
            if (song.DurationMs > 0)
                return $"{fn}|dur:{song.DurationMs}";
            if (song.TotalSamples.HasValue && song.TotalSamples.Value > 0)
                return $"{fn}|smp:{song.TotalSamples.Value}";
            return fn;
        }

        private static SyncLoopPointEntry Clone(SyncLoopPointEntry value)
        {
            return value == null ? null : new SyncLoopPointEntry
            {
                Song = Clone(value.Song),
                LoopPoint = Clone(value.LoopPoint)
            };
        }

        private static SyncRatingEntry Clone(SyncRatingEntry value)
        {
            return value == null ? null : new SyncRatingEntry
            {
                Song = Clone(value.Song),
                Rating = Clone(value.Rating)
            };
        }

        private static SyncPlaylist Clone(SyncPlaylist value)
        {
            return value == null ? null : new SyncPlaylist
            {
                Id = value.Id,
                Name = value.Name,
                CreatedAt = value.CreatedAt,
                ModifiedAt = value.ModifiedAt,
                Items = (value.Items ?? new List<SyncPlaylistItem>()).Select(Clone).ToList()
            };
        }

        private static SyncPlaylistItem Clone(SyncPlaylistItem value)
        {
            return value == null ? null : new SyncPlaylistItem
            {
                Song = Clone(value.Song),
                SortOrder = value.SortOrder
            };
        }

        private static SyncSongIdentity Clone(SyncSongIdentity value)
        {
            return value == null ? null : new SyncSongIdentity
            {
                FileName = value.FileName,
                DurationMs = value.DurationMs,
                TotalSamples = value.TotalSamples
            };
        }

        private static SyncLoopPoint Clone(SyncLoopPoint value)
        {
            return value == null ? null : new SyncLoopPoint
            {
                LoopStart = value.LoopStart,
                LoopEnd = value.LoopEnd,
                LastModified = value.LastModified
            };
        }

        private static SyncRating Clone(SyncRating value)
        {
            return value == null ? null : new SyncRating
            {
                RatingValue = value.RatingValue,
                LastModified = value.LastModified
            };
        }
    }
}
