using Windows.Media;
using Windows.Media.Control;

namespace NanoShell.Services;

public sealed class MediaPlaybackService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public MediaPlaybackService()
    {
        try
        {
            var task = Task.Run(async () =>
                await GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            if (task.Wait(5000))
                _manager = task.Result;
        }
        catch
        {
            _manager = null;
        }
    }

    public bool IsVideoPlaying()
    {
        if (_manager is null) return false;

        try
        {
            var sessions = _manager.GetSessions();
            foreach (var session in sessions)
            {
                var info = session.GetPlaybackInfo();
                if (info is null) continue;
                if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing &&
                    info.PlaybackType == MediaPlaybackType.Video)
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }
}
