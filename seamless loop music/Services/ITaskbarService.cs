using System.Windows.Shell;

namespace seamless_loop_music.Services
{
    public interface ITaskbarService
    {
        void Initialize(TaskbarItemInfo taskbarItemInfo);
    }
}
