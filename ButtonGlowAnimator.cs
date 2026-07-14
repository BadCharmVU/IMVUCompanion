using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace IMVUCompanion;

internal sealed class ButtonGlowAnimator
{
    private readonly Button _button;
    private DispatcherTimer? _timer;
    private RotateTransform? _rotate;
    private double _angle;

    public ButtonGlowAnimator(Button button) => _button = button;

    public void SetActive(bool active)
    {
        if (_button == null) return;
        _button.ApplyTemplate();
        _rotate ??= _button.Template.FindName("BotGlowRotate", _button) as RotateTransform;
        var glowRing = _button.Template.FindName("GlowRing", _button) as UIElement;

        if (active)
        {
            if (glowRing != null) glowRing.Visibility = Visibility.Visible;
            _timer?.Stop();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _timer.Tick += (_, _) =>
            {
                if (_rotate == null) return;
                _angle = (_angle + 6) % 360;
                _rotate.Angle = _angle;
            };
            _timer.Start();
        }
        else
        {
            _timer?.Stop();
            _timer = null;
            if (glowRing != null) glowRing.Visibility = Visibility.Collapsed;
            if (_rotate != null) _rotate.Angle = 0;
            _angle = 0;
        }
    }

    public void Stop() => SetActive(false);
}