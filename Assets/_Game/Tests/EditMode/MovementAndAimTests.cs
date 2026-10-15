using NUnit.Framework;
using UnityEngine;
using ZombieSurvival.CameraSystem;
using ZombieSurvival.Core;
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

    // 캐릭터 시트의 핵심 네 방향이 월드 이동축과 올바른 행으로 연결되는지 검증합니다.
    // 생성 원화는 정면(-Z) 0, 왼쪽(-X) 2, 등(+Z) 4, 오른쪽(+X) 6 순서이므로 이 규칙이 바뀌면 즉시 실패합니다.
    [Test]
    public void PlayerSpriteDirection_MapsCardinalWorldDirectionsToSheetRows()
    {
        Assert.That(PlayerSpriteAnimator.DirectionToRow(Vector3.back), Is.EqualTo(0));
        Assert.That(PlayerSpriteAnimator.DirectionToRow(Vector3.left), Is.EqualTo(2));
        Assert.That(PlayerSpriteAnimator.DirectionToRow(Vector3.forward), Is.EqualTo(4));
        Assert.That(PlayerSpriteAnimator.DirectionToRow(Vector3.right), Is.EqualTo(6));
    }

    // 방향 벡터가 0이면 새 방향을 추측하지 않고 마지막으로 보던 행을 유지해야 정지 순간 그림이 정면으로 튀지 않습니다.
    [Test]
    public void PlayerSpriteDirection_UsesFallbackWhenDirectionIsZero()
    {
        Assert.That(PlayerSpriteAnimator.DirectionToRow(Vector3.zero, 5), Is.EqualTo(5));
    }

    // 플레이어가 위쪽으로 이동하면서 마우스는 왼쪽을 가리키는 상황을 재현합니다.
    // 조준 기능이 켜진 레이드에서는 이동 방향보다 마우스 방향이 우선되어야 스트레이프 중에도 총구가 커서를 바라봅니다.
    [Test]
    public void PlayerSpriteFacing_AimDirectionOverridesMovementDirection()
    {
        Vector3 facing = PlayerSpriteAnimator.ResolveFacingDirection(
            true,
            Vector3.left,
            Vector3.forward,
            0);

        Assert.That(PlayerSpriteAnimator.DirectionToRow(facing), Is.EqualTo(2));
    }

    // 거점에서는 PlayerAim을 끄므로 마우스 방향 대신 실제 이동 방향을 바라봐야 합니다.
    // 이 검사가 없으면 레이드용 마우스 주시 기능 때문에 거점 캐릭터가 한 방향만 보며 걷는 문제가 생길 수 있습니다.
    [Test]
    public void PlayerSpriteFacing_WhenAimIsDisabledUsesMovementDirection()
    {
        Vector3 facing = PlayerSpriteAnimator.ResolveFacingDirection(
            false,
            Vector3.left,
            Vector3.forward,
            0);

        Assert.That(PlayerSpriteAnimator.DirectionToRow(facing), Is.EqualTo(4));
    }

    // 기본 카메라 FOV 60에 조준 비율 0.8을 적용하면 목표 FOV가 48이 되는지 확인합니다.
    // Perspective 카메라는 FOV가 작아질수록 화면이 확대되므로 결과가 원래 값보다 작아야 합니다.
    [Test]
    public void AimZoom_ReducesCameraLensValue()
    {
        Assert.That(CameraFollow.CalculateZoomedLensValue(60f, 0.8f), Is.EqualTo(48f).Within(0.001f));
    }

    // 가림이 시작되면 현재 알파가 지정 속도만큼 감소하고 목표 알파 아래로 넘어가지 않는지 확인합니다.
    // 이 계산이 틀리면 낮은 프레임에서 천장이 한 번에 사라지거나 음수 알파가 되어 렌더링이 불안정해질 수 있습니다.
    [Test]
    public void OccluderFade_MovesTowardTransparentTargetWithoutOvershoot()
    {
        float firstStep = OccluderFadeTarget.MoveFadeAlpha(1f, 0.18f, 5f, 0.1f);
        float finalStep = OccluderFadeTarget.MoveFadeAlpha(firstStep, 0.18f, 5f, 1f);

        Assert.That(firstStep, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(finalStep, Is.EqualTo(0.18f).Within(0.001f));
    }

    // 가림이 끝난 뒤에는 같은 계산이 다시 1을 향해 증가해야 건물을 빠져나왔을 때 지붕이 원래 모습으로 돌아옵니다.
    [Test]
    public void OccluderFade_RestoresOpaqueAlpha()
    {
        float restored = OccluderFadeTarget.MoveFadeAlpha(0.18f, 1f, 5f, 1f);
        Assert.That(restored, Is.EqualTo(1f).Within(0.001f));
    }

    // 1층 플레이어에게는 1층 대상만, 2층 플레이어에게는 2층 대상만 보여야 투명한 천장 너머 오브젝트가 노출되지 않습니다.
    [Test]
    public void FloorVisibility_ShowsOnlyTargetsOnPlayersCurrentFloor()
    {
        const float floorHeight = SchoolRaidMapLayout.ClassroomSecondFloorHeight;

        Assert.That(FloorVisibilityTarget.ShouldShowOnFloor(0.3f, 0f, floorHeight), Is.True);
        Assert.That(FloorVisibilityTarget.ShouldShowOnFloor(floorHeight + 0.3f, 0f, floorHeight), Is.False);
        Assert.That(FloorVisibilityTarget.ShouldShowOnFloor(floorHeight + 0.3f, floorHeight, floorHeight), Is.True);
        Assert.That(FloorVisibilityTarget.ShouldShowOnFloor(0.3f, floorHeight, floorHeight), Is.False);
    }
}
