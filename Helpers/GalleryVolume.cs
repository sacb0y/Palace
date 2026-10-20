namespace Palace.Helpers;

/// <summary>
/// Video / audio volume chrome: mute remembers prior level, unmute-from-0
/// restores 50%, icon bands, wheel/key deltas, LocalSettings keys. Off WinUI so
/// <see cref="Palace.Controls.VolumeControl"/> and tests share one policy.
/// Reusable for a future audio viewer.
/// </summary>
public static class GalleryVolume
{
    public const string VolumeKey = "GalleryVolume";
    public const string MuteKey = "GalleryVolumeMuted";

    public const int MinVolume = 0;
    public const int MaxVolume = 100;
    public const int DefaultVolume = 100;
    public const int UnmuteFromZeroVolume = 50;
    public const int WheelStep = 5;
    public const int WheelFineStep = 1;
    public const int KeyStep = 5;
    public const int KeyFineStep = 1;

    public const int SliderWidthDip = 100;
    public const int ExpandMs = 150;
    public const int CollapseIdleMs = 600;
    public const int HudVisibleMs = 800;

    public const int KeyLetterM = 77;
    public const int KeyUp = 38;
    public const int KeyDown = 40;
    public const int KeyRight = 39;
    public const int KeyEnter = 13;
    public const int KeySpace = 32;

    /// <summary>Live session level (0–100). Persisted when applied.</summary>
    public static int Volume { get; private set; } = DefaultVolume;

    /// <summary>Live mute. Persisted when applied.</summary>
    public static bool IsMuted { get; private set; }

    /// <summary>Level remembered before the last mute (for unmute restore).</summary>
    public static int VolumeBeforeMute { get; private set; } = DefaultVolume;

    public static event EventHandler? Changed;

    public enum SpeakerIcon
    {
        Mute = 0,
        Level0 = 1,
        Level1 = 2,
        Level2 = 3
    }

