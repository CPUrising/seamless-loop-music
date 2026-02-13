using seamless_loop_music.Models;
using seamless_loop_music.Services;
using System.Collections.Generic;
using System.Windows;

namespace seamless_loop_music.UI
{
    public partial class LoopListWindow : Window
    {
        private PlayerService _playerService;
        private List<LoopCandidate> _candidates;
        
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

        public LoopListWindow(List<LoopCandidate> candidates, PlayerService service)
        {
            InitializeComponent();
            _playerService = service;
            _candidates = candidates;
            
            int rate = _playerService.SampleRate;
            if (rate <= 0) rate = 44100;

            var list = new List<LoopCandidateViewModel>();
            foreach (var c in candidates)
            {
                list.Add(new LoopCandidateViewModel(c, rate));
            }
            lstCandidates.ItemsSource = list;
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
