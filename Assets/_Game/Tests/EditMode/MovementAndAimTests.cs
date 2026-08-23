using NUnit.Framework;
using UnityEngine;
using ZombieSurvival.Player;

public sealed class MovementAndAimTests
{
    // WASD 대각선 입력의 최종 속력이 직선 이동 속력과 같은지 검증합니다.
    [Test]
    public void DiagonalMovement_IsNormalized()
    {
        Vector3 velocity = PlayerMovement.CalculatePlanarVelocity(Vector2.one, 5f);
        Assert.That(velocity.magnitude, Is.EqualTo(5f).Within(0.0001f));
    }

    // 아래를 향한 Ray가 높이 0의 지면에서 정확한 좌표를 반환하는지 검증합니다.
    [Test]
    public void AimRay_ProjectsOntoGround()
    {
        var ray = new Ray(new Vector3(2f, 10f, 3f), Vector3.down);
        Assert.That(PlayerAim.TryProjectToGround(ray, 0f, out Vector3 point), Is.True);
        Assert.That(point, Is.EqualTo(new Vector3(2f, 0f, 3f)));
    }

    // 원근 Ray를 총구 높이에 투영하면 바닥 교차점과 다른 위치가 계산되는지 검증합니다.
    [Test]
    public void AimRay_ProjectsOntoWeaponHeightPlane()
    {
        var ray = new Ray(new Vector3(0f, 10f, -10f), new Vector3(0f, -1f, 1f).normalized);

        Assert.That(PlayerAim.TryProjectToGround(ray, 1f, out Vector3 point), Is.True);
        Assert.That(point.y, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(point.z, Is.EqualTo(-1f).Within(0.0001f));
    }
}
