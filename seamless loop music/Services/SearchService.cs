using System;
using System.Windows.Threading;

namespace seamless_loop_music.Services
{
    public class SearchService : ISearchService
    {
        private string _searchText = string.Empty;
        private DispatcherTimer _timer;
        private const double SearchTimeoutSeconds = 0.4;

        public event Action<string> DoSearch;

        public string SearchText
        {
            get => _searchText ?? string.Empty;
            set
            {
                // Only trigger if trimmed text has changed
                bool isTextChanged = !(_searchText ?? "").Trim().Equals((value ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
                _searchText = value;

                if (isTextChanged)
                {
                    StartSearchTimer();
                }
            }
        }

        private void StartSearchTimer()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer();
                _timer.Interval = TimeSpan.FromSeconds(SearchTimeoutSeconds);
                _timer.Tick += Timer_Tick;
            }
            else
            {
                _timer.Stop();
            }

            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _timer.Stop();
            DoSearch?.Invoke(SearchText);
        }
    }
}
