using NUnit.Framework;
using UnityEngine;
using ZombieSurvival.Core;

public sealed class BackgroundMusicTests
{
    [TestCase(GameSceneNavigator.TitleSceneName, BackgroundMusicController.TitleMusicResource)]
    [TestCase(GameSceneNavigator.HideoutSceneName, BackgroundMusicController.HideoutMusicResource)]
    [TestCase(GameSceneNavigator.SchoolRaidSceneName, BackgroundMusicController.SchoolMusicResource)]
    public void SceneMapping_SelectsExpectedTrack(string sceneName, string expectedResource)
    {
        Assert.That(BackgroundMusicController.GetMusicResourceForScene(sceneName), Is.EqualTo(expectedResource));
        Assert.That(BackgroundMusicController.GetMusicVolumeForScene(sceneName), Is.GreaterThan(0f));
    }

    [Test]
    public void LoadingScene_KeepsCurrentTrackInsteadOfSelectingAnother()
    {
        Assert.That(BackgroundMusicController.GetMusicResourceForScene(GameSceneNavigator.LoadingSceneName), Is.Null);
        Assert.That(BackgroundMusicController.GetMusicVolumeForScene(GameSceneNavigator.LoadingSceneName), Is.Zero);
    }

    [TestCase(BackgroundMusicController.TitleMusicResource, 60f, 65f)]
    [TestCase(BackgroundMusicController.HideoutMusicResource, 78f, 82f)]
    [TestCase(BackgroundMusicController.SchoolMusicResource, 81f, 84f)]
    public void GeneratedTrack_IsStereo48KhzAndExpectedLength(string resourcePath, float minimumLength, float maximumLength)
    {
        AudioClip clip = Resources.Load<AudioClip>(resourcePath);
        Assert.That(clip, Is.Not.Null, "생성된 배경음악이 Resources 경로에 있어야 합니다.");
        Assert.That(clip.channels, Is.EqualTo(2));
        Assert.That(clip.frequency, Is.EqualTo(48000));
        Assert.That(clip.length, Is.InRange(minimumLength, maximumLength));
    }
}
