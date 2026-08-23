using NUnit.Framework;
using ZombieSurvival.Combat;
using ZombieSurvival.Player;

public sealed class CombatTests
{
    // 탄창의 총알을 소비하면 정확히 한 발씩 감소하는지 검증합니다.
    [Test]
    public void ConsumeRound_DecreasesMagazine()
    {
        var ammo = new AmmunitionState(8, 8, 24);
        Assert.That(ammo.TryConsumeRound(), Is.True);
        Assert.That(ammo.InMagazine, Is.EqualTo(7));
    }

    // 재장전 시 필요한 양만 예비탄에서 탄창으로 이동하는지 검증합니다.
    [Test]
    public void Reload_TransfersOnlyRequiredRounds()
    {
        var ammo = new AmmunitionState(8, 3, 10);
        Assert.That(ammo.Reload(), Is.EqualTo(5));
        Assert.That(ammo.InMagazine, Is.EqualTo(8));
        Assert.That(ammo.Reserve, Is.EqualTo(5));
    }

    // 탄약, 재장전, 발사 간격 조건이 모두 충족되어야 발사 가능한지 검증합니다.
    [TestCase(1, false, 2f, 1f, true)]
    [TestCase(0, false, 2f, 1f, false)]
    [TestCase(1, true, 2f, 1f, false)]
    [TestCase(1, false, 0.5f, 1f, false)]
    public void CanFire_EvaluatesAllConditions(int rounds, bool reloading, float now, float next, bool expected)
    {
        Assert.That(PlayerCombat.CanFire(rounds, reloading, now, next), Is.EqualTo(expected));
    }
}
