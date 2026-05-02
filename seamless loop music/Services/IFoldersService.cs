using System.Collections.Generic;
using System.Threading.Tasks;
using seamless_loop_music.Models;

namespace seamless_loop_music.Services
{
    public interface IFoldersService
    {
        Task<List<SubfolderItem>> GetRootFoldersAsync();
        Task<List<SubfolderItem>> GetSubfoldersAsync(string parentPath);
        IEnumerable<SubfolderItem> GetBreadcrumbs(string currentPath);
    }
}
