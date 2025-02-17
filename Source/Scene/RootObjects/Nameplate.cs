using Godot;
using System;

namespace Aquamarine.Source.Scene.UI
{
    public partial class Nameplate : Node3D
    {
        private SubViewport _viewport;
        private Sprite3D _sprite3D;
        private Label _label;
        private float _maxWidth = 1.0f; // Max width in 3D units

        public override void _Ready()
        {
            _viewport = GetNode<SubViewport>("SubViewport");
            _sprite3D = GetNode<Sprite3D>("Sprite3D");
            _label = GetNode<Label>("SubViewport/NameplateContainer/Panel/Label");

            // High res viewport
            _viewport.Size = new Vector2I(512, 128);

            // Set up the viewport texture
            _sprite3D.Texture = _viewport.GetTexture();

            // Force fixed scale
            _sprite3D.PixelSize = 0.002f; // Smaller pixel size since we're using higher res
            UpdateSpriteSize();
        }

        public void SetText(string newText)
        {
            if (_label != null)
            {
                _label.Text = newText;
                // Clamp the sprite scale after setting text
                ClampScale();
            }
        }

        private void ClampScale()
        {
            if (_sprite3D != null)
            {
                // Keep height at 1.0, clamp width
                Vector3 currentScale = _sprite3D.Scale;
                _sprite3D.Scale = new Vector3(
                    Mathf.Min(currentScale.X, _maxWidth),
                    1,
                    1
                );
            }
        }

        private void UpdateSpriteSize()
        {
            if (_viewport != null && _sprite3D != null)
            {
                Vector2 viewportSize = _viewport.Size;
                _sprite3D.Scale = new Vector3(
                    viewportSize.X / viewportSize.Y,
                    1,
                    1
                );
                ClampScale();
            }
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
        }
    }
}
