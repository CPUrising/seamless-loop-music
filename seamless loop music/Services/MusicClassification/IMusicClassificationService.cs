using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services.MusicClassification
{
    /// <summary>
    /// 音乐节奏/情绪轻量分类服务：纯信号处理特征 + 透明规则四象限分类。
    /// 结果仅用于展示，不写入数据库。
    /// </summary>
    public interface IMusicClassificationService
    {
        /// <summary>
        /// 对一个文件夹内的音频文件批量分类（仅展示，不落库）。
        /// </summary>
        /// <param name="folderPath">目标文件夹。</param>
        /// <param name="recursive">是否递归子目录。</param>
        /// <param name="progress">进度回调（当前序号/总数/当前文件名）。</param>
        /// <param name="ct">取消令牌。</param>
        Task<List<TrackClassificationResult>> ClassifyFolderAsync(
            string folderPath,
            bool recursive,
            System.IProgress<(int current, int total, string fileName)> progress,
            CancellationToken ct);
    }
}