    public static void Apply(int volume, bool muted)
    {
        volume = Clamp(volume);
        if (Volume == volume && IsMuted == muted)
        {
            return;
        }

        if (muted && !IsMuted && volume > MinVolume)
        {
            VolumeBeforeMute = volume;
        }

        Volume = volume;
        IsMuted = muted;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Load(object? volumeStored, object? muteStored)
    {
        var volume = ParseVolume(volumeStored);
        var muted = ParseMuted(muteStored);
        VolumeBeforeMute = volume > MinVolume ? volume : UnmuteFromZeroVolume;
        Volume = volume;
        IsMuted = muted;
    }

    public static int ParseVolume(object? stored)
    {
        var value = stored switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)Math.Round(d),
            float f => (int)Math.Round(f),
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => DefaultVolume
        };
        return Clamp(value);
    }

    public static bool ParseMuted(object? stored) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => false
    };

    public static int Clamp(int volume) =>
        Math.Clamp(volume, MinVolume, MaxVolume);

    /// <summary>Effective audible percent (0 when muted).</summary>
    public static int EffectiveVolume(int volume, bool muted) =>
        muted ? MinVolume : Clamp(volume);

    /// <summary>MediaPlayer.Volume is 0..1.</summary>
    public static double ToPlayerVolume(int volume, bool muted) =>
        EffectiveVolume(volume, muted) / 100.0;

    public static (int Volume, bool IsMuted) ToggleMute(int volume, bool muted, int remembered)
    {
        volume = Clamp(volume);
        remembered = remembered > MinVolume ? Clamp(remembered) : UnmuteFromZeroVolume;

        if (!muted)
        {
            var keep = volume > MinVolume ? volume : remembered;
            return (keep, true);
        }

        if (volume <= MinVolume)
        {
            return (UnmuteFromZeroVolume, false);
        }

        return (volume, false);
    }

    public static (int Volume, bool IsMuted) ToggleMute()
    {
        var next = ToggleMute(Volume, IsMuted, VolumeBeforeMute);
        if (!IsMuted && next.IsMuted && Volume > MinVolume)
        {
            VolumeBeforeMute = Volume;
        }

        Apply(next.Volume, next.IsMuted);
        return next;
    }

    public static (int Volume, bool IsMuted) Nudge(int volume, bool muted, int delta)
    {
        volume = Clamp(volume);
        if (delta == 0)
        {
            return (volume, muted);
        }

        if (muted && delta > 0)
        {
            var from = volume > MinVolume ? volume : UnmuteFromZeroVolume;
            return (Clamp(from + delta), false);
        }

        if (muted && delta < 0)
        {
            return (volume, true);
        }

        var next = Clamp(volume + delta);
        if (next <= MinVolume)
        {
            return (MinVolume, true);
        }

        return (next, false);
    }

    public static (int Volume, bool IsMuted) Nudge(int delta)
    {
        var next = Nudge(Volume, IsMuted, delta);
        if (!IsMuted && next.IsMuted && Volume > MinVolume)
        {
            VolumeBeforeMute = Volume;
        }

        Apply(next.Volume, next.IsMuted);
        return next;
    }

    public static int WheelDelta(int notches, bool fine) =>
        notches * (fine ? WheelFineStep : WheelStep);

    public static int KeyDelta(bool up, bool fine)
    {
        var step = fine ? KeyFineStep : KeyStep;
        return up ? step : -step;
    }

    public static SpeakerIcon IconFor(int volume, bool muted)
    {
        var level = EffectiveVolume(volume, muted);
        if (level <= MinVolume)
        {
            return SpeakerIcon.Mute;
        }

        if (level <= 33)
        {
            return SpeakerIcon.Level0;
        }

        if (level <= 66)
        {
            return SpeakerIcon.Level1;
        }

        return SpeakerIcon.Level2;
    }

    public static string MuteButtonName(bool muted) =>
        muted ? "Unmute" : "Mute";

    public static string MuteButtonTooltip(bool muted) =>
        muted ? "Unmute (M)" : "Mute (M)";

    public static string VolumeStatus(int volume, bool muted) =>
        $"Volume {EffectiveVolume(volume, muted)}%";

    public static string SliderTooltip(int volume, bool muted) =>
        $"{EffectiveVolume(volume, muted)}%";

    public static string HudText(int volume, bool muted) =>
        $"{EffectiveVolume(volume, muted)}%";

    public static string MuteNotification(bool muted) =>
        muted ? "Muted" : "Unmuted";

    /// <summary>
    /// Filled slider track: Palace pink when that accent mode is on; else white.
    /// High contrast is handled in UI via theme brushes.
    /// </summary>
    public static bool UsePalacePinkFill(object? accentMode) =>
        BrandAccent.IsPalacePink(accentMode);

    /// <summary>
    /// M / Up / Down on the viewer when a video is showing (not while typing).
    /// Right / Enter / Space on the mute button are handled by the control.
    /// </summary>
    public static bool HandlesViewerKey(bool videoShowing, bool isTyping, int keyCode)
    {
        if (!videoShowing || isTyping)
        {
            return false;
        }

        return keyCode is KeyLetterM or KeyUp or KeyDown;
    }

    /// <summary>
    /// When the mute button (or its pill) owns focus, Left/Right/next-asset must
    /// not steal — Right moves into the slider; Space/Enter mute on the button.
    /// </summary>
    public static bool PassesVolumeFocusKeys(bool volumeChromeFocused, int keyCode) =>
        volumeChromeFocused && keyCode is KeyRight or KeyEnter or KeySpace or KeyUp or KeyDown;

    public static bool IsVolumeChromeAutomationId(string? automationId) =>
        automationId is "BtnGalleryVolume"
            or "BtnGalleryWindowVolume"
            or "SldGalleryVolume"
            or "SldGalleryWindowVolume"
            or "VolGalleryVolume"
            or "VolGalleryWindowVolume";
}
