using System.Windows.Controls;
using System.Windows.Threading;

namespace SeaImageViewer;

public sealed class AnimatedImagePlayer
{
    private static readonly TimeSpan DefaultFrameDelay = TimeSpan.FromMilliseconds(100);

    private readonly Image _target;
    private readonly DispatcherTimer _timer;
    private AnimatedImage? _animation;
    private int _frameIndex;

    public AnimatedImagePlayer(Image target)
    {
        _target = target;
        _timer = new DispatcherTimer(DispatcherPriority.Render, target.Dispatcher);
        _timer.Tick += Timer_Tick;
    }

    public void Start(AnimatedImage animation)
    {
        Stop();

        if (animation.Frames.Count == 0)
        {
            return;
        }

        _animation = animation;
        _frameIndex = 0;
        _target.Source = animation.Frames[0];

        if (animation.Frames.Count == 1)
        {
            return;
        }

        _timer.Interval = GetFrameDelay(0);
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _animation = null;
        _frameIndex = 0;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_animation is null || _animation.Frames.Count == 0)
        {
            Stop();
            return;
        }

        _frameIndex = (_frameIndex + 1) % _animation.Frames.Count;
        _target.Source = _animation.Frames[_frameIndex];
        _timer.Interval = GetFrameDelay(_frameIndex);
    }

    private TimeSpan GetFrameDelay(int frameIndex)
    {
        return _animation is not null && frameIndex < _animation.Delays.Count
            ? _animation.Delays[frameIndex]
            : DefaultFrameDelay;
    }
}
