using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aquamarine.Source.Management.HUD
{
    public partial class LoadingScreen : Control
    {
        [Export] private ProgressBar _progressBar;
        [Export] private Label _statusLabel;
        [Export] private AnimationPlayer _animationPlayer;

        private bool _isShown = false;

        public override void _Ready()
        {
            Visible = false;
        }

        public void Show(string statusText = "LoadingWorld...")
        {
            if (_isShown) return;

            _statusLabel.Text = statusText;
            _progressBar.Value = 0;
            Visible = true;

            _animationPlayer.Play("FadeIn");
            _isShown = true;
        }

        public void Hide()
        {
            if (!_isShown) return;

            _animationPlayer.Play("FadeOut");
            _animationPlayer.AnimationFinished += OnFadeOutFinished;
            _isShown = false;
        }

        private void OnFadeOutFinished(StringName animName)
        {
            if (animName == "FadeOut")
            {
                Visible = false;
                _animationPlayer.AnimationFinished -= OnFadeOutFinished;
            }
        }

        public void UpdateProgress(float progress)
        {
            _progressBar.Value = progress * 100;
        }
    }
}
