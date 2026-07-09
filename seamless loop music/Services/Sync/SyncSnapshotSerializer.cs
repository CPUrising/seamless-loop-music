using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using seamless_loop_music.Services.Sync.Models;

namespace seamless_loop_music.Services.Sync
{
    /// <summary>
    /// JSON serializer for SyncSnapshot (phone-compatible schema v1).
    /// Produces indented JSON; deserialization validates invariants.
    /// </summary>
    public static class SyncSnapshotSerializer
    {
        private static readonly JsonSerializerSettings SerializeSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    OverrideSpecifiedNames = false
                }
            },
            NullValueHandling = NullValueHandling.Include
        };

        private static readonly JsonSerializerSettings DeserializeSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    OverrideSpecifiedNames = false
                }
            }
        };

        /// <summary>
        /// Serialize a SyncSnapshot to indented JSON string.
        /// </summary>
        public static string Serialize(SyncSnapshot snapshot)
        {
            return JsonConvert.SerializeObject(snapshot, SerializeSettings);
        }

        /// <summary>
        /// Deserialize JSON into SyncSnapshot with validation.
        /// Throws FormatException if schemaVersion != 1, deviceId missing, or exportedAt < 0.
        /// Null lists are normalized to empty lists.
        /// </summary>
        public static SyncSnapshot Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new FormatException("JSON input is null or empty.");

            var snapshot = JsonConvert.DeserializeObject<SyncSnapshot>(json, DeserializeSettings);

            if (snapshot == null)
                throw new FormatException("Deserialization returned null.");

            // Validate required fields
            if (snapshot.SchemaVersion != 1)
                throw new FormatException(
                    $"Unsupported schemaVersion: {snapshot.SchemaVersion}. Expected: 1.");

            if (string.IsNullOrWhiteSpace(snapshot.DeviceId))
                throw new FormatException("Missing required field: deviceId.");

            if (snapshot.ExportedAt < 0)
                throw new FormatException("exportedAt must be >= 0.");

            // Normalize null lists to empty
            snapshot.Playlists ??= new List<SyncPlaylist>();
            snapshot.LoopPoints ??= new List<SyncLoopPointEntry>();
            snapshot.Ratings ??= new List<SyncRatingEntry>();

            return snapshot;
        }
    }
}
