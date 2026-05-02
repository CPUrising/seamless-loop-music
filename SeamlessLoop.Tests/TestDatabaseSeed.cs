using System;
using System.Collections.Generic;
using seamless_loop_music.Models;

namespace SeamlessLoop.Tests
{
    public static class TestDatabaseSeed
    {
        /// <summary>
        /// 提供一套"黄金数据集"，包含各种复杂关联和极端路径情况
        /// </summary>
        public static List<MusicTrack> GetGoldenTracks()
        {
            return new List<MusicTrack>
            {
                // 1. 标准场景
                new MusicTrack { 
                    FileName = "Standard_Track.mp3", 
                    FilePath = @"C:\Music\Standard_Track.mp3",
                    DisplayName = "Normal Song",
                    Artist = "Normal Artist",
                    Album = "Normal Album",
                    TotalSamples = 1000000,
                    LoopStart = 1000,
                    LoopEnd = 900000,
                    Rating = 4
                },

                // 2. 重名专辑场景 (不同艺术家)
                new MusicTrack { 
                    FileName = "Pop_Greatest.mp3", 
                    FilePath = @"C:\Music\Pop_Greatest.mp3",
                    DisplayName = "Pop Hit",
                    Artist = "Pop Star",
                    Album = "Greatest Hits", // 重名专辑名
                    TotalSamples = 800000,
                    LoopStart = 500,
                    LoopEnd = 790000
                },
                new MusicTrack { 
                    FileName = "Rock_Greatest.mp3", 
                    FilePath = @"C:\Music\Rock_Greatest.mp3",
                    DisplayName = "Rock Anthem",
                    Artist = "Rock Legend",
                    Album = "Greatest Hits", // 重名专辑名
                    TotalSamples = 900000,
                    LoopStart = 2000,
                    LoopEnd = 880000
                },

                // 3. 特殊字符与极端路径场景
                new MusicTrack { 
                    FileName = "Special_#%$_Name.wav", 
                    FilePath = @"D:\Music\Folder#1\Song_With_Special_Chars_!@#.wav",
                    DisplayName = "Special Characters",
                    Artist = "Artist_With_#",
                    Album = "Album_With_$",
                    TotalSamples = 1200000,
                    LoopStart = 0,
                    LoopEnd = 1200000
                },

                // 4. A/B 拼接流场景 (模拟)
                new MusicTrack { 
                    FileName = "Concatenated_Part_A.flac", 
                    FilePath = @"E:\Music\LongSuite_Part_A.flac",
                    DisplayName = "Long Suite",
                    Artist = "Prog Rocker",
                    Album = "Infinite Loop",
                    TotalSamples = 5000000, // 总采样数包含 B 部分
                    LoopStart = 2500000,   // 循环点在衔接处
                    LoopEnd = 5000000
                }
            };
        }

        /// <summary>
        /// 生成万级测试数据的快捷工具
        /// </summary>
        public static IEnumerable<MusicTrack> GenerateMassiveData(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new MusicTrack
                {
                    FileName = $"Track_{i}.mp3",
                    FilePath = $@"F:\MassiveLibrary\Track_{i}.mp3",
                    DisplayName = $"Track Title {i}",
                    Artist = $"Artist {i % 100}", // 100 个歌手
                    Album = $"Album {i % 500}",   // 500 个专辑
                    TotalSamples = 100000 + i,
                    LoopStart = 0,
                    LoopEnd = 100000 + i,
                    LastModified = DateTime.Now
                };
            }
        }
    }
}
