using seamless_loop_music.Models;
using seamless_loop_music.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace seamless_loop_music.UI
{
    public partial class LoopListWindow : Window
    {
        private IPlayerService _playerService;
        private List<LoopCandidate> _candidates;
        private Func<Task<bool>> _checker;

        public LoopListWindow(List<LoopCandidate> candidates, IPlayerService service, Func<Task<bool>> checker = null)
        {
            InitializeComponent();
            _playerService = service;
            _candidates = candidates;
            _checker = checker;
            
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
            try
            {
                btnUpdate.IsEnabled = false;
                
                if (_checker != null && !await _checker()) return;

                var newCandidates = await _playerService.GetLoopCandidatesAsync(forceRefresh: true);
                if (newCandidates != null) RefreshListView(newCandidates);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Update failed: " + ex.Message);
            }
            finally
            {
                btnUpdate.IsEnabled = true;
            }
        }

        private void LstCandidates_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (lstCandidates.SelectedItem is LoopCandidateViewModel vm)
            {
                _playerService.ApplyLoopCandidate(vm.Candidate);
                this.DialogResult = true;
                this.Close();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public class LoopCandidateViewModel
    {
        public LoopCandidate Candidate { get; }
        private int _rate;

        public LoopCandidateViewModel(LoopCandidate c, int sampleRate)
        {
            Candidate = c;
            _rate = sampleRate;
        }

        public string ScoreDisplay => Candidate.Score.ToString("P1");
        public long Start => Candidate.LoopStart;
        public long End => Candidate.LoopEnd;
        public string Duration => _rate > 0 ? ((Candidate.LoopEnd - Candidate.LoopStart) / (double)_rate).ToString("F2") + "s" : "";
        public string NoteDistance => Candidate.NoteDifference.ToString("F3");
    }
}
