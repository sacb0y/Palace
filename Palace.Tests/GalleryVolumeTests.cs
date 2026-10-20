using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class GalleryVolumeTests
{
    public GalleryVolumeTests()
    {
        // Isolate static session state between tests.
        GalleryVolume.Load(GalleryVolume.DefaultVolume, false);
        GalleryVolume.Apply(GalleryVolume.DefaultVolume, false);
    }

    [Fact]
    public void Keys_AreStable()
    {
        Assert.Equal("GalleryVolume", GalleryVolume.VolumeKey);
        Assert.Equal("GalleryVolumeMuted", GalleryVolume.MuteKey);
    }

    [Fact]
    public void ParseVolume_ClampsAndDefaults()
    {
        Assert.Equal(100, GalleryVolume.ParseVolume(null));
        Assert.Equal(100, GalleryVolume.ParseVolume("nope"));
        Assert.Equal(0, GalleryVolume.ParseVolume(-4));
        Assert.Equal(100, GalleryVolume.ParseVolume(140));
        Assert.Equal(42, GalleryVolume.ParseVolume("42"));
        Assert.Equal(33, GalleryVolume.ParseVolume(33.4));
    }

    [Fact]
    public void ParseMuted_DefaultsOff()
    {
        Assert.False(GalleryVolume.ParseMuted(null));
        Assert.False(GalleryVolume.ParseMuted("nope"));
        Assert.True(GalleryVolume.ParseMuted(true));
        Assert.False(GalleryVolume.ParseMuted("false"));
        Assert.True(GalleryVolume.ParseMuted("true"));
    }

    [Fact]
    public void ToggleMute_RemembersVolumeBeforeMute()
    {
        var muted = GalleryVolume.ToggleMute(volume: 80, muted: false, remembered: 50);
        Assert.True(muted.IsMuted);
        Assert.Equal(80, muted.Volume);

        var unmuted = GalleryVolume.ToggleMute(volume: 80, muted: true, remembered: 80);
        Assert.False(unmuted.IsMuted);
        Assert.Equal(80, unmuted.Volume);
    }

    [Fact]
    public void ToggleMute_UnmuteFromZero_RestoresFifty()
    {
        var next = GalleryVolume.ToggleMute(volume: 0, muted: true, remembered: 0);
        Assert.False(next.IsMuted);
        Assert.Equal(50, next.Volume);
    }

    [Fact]
    public void SessionToggleMute_RemembersAndRestores()
    {
        GalleryVolume.Apply(70, false);
        GalleryVolume.ToggleMute();
        Assert.True(GalleryVolume.IsMuted);
        Assert.Equal(70, GalleryVolume.Volume);
        Assert.Equal(70, GalleryVolume.VolumeBeforeMute);

        GalleryVolume.ToggleMute();
        Assert.False(GalleryVolume.IsMuted);
        Assert.Equal(70, GalleryVolume.Volume);
    }

    [Fact]
    public void SessionToggleMute_FromZero_UsesFifty()
    {
        GalleryVolume.Load(0, true);
        GalleryVolume.ToggleMute();
        Assert.False(GalleryVolume.IsMuted);
        Assert.Equal(50, GalleryVolume.Volume);
    }

    [Fact]
    public void Nudge_ScrollUpUnmutes()
    {
        var next = GalleryVolume.Nudge(volume: 40, muted: true, delta: 5);
        Assert.False(next.IsMuted);
        Assert.Equal(45, next.Volume);
    }

    [Fact]
    public void Nudge_ToZeroMutes()
    {
        var next = GalleryVolume.Nudge(volume: 3, muted: false, delta: -5);
        Assert.True(next.IsMuted);
        Assert.Equal(0, next.Volume);
    }

    [Fact]
    public void WheelAndKeyDeltas_HonorFineStep()
    {
        Assert.Equal(5, GalleryVolume.WheelDelta(1, fine: false));
        Assert.Equal(-1, GalleryVolume.WheelDelta(-1, fine: true));
        Assert.Equal(5, GalleryVolume.KeyDelta(up: true, fine: false));
        Assert.Equal(-1, GalleryVolume.KeyDelta(up: false, fine: true));
    }

    [Fact]
    public void IconFor_BandsByLevel()
    {
        Assert.Equal(GalleryVolume.SpeakerIcon.Mute, GalleryVolume.IconFor(0, muted: false));
        Assert.Equal(GalleryVolume.SpeakerIcon.Mute, GalleryVolume.IconFor(80, muted: true));
        Assert.Equal(GalleryVolume.SpeakerIcon.Level0, GalleryVolume.IconFor(20, muted: false));
        Assert.Equal(GalleryVolume.SpeakerIcon.Level1, GalleryVolume.IconFor(50, muted: false));
        Assert.Equal(GalleryVolume.SpeakerIcon.Level2, GalleryVolume.IconFor(90, muted: false));
    }

    [Fact]
    public void TooltipsAndStatus_MatchMuteState()
    {
        Assert.Equal("Mute (M)", GalleryVolume.MuteButtonTooltip(muted: false));
        Assert.Equal("Unmute (M)", GalleryVolume.MuteButtonTooltip(muted: true));
        Assert.Equal("Mute", GalleryVolume.MuteButtonName(muted: false));
        Assert.Equal("Unmute", GalleryVolume.MuteButtonName(muted: true));
        Assert.Equal("Volume 0%", GalleryVolume.VolumeStatus(80, muted: true));
        Assert.Equal("42%", GalleryVolume.SliderTooltip(42, muted: false));
        Assert.Equal("Muted", GalleryVolume.MuteNotification(muted: true));
        Assert.Equal("Unmuted", GalleryVolume.MuteNotification(muted: false));
    }

    [Fact]
    public void ToPlayerVolume_IsZeroWhenMuted()
    {
        Assert.Equal(0.8, GalleryVolume.ToPlayerVolume(80, muted: false), 3);
        Assert.Equal(0.0, GalleryVolume.ToPlayerVolume(80, muted: true), 3);
    }

    [Fact]
    public void UsePalacePinkFill_FollowsBrandAccent()
    {
        Assert.True(GalleryVolume.UsePalacePinkFill(BrandAccent.ModePalacePink));
        Assert.False(GalleryVolume.UsePalacePinkFill(BrandAccent.ModeFollowWindows));
        Assert.False(GalleryVolume.UsePalacePinkFill(null));
    }

    [Fact]
    public void HandlesViewerKey_OnlyForVideo()
    {
        Assert.True(GalleryVolume.HandlesViewerKey(true, false, GalleryVolume.KeyLetterM));
        Assert.True(GalleryVolume.HandlesViewerKey(true, false, GalleryVolume.KeyUp));
        Assert.False(GalleryVolume.HandlesViewerKey(false, false, GalleryVolume.KeyLetterM));
        Assert.False(GalleryVolume.HandlesViewerKey(true, true, GalleryVolume.KeyLetterM));
    }

    [Fact]
    public void Load_PersistsAcrossVideosViaSession()
    {
        GalleryVolume.Load(55, true);
        Assert.Equal(55, GalleryVolume.Volume);
        Assert.True(GalleryVolume.IsMuted);
        Assert.Equal(55, GalleryVolume.VolumeBeforeMute);

        // Switching videos reuses the same static session (ApplyToPlayer reads it).
        Assert.Equal(0.0, GalleryVolume.ToPlayerVolume(GalleryVolume.Volume, GalleryVolume.IsMuted), 3);
        GalleryVolume.ToggleMute();
        Assert.Equal(0.55, GalleryVolume.ToPlayerVolume(GalleryVolume.Volume, GalleryVolume.IsMuted), 3);
    }
}
