using System;
using System.IO;
using NAudio.Wave;
using NUnit.Framework;
using Prism.Events;
using Unity;
using Unity.Lifetime;
using seamless_loop_music.Data;
using seamless_loop_music.Data.Repositories;
using seamless_loop_music.Models;
using seamless_loop_music.Services;
using seamless_loop_music.Services.Sync;

namespace SeamlessLoop.Tests
{
    [TestFixture]
    public class PlaybackServiceDependencyInjectionTests
    {
        private string _dbPath = null!;

        [Test]
        public void UnityResolvesIPlaybackServiceWithoutStatisticsProviderRegistrations()
        {
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"PlaybackServiceDi_{Guid.NewGuid()}.db");
            var database = new DatabaseHelper(_dbPath);
            database.InitializeDatabase();
            var events = new EventAggregator();
            var trackRepository = new TrackRepository(_dbPath);
            var playlistRepository = new PlaylistRepository(_dbPath);
            var playlistManager = new PlaylistManager(playlistRepository, trackRepository, events);
            var statistics = new PlaybackStatisticsLocalService(
                database,
                new PlaybackStatisticsSyncRepository(_dbPath));

            using (var container = new UnityContainer())
            {
                container.RegisterInstance<IDatabaseHelper>(database);
                container.RegisterInstance<ITrackRepository>(trackRepository);
                container.RegisterInstance<IPlaylistManager>(playlistManager);
                container.RegisterInstance<IEventAggregator>(events);
                container.RegisterInstance<IQueueManager>(new QueueManager());
                container.RegisterInstance<TrackMetadataService>(new TrackMetadataService(database, events));
                container.RegisterInstance<IPlaybackStatisticsLocalService>(statistics);
                container.RegisterType<IPlaybackService, PlaybackService>(new ContainerControlledLifetimeManager());
                container.RegisterType<ISyncSnapshotStore, SQLiteSyncSnapshotStore>(new ContainerControlledLifetimeManager());
                container.RegisterType<IGitHubSyncPreparationService, GitHubSyncPreparationService>(new ContainerControlledLifetimeManager());

                Assert.That(container.IsRegistered<Func<PlaybackState>>(), Is.False);
                Assert.That(container.IsRegistered<Func<MusicTrack>>(), Is.False);

                var resolvedPlayback = container.Resolve<IPlaybackService>();
                var resolvedPreparation = container.Resolve<IGitHubSyncPreparationService>();
                var resolvedPlaybackAgain = container.Resolve<IPlaybackService>();

                Assert.That(resolvedPlayback, Is.TypeOf<PlaybackService>());
                Assert.That(resolvedPlaybackAgain, Is.SameAs(resolvedPlayback));
                Assert.That(resolvedPreparation, Is.Not.Null);
                var snapshot = resolvedPreparation.CaptureFreshLocalSnapshotAsync().GetAwaiter().GetResult();
                Assert.That(snapshot, Is.Not.Null);
                Assert.That(snapshot.SchemaVersion, Is.EqualTo(2));
                resolvedPlayback.Dispose();
            }
        }

        [TearDown]
        public void TearDown()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }
    }
}
