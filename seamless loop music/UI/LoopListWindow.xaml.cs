using seamless_loop_music.Models;
using seamless_loop_music.Services;
using System.Collections.Generic;
using System.Windows;
using System.Threading.Tasks;

namespace seamless_loop_music.UI
{
    public partial class LoopListWindow : Window
    {
        private PlayerService _playerService;
        private List<LoopCandidate> _candidates;
        private System.Func<System.Threading.Tasks.Task<bool>> _checker;
        
        public class LoopCandidateViewModel
        {
            public LoopCandidate Candidate { get; }
            public string ScoreDisplay => Candidate.Score.ToString("P1");
            public long Start => Candidate.LoopStart;
            public long End => Candidate.LoopEnd;
            public string Duration => _rate > 0 ? ((Candidate.LoopEnd - Candidate.LoopStart) / (double)_rate).ToString("F2") : "?";
            public string NoteDistance => Candidate.NoteDifference.ToString("F3");
            
            private int _rate;
            
            public LoopCandidateViewModel(LoopCandidate c, int sampleRate)
            {
                Candidate = c;
                _rate = sampleRate;
            }
        }

        public LoopListWindow(List<LoopCandidate> candidates, PlayerService service, System.Func<System.Threading.Tasks.Task<bool>> checker = null)
        {
            InitializeComponent();
            _playerService = service;
            _candidates = candidates;
            _checker = checker;
            
            if (btnUpdate != null) {
                btnUpdate.ToolTip = LocalizationService.Instance["ToolTipRecalculate"];
            }

            RefreshListView(candidates);
        }

        private void RefreshListView(List<LoopCandidate> candidates)
        {
            int rate = _playerService.SampleRate;
            if (rate <= 0) rate = 44100;

            var list = new List<LoopCandidateViewModel>();
            foreach (var c in candidates)
            {
                list.Add(new LoopCandidateViewModel(c, rate));
            }
            lstCandidates.ItemsSource = list;
        }

        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
             string originalText = btnUpdate.Content?.ToString() ?? "Update";
             try
            {
                if (btnUpdate != null) {
                    btnUpdate.IsEnabled = false;
                    btnUpdate.Content = LocalizationService.Instance["StatusUpdating"];
                }
                
                // 环境预检
                if (_checker != null && !await _checker()) return;

                // 强制刷新
                var newCandidates = await _playerService.GetLoopCandidatesAsync(forceRefresh: true);
                RefreshListView(newCandidates);
                
                if (btnUpdate != null) btnUpdate.Content = "√";
                await System.Threading.Tasks.Task.Delay(500);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Update failed: " + ex.Message);
            }
            finally
            {
                 if (btnUpdate != null) {
                     btnUpdate.IsEnabled = true;
                     btnUpdate.Content = originalText;
                 }
            }
        }

        private void LstCandidates_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (lstCandidates.SelectedItem is LoopCandidateViewModel vm)
            {
                _playerService.ApplyLoopCandidate(vm.Candidate);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
