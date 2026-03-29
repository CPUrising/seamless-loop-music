using System;

namespace seamless_loop_music.Services
{
    public interface ISearchService
    {
        string SearchText { get; set; }
        event Action<string> DoSearch;
    }
}
