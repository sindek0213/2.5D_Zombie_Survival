using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Extraction;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;
using ZombieSurvival.Player;
using ZombieSurvival.UI;
using Object = UnityEngine.Object;

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

    // Health가 실제 감소량과 전달받은 충돌 위치를 Damaged 이벤트로 그대로 알리는지 검사합니다.
    [Test]
    public void TakeDamage_RaisesFeedbackEventWithAppliedDamageAndHitPoint()
    {
        var target = new GameObject("DamageEventTestTarget");
        target.SetActive(false);
        try
        {
            Health health = target.AddComponent<Health>();
            InvokeAwake(health);
            float receivedDamage = 0f;
            Vector3 receivedPoint = Vector3.zero;
            int eventCount = 0;
            health.Damaged += (amount, point) =>
            {
                receivedDamage = amount;
                receivedPoint = point;
                eventCount++;
            };

            var hitPoint = new Vector3(2f, 1f, -3f);
            health.TakeDamage(25f, hitPoint);

            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(receivedDamage, Is.EqualTo(25f));
            Assert.That(receivedPoint, Is.EqualTo(hitPoint));
            Assert.That(health.CurrentHealth, Is.EqualTo(75f));
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }

    // PlayerCombat의 시각 효과 이벤트가 한 발당 한 번 발생하고 빗나간 탄도 끝이 정확히 사거리만큼 떨어지는지 검사합니다.
    [Test]
    public void TryFire_RaisesOneShotResolvedEventWithTraceEndpoint()
    {
        var player = new GameObject("ShotFeedbackTestPlayer");
        player.SetActive(false);
        WeaponDefinition pistol = CreateWeaponDefinition(
            "테스트 권총", WeaponTriggerMode.SemiAutomatic, 34f, 30f, 0.2f, 1.1f, 8, AmmoType.NineMillimeter);
        ItemDefinition pistolItem = CreateWeaponItem(
            "pistol_feedback_test", "테스트 권총", EquipmentSlotType.SecondaryWeapon, pistol);
        try
        {
            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            PlayerCombat combat = player.AddComponent<PlayerCombat>();
            var serializedCombat = new SerializedObject(combat);
            // 테스트 씬에 남아 있는 Collider와 무관하게 항상 빗나감 경로를 검사하도록 Raycast LayerMask를 비웁니다.
            serializedCombat.FindProperty("hitMask").intValue = 0;
            serializedCombat.ApplyModifiedPropertiesWithoutUndo();
            InvokeAwake(combat);
            Assert.That(inventory.TryEquipRaidProvidedItem(pistolItem), Is.True);
            Assert.That(combat.TrySelectWeaponSlot(EquipmentSlotType.SecondaryWeapon), Is.True);

            int eventCount = 0;
            Vector3 receivedOrigin = Vector3.zero;
            Vector3 receivedEnd = Vector3.zero;
            bool receivedHit = true;
            combat.ShotResolved += (origin, end, hitSomething) =>
            {
                eventCount++;
                receivedOrigin = origin;
                receivedEnd = end;
                receivedHit = hitSomething;
            };

            Assert.That(combat.TryFire(), Is.True);
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(receivedHit, Is.False);
            Assert.That(Vector3.Distance(receivedOrigin, receivedEnd), Is.EqualTo(30f).Within(0.001f));
            Assert.That(combat.Ammo.InMagazine, Is.EqualTo(7));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(pistolItem);
            Object.DestroyImmediate(pistol);
        }
    }

    // 산탄의 첫 펠릿부터 마지막 펠릿까지 설정된 전체 각도 안에 같은 간격으로 배치되는지 검사합니다.
    // 한 발짜리 일반 총기는 확산 설정이 들어와도 항상 정면 0도를 사용해야 기존 소총과 권총의 명중 방향이 바뀌지 않습니다.
    [TestCase(0, 5, 20f, -10f)]
    [TestCase(1, 5, 20f, -5f)]
    [TestCase(2, 5, 20f, 0f)]
    [TestCase(4, 5, 20f, 10f)]
    [TestCase(0, 1, 20f, 0f)]
    public void CalculatePelletAngle_DistributesPelletsAcrossTotalSpread(
        int pelletIndex,
        int pelletCount,
        float spreadAngle,
        float expectedAngle)
    {
        Assert.That(
            PlayerCombat.CalculatePelletAngle(pelletIndex, pelletCount, spreadAngle),
            Is.EqualTo(expectedAngle).Within(0.001f));
    }

    // 정확도 난수의 0·중간·1 경계값이 각각 왼쪽 끝·정면·오른쪽 끝 각도로 변환되는지 검사합니다.
    [TestCase(0f, 4f, -2f)]
    [TestCase(0.5f, 4f, 0f)]
    [TestCase(1f, 4f, 2f)]
    public void CalculateAccuracyAngle_MapsRollAcrossWeaponSpread(float roll, float spread, float expected)
    {
        Assert.That(PlayerCombat.CalculateAccuracyAngle(roll, spread), Is.EqualTo(expected).Within(0.001f));
    }

    // 기본 명중 오차 4도에 정밀 조준 배율 0.35를 적용하면 실제 오차가 1.4도로 줄어드는지 확인합니다.
    // 조준을 풀었을 때는 같은 함수가 원래 4도를 그대로 반환해야 일반 사격 성능이 변하지 않습니다.
    [Test]
    public void CalculateEffectiveAccuracySpread_AimingReducesOnlyCurrentShotError()
    {
        Assert.That(
            PlayerCombat.CalculateEffectiveAccuracySpread(4f, true, 0.35f),
            Is.EqualTo(1.4f).Within(0.001f));
        Assert.That(
            PlayerCombat.CalculateEffectiveAccuracySpread(4f, false, 0.35f),
            Is.EqualTo(4f).Within(0.001f));
    }

    // 산탄총은 일곱 번의 피해 Ray를 계산하더라도 쉘은 한 발만 소비하고 발사 피드백 이벤트도 한 번만 보내야 합니다.
    // 이 테스트는 충돌 LayerMask를 비워 실제 씬 Collider의 영향을 받지 않고 탄약과 이벤트 규칙만 고정합니다.
    [Test]
    public void ShotgunFire_ConsumesOneShellAndRaisesOneShotEvent()
    {
        var player = new GameObject("ShotgunFireTestPlayer");
        player.SetActive(false);
        WeaponDefinition shotgun = CreateWeaponDefinition(
            "테스트 산탄총",
            WeaponTriggerMode.SemiAutomatic,
            12f,
            14f,
            0.75f,
            2.2f,
            5,
            AmmoType.TwelveGauge,
            7,
            18f);
        ItemDefinition shotgunItem = CreateWeaponItem(
            "shotgun_fire_test",
            "테스트 산탄총",
            EquipmentSlotType.PrimaryWeapon,
            shotgun);

        try
        {
            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            PlayerCombat combat = player.AddComponent<PlayerCombat>();
            var serializedCombat = new SerializedObject(combat);
            serializedCombat.FindProperty("hitMask").intValue = 0;
            serializedCombat.ApplyModifiedPropertiesWithoutUndo();
            InvokeAwake(combat);
            Assert.That(inventory.TryEquipRaidProvidedItem(shotgunItem), Is.True);
            Assert.That(combat.TrySelectWeaponSlot(EquipmentSlotType.PrimaryWeapon), Is.True);

            int eventCount = 0;
            combat.ShotResolved += (_, _, _) => eventCount++;

            Assert.That(combat.TryFire(), Is.True);
            Assert.That(combat.Ammo.InMagazine, Is.EqualTo(4));
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(combat.CurrentPelletCount, Is.EqualTo(7));
            Assert.That(combat.CurrentSpreadAngle, Is.EqualTo(18f));
            Assert.That(shotgun.PotentialDamagePerShot, Is.EqualTo(84f));
            Assert.That(ItemDetailFormatter.AmmoName(AmmoType.TwelveGauge), Is.EqualTo("12게이지"));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(shotgunItem);
            Object.DestroyImmediate(shotgun);
        }
    }

    // 실제 장비 슬롯에 무기가 없으면 탄약 상태도 만들지 않고 발사 요청을 거부하는지 검사합니다.
    [Test]
    public void TryFire_WithoutEquippedWeapon_IsRejected()
    {
        var player = new GameObject("UnarmedCombatTestPlayer");
        player.SetActive(false);
        try
        {
            player.AddComponent<PlayerInventory>();
            PlayerCombat combat = player.AddComponent<PlayerCombat>();
            InvokeAwake(combat);

            Assert.That(combat.ActiveWeaponEntry, Is.Null);
            Assert.That(combat.Ammo, Is.Null);
            Assert.That(combat.CurrentWeaponName, Is.EqualTo("무기 없음"));
            Assert.That(combat.TryFire(), Is.False);
            Assert.That(combat.TryStartReload(), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(player);
        }
    }

    // 서로 다른 시스템이 동시에 잠금을 요청하면 마지막 잠금이 해제될 때만 원래 조작 상태가 복원되는지 검사합니다.
    [Test]
    public void PlayerControlGate_MultipleOwnersRestoreOnlyAfterLastRelease()
    {
        GameObject player = CreateControllablePlayer(out PlayerControlGate gate);
        var inventoryOwner = new GameObject("InventoryLockOwner");
        var raidOwner = new GameObject("RaidLockOwner");
        try
        {
            PlayerMovement movement = player.GetComponent<PlayerMovement>();
            PlayerAim aim = player.GetComponent<PlayerAim>();
            PlayerCombat combat = player.GetComponent<PlayerCombat>();
            PlayerInteraction interaction = player.GetComponent<PlayerInteraction>();
            // 잠금 전부터 꺼진 전투 컴포넌트가 마지막 해제 때 임의로 켜지지 않는지도 함께 검사합니다.
            combat.enabled = false;

            Assert.That(gate.AcquireLock(inventoryOwner), Is.True);
            Assert.That(gate.AcquireLock(raidOwner), Is.True);
            Assert.That(gate.ActiveLockCount, Is.EqualTo(2));
            Assert.That(movement.enabled, Is.False);
            Assert.That(aim.enabled, Is.False);
            Assert.That(combat.enabled, Is.False);
            Assert.That(interaction.enabled, Is.False);

            Assert.That(gate.ReleaseLock(inventoryOwner), Is.True);
            Assert.That(gate.IsLocked, Is.True);
            Assert.That(movement.enabled, Is.False);

            Assert.That(gate.ReleaseLock(raidOwner), Is.True);
            Assert.That(gate.IsLocked, Is.False);
            Assert.That(movement.enabled, Is.True);
            Assert.That(aim.enabled, Is.True);
            Assert.That(combat.enabled, Is.False);
            Assert.That(interaction.enabled, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(inventoryOwner);
            Object.DestroyImmediate(raidOwner);
            Object.DestroyImmediate(player);
        }
    }

    // 잠금 소유자가 정상적인 ReleaseLock 호출 전에 파괴돼도 플레이어 조작이 영구적으로 잠기지 않는지 검사합니다.
    // UnityEngine.Object는 Destroy 뒤 null처럼 비교되지만 HashSet 항목은 자동 삭제되지 않으므로 이 회귀 테스트가 필요합니다.
    [Test]
    public void PlayerControlGate_DestroyedOwnerIsPrunedAndRestoresControls()
    {
        GameObject player = CreateControllablePlayer(out PlayerControlGate gate);
        var temporaryOwner = new GameObject("DestroyedLockOwner");
        try
        {
            PlayerMovement movement = player.GetComponent<PlayerMovement>();
            Assert.That(gate.AcquireLock(temporaryOwner), Is.True);
            Assert.That(movement.enabled, Is.False);

            Object.DestroyImmediate(temporaryOwner);
            temporaryOwner = null;

            // IsLocked를 읽는 시점에 파괴된 소유자를 정리하고 잠금 전 상태를 복원해야 합니다.
            Assert.That(gate.IsLocked, Is.False);
            Assert.That(gate.ActiveLockCount, Is.Zero);
            Assert.That(movement.enabled, Is.True);
        }
        finally
        {
            if (temporaryOwner != null) Object.DestroyImmediate(temporaryOwner);
            Object.DestroyImmediate(player);
        }
    }

    // InventoryHud를 열고 닫는 실제 명령이 PlayerControlGate 잠금과 정확히 연결되는지 검사합니다.
    [Test]
    public void InventoryHud_OpenLocksWorldControlsAndCloseRestoresThem()
    {
        GameObject player = CreateControllablePlayer(out PlayerControlGate gate);
        var hudObject = new GameObject("InventoryControlLockTestHud");
        hudObject.SetActive(false);
        try
        {
            var hud = hudObject.AddComponent<InventoryHud>();
            hud.Configure(player.GetComponent<PlayerInventory>(), player.GetComponent<PlayerInteraction>());

            hud.SetOpen(true);
            Assert.That(hud.IsOpen, Is.True);
            Assert.That(gate.IsLocked, Is.True);
            Assert.That(player.GetComponent<PlayerMovement>().enabled, Is.False);
            Assert.That(player.GetComponent<PlayerAim>().enabled, Is.False);
            Assert.That(player.GetComponent<PlayerCombat>().enabled, Is.False);
            Assert.That(player.GetComponent<PlayerInteraction>().enabled, Is.False);

            hud.SetOpen(false);
            Assert.That(hud.IsOpen, Is.False);
            Assert.That(gate.IsLocked, Is.False);
            Assert.That(player.GetComponent<PlayerMovement>().enabled, Is.True);
            Assert.That(player.GetComponent<PlayerCombat>().enabled, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(hudObject);
            Object.DestroyImmediate(player);
        }
    }

    // Canvas 안의 픽셀 위치가 가장 가까운 50픽셀 격자 칸으로 변환되는지 확인합니다.
    // 드롭 판정의 반올림 경계를 고정해 UI 이미지를 교체해도 아이템이 한 칸씩 밀리는 회귀 오류를 찾습니다.
    [TestCase(0f, 0f, 0, 0)]
    [TestCase(24f, 24f, 0, 0)]
    [TestCase(26f, 74f, 1, 1)]
    [TestCase(126f, 151f, 3, 3)]
    public void InventoryGridView_CalculateDropCellRoundsToNearestCell(
        float pixelX,
        float pixelY,
        int expectedX,
        int expectedY)
    {
        Vector2Int result = InventoryGridView.CalculateDropCell(new Vector2(pixelX, pixelY), 50f);

        Assert.That(result, Is.EqualTo(new Vector2Int(expectedX, expectedY)));
    }

    // 장착한 소총을 선택하면 고정 권총값 대신 소총 에셋의 피해량·사거리·탄창과 자동 발사 설정을 사용하는지 검사합니다.
    // 보조무기로 전환했다가 돌아왔을 때도 소총에서 이미 소비한 탄약이 공짜로 복구되지 않아야 합니다.
    [Test]
    public void SelectEquippedWeapon_AppliesDefinitionAndPreservesItsAmmunition()
    {
        var player = new GameObject("EquippedWeaponTestPlayer");
        player.SetActive(false);
        WeaponDefinition rifle = CreateWeaponDefinition(
            "테스트 소총", WeaponTriggerMode.Automatic, 45f, 38f, 0.12f, 1.6f, 20, AmmoType.FiveFiveSix);
        WeaponDefinition pistol = CreateWeaponDefinition(
            "테스트 권총", WeaponTriggerMode.SemiAutomatic, 28f, 24f, 0.3f, 1f, 8, AmmoType.NineMillimeter);
        ItemDefinition rifleItem = CreateWeaponItem("rifle_test", "테스트 소총", EquipmentSlotType.PrimaryWeapon, rifle);
        ItemDefinition pistolItem = CreateWeaponItem("pistol_test", "테스트 권총", EquipmentSlotType.SecondaryWeapon, pistol);
        ItemDefinition rifleAmmo = CreateAmmoItem("ammo_556_test", AmmoType.FiveFiveSix, 60);
        ItemDefinition pistolAmmo = CreateAmmoItem("ammo_9mm_test", AmmoType.NineMillimeter, 60);

        try
        {
            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            PlayerCombat combat = player.AddComponent<PlayerCombat>();
            InvokeAwake(combat);
            Assert.That(inventory.TryAdd(rifleAmmo, 40), Is.True);
            Assert.That(inventory.TryAdd(pistolAmmo, 12), Is.True);

            var rifleEntry = new InventoryEntry(rifleItem, 1, Vector2Int.zero);
            var pistolEntry = new InventoryEntry(pistolItem, 1, new Vector2Int(2, 0));
            inventory.Root.Entries.Add(rifleEntry);
            inventory.Root.Entries.Add(pistolEntry);
            Assert.That(inventory.TryEquip(inventory.Root, rifleEntry), Is.True);
            Assert.That(inventory.TryEquip(inventory.Root, pistolEntry), Is.True);

            Assert.That(combat.TrySelectWeaponSlot(EquipmentSlotType.PrimaryWeapon), Is.True);
            Assert.That(combat.ActiveWeaponEntry, Is.SameAs(rifleEntry));
            Assert.That(combat.CurrentWeaponName, Is.EqualTo("테스트 소총"));
            Assert.That(combat.CurrentDamage, Is.EqualTo(45f));
            Assert.That(combat.CurrentRange, Is.EqualTo(38f));
            Assert.That(combat.CurrentFireInterval, Is.EqualTo(0.12f));
            Assert.That(combat.CurrentReloadDuration, Is.EqualTo(1.6f));
            Assert.That(combat.IsAutomatic, Is.True);
            Assert.That(combat.Ammo.MagazineSize, Is.EqualTo(20));
            Assert.That(combat.Ammo.Reserve, Is.EqualTo(40));

            // 실제 영구 탄창 상태는 무기 InventoryEntry가 소유하므로 두 발 소비도 해당 인스턴스에 적용합니다.
            Assert.That(rifleEntry.TryConsumeMagazineRound(), Is.True);
            Assert.That(rifleEntry.TryConsumeMagazineRound(), Is.True);
            Assert.That(combat.TrySelectWeaponSlot(EquipmentSlotType.SecondaryWeapon), Is.True);
            Assert.That(combat.Ammo.MagazineSize, Is.EqualTo(8));
            Assert.That(combat.IsAutomatic, Is.False);

            Assert.That(combat.TrySelectWeaponSlot(EquipmentSlotType.PrimaryWeapon), Is.True);
            Assert.That(combat.Ammo.InMagazine, Is.EqualTo(18));
            Assert.That(combat.Ammo.Reserve, Is.EqualTo(40));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(rifleItem);
            Object.DestroyImmediate(pistolItem);
            Object.DestroyImmediate(rifleAmmo);
            Object.DestroyImmediate(pistolAmmo);
            Object.DestroyImmediate(rifle);
            Object.DestroyImmediate(pistol);
        }
    }

    // 전투 데이터가 연결되지 않은 무기 아이템은 선택을 거부하고 현재 무장 해제 상태를 바꾸지 않는지 검사합니다.
    [Test]
    public void SelectWeaponWithoutDefinition_IsRejectedWithoutChangingFallback()
    {
        var player = new GameObject("InvalidWeaponTestPlayer");
        player.SetActive(false);
        ItemDefinition invalidWeapon = CreateWeaponItem(
            "invalid_weapon", "잘못된 무기", EquipmentSlotType.PrimaryWeapon, null);

        try
        {
            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            PlayerCombat combat = player.AddComponent<PlayerCombat>();
            InvokeAwake(combat);

            var entry = new InventoryEntry(invalidWeapon, 1, Vector2Int.zero);
            inventory.Root.Entries.Add(entry);
            Assert.That(inventory.TryEquip(inventory.Root, entry), Is.True);

            Assert.That(combat.TrySelectWeaponSlot(EquipmentSlotType.PrimaryWeapon), Is.False);
            Assert.That(combat.ActiveWeaponEntry, Is.Null);
            Assert.That(combat.Ammo, Is.Null);
            Assert.That(combat.CurrentWeaponName, Is.EqualTo("무기 없음"));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(invalidWeapon);
        }
    }

    // 코드에서 생성한 모든 임시 효과음 파형이 비어 있지 않고 AudioClip 허용 범위인 -1~1 안에 있는지 검사합니다.
    [Test]
    public void PrototypeSoundFactory_GeneratedSamplesAreFiniteAndNormalized()
    {
        AssertValidSamples(PrototypeSoundFactory.GenerateGunshotSamples());
        AssertValidSamples(PrototypeSoundFactory.GenerateImpactSamples(true));
        AssertValidSamples(PrototypeSoundFactory.GenerateImpactSamples(false));
        AssertValidSamples(PrototypeSoundFactory.GenerateDeathSamples(true));
        AssertValidSamples(PrototypeSoundFactory.GenerateDeathSamples(false));
    }

    // 결과 문구가 있고 현재 시간이 종료 시각보다 작을 때만 상호작용 결과를 표시하는지 경계값을 검사합니다.
    [TestCase("아이템을 획득했습니다.", 1f, 2f, true)]
    [TestCase("아이템을 획득했습니다.", 2f, 2f, false)]
    [TestCase("", 1f, 2f, false)]
    public void InteractionStatusVisibility_UsesMessageAndEndTime(
        string message,
        float currentTime,
        float endTime,
        bool expected)
    {
        Assert.That(PlayerInteraction.IsStatusVisible(message, currentTime, endTime), Is.EqualTo(expected));
    }

    // 탄약 HUD가 무장 해제, 일반 사격 준비, 재장전 상태를 서로 혼동하지 않고 올바른 문자열로 바꾸는지 검사합니다.
    // 이 규칙을 Canvas 생성과 분리해 검증하면 글꼴이나 배치를 바꾸더라도 전투 정보 표시가 깨졌는지 빠르게 찾을 수 있습니다.
    [TestCase(-1, -1, false, "- / -")]
    [TestCase(15, 45, false, "15 / 45")]
    [TestCase(0, 12, false, "0 / 12")]
    [TestCase(7, 21, true, "재장전 중...")]
    public void AmmoHud_BuildAmmoText_DescribesCombatState(
        int magazine,
        int reserve,
        bool isReloading,
        string expected)
    {
        Assert.That(AmmoHud.BuildAmmoText(magazine, reserve, isReloading), Is.EqualTo(expected));
    }

    // 탈출 HUD가 잠김·사용 가능·진행 중·성공의 네 상태를 사용자가 이해할 수 있는 문구로 표시하는지 검사합니다.
    [TestCase(ExtractionController.ExtractionState.Locked, 0f, "탈출 조건: 퀘스트 무전기 x1")]
    [TestCase(ExtractionController.ExtractionState.Available, 0f, "탈출 가능 - 녹색 구역으로 이동하세요")]
    [TestCase(ExtractionController.ExtractionState.CountingDown, 1.24f, "탈출까지 1.2초")]
    [TestCase(ExtractionController.ExtractionState.Succeeded, 0f, "탈출 성공")]
    public void ExtractionHud_BuildMessage_DescribesEveryState(
        ExtractionController.ExtractionState state,
        float remainingTime,
        string expected)
    {
        Assert.That(
            ExtractionHud.BuildMessage(state, remainingTime, "퀘스트 무전기", 1),
            Is.EqualTo(expected));
    }

    // 인벤토리 확대·페이드 효과의 보간값이 시작과 끝에서는 정확하고 중간에서는 부드럽게 이어지는지 검사합니다.
    // 범위를 벗어난 시간이 들어와도 0~1로 제한되어 Canvas 알파나 크기가 잘못 뒤집히지 않아야 합니다.
    [TestCase(-1f, 0f)]
    [TestCase(0f, 0f)]
    [TestCase(0.5f, 0.5f)]
    [TestCase(1f, 1f)]
    [TestCase(2f, 1f)]
    public void InventoryHud_EaseTransition_ClampsAndSmoothsProgress(float progress, float expected)
    {
        Assert.That(InventoryHud.EaseTransition(progress), Is.EqualTo(expected).Within(0.0001f));
    }

    // 재장전 요청량이 탄창 빈자리와 인벤토리의 호환 탄약 중 더 적은 값을 정확히 선택하는지 검사합니다.
    [TestCase(3, 8, 20, 5)]
    [TestCase(3, 8, 2, 2)]
    [TestCase(8, 8, 20, 0)]
    [TestCase(0, 20, 0, 0)]
    public void CalculateReloadRequest_UsesMagazineSpaceAndAvailableAmmo(
        int loaded,
        int magazineSize,
        int available,
        int expected)
    {
        Assert.That(PlayerCombat.CalculateReloadRequest(loaded, magazineSize, available), Is.EqualTo(expected));
    }

    // 인벤토리 탄약 소비가 요청한 탄종만 줄이고 다른 무기에 사용하는 탄약은 그대로 보존하는지 검사합니다.
    [Test]
    public void TryConsumeAmmo_RemovesOnlyMatchingCaliber()
    {
        var player = new GameObject("AmmoInventoryConsumptionTestPlayer");
        player.SetActive(false);
        ItemDefinition nineMillimeter = CreateAmmoItem("ammo_9mm_consume_test", AmmoType.NineMillimeter, 30);
        ItemDefinition fiveFiveSix = CreateAmmoItem("ammo_556_consume_test", AmmoType.FiveFiveSix, 30);
        try
        {
            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            Assert.That(inventory.TryAdd(nineMillimeter, 12), Is.True);
            Assert.That(inventory.TryAdd(fiveFiveSix, 20), Is.True);

            Assert.That(inventory.TryConsumeAmmo(AmmoType.NineMillimeter, 8, out int consumed), Is.True);
            Assert.That(consumed, Is.EqualTo(8));
            Assert.That(inventory.CountAmmo(AmmoType.NineMillimeter), Is.EqualTo(4));
            Assert.That(inventory.CountAmmo(AmmoType.FiveFiveSix), Is.EqualTo(20));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(nineMillimeter);
            Object.DestroyImmediate(fiveFiveSix);
        }
    }

    // PlayerCombat의 실제 재장전 이동 단계가 탄창 빈자리만 채우고 같은 탄종의 인벤토리 묶음만 줄이는지 통합 검사합니다.
    [Test]
    public void ReloadTransfer_ConsumesInventoryAmmoAndFillsStoredMagazine()
    {
        var player = new GameObject("InventoryReloadTransferTestPlayer");
        player.SetActive(false);
        WeaponDefinition pistol = CreateWeaponDefinition(
            "재장전 테스트 권총",
            WeaponTriggerMode.SemiAutomatic,
            20f,
            20f,
            0.2f,
            1f,
            8,
            AmmoType.NineMillimeter);
        ItemDefinition pistolItem = CreateWeaponItem(
            "reload_test_pistol",
            "재장전 테스트 권총",
            EquipmentSlotType.SecondaryWeapon,
            pistol);
        ItemDefinition nineMillimeter = CreateAmmoItem("reload_test_9mm", AmmoType.NineMillimeter, 30);
        ItemDefinition fiveFiveSix = CreateAmmoItem("reload_test_556", AmmoType.FiveFiveSix, 30);
        try
        {
            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            PlayerCombat combat = player.AddComponent<PlayerCombat>();
            InvokeAwake(combat);
            Assert.That(inventory.TryAdd(nineMillimeter, 12), Is.True);
            Assert.That(inventory.TryAdd(fiveFiveSix, 10), Is.True);

            var weaponEntry = new InventoryEntry(pistolItem, 1, Vector2Int.zero);
            inventory.Root.Entries.Add(weaponEntry);
            Assert.That(inventory.TryEquip(inventory.Root, weaponEntry), Is.True);
            Assert.That(combat.TrySelectWeaponSlot(EquipmentSlotType.SecondaryWeapon), Is.True);
            for (int i = 0; i < 5; i++) Assert.That(weaponEntry.TryConsumeMagazineRound(), Is.True);
            Assert.That(weaponEntry.MagazineRounds, Is.EqualTo(3));

            MethodInfo transfer = typeof(PlayerCombat).GetMethod(
                "TransferAmmoFromInventory",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(transfer, Is.Not.Null);
            int loaded = (int)transfer.Invoke(combat, new object[] { weaponEntry });

            Assert.That(loaded, Is.EqualTo(5));
            Assert.That(weaponEntry.MagazineRounds, Is.EqualTo(8));
            Assert.That(inventory.CountAmmo(AmmoType.NineMillimeter), Is.EqualTo(7));
            Assert.That(inventory.CountAmmo(AmmoType.FiveFiveSix), Is.EqualTo(10));
            Assert.That(combat.Ammo.InMagazine, Is.EqualTo(8));
            Assert.That(combat.Ammo.Reserve, Is.EqualTo(7));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(pistolItem);
            Object.DestroyImmediate(nineMillimeter);
            Object.DestroyImmediate(fiveFiveSix);
            Object.DestroyImmediate(pistol);
        }
    }

    // 조작 잠금 테스트에 필요한 네 입력 컴포넌트와 의존 컴포넌트를 한 플레이어 루트에 구성합니다.
    private static GameObject CreateControllablePlayer(out PlayerControlGate gate)
    {
        var player = new GameObject("PlayerControlGateTestPlayer");
        player.SetActive(false);
        player.AddComponent<PlayerMovement>();
        player.AddComponent<PlayerCombat>();
        player.AddComponent<PlayerInventory>();
        player.AddComponent<PlayerInteraction>();
        gate = player.AddComponent<PlayerControlGate>();
        return player;
    }

    private static void AssertValidSamples(float[] samples)
    {
        Assert.That(samples, Is.Not.Null.And.Not.Empty);
        bool containsAudibleValue = false;
        foreach (float sample in samples)
        {
            Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False);
            Assert.That(sample, Is.InRange(-1f, 1f));
            if (Mathf.Abs(sample) > 0.001f) containsAudibleValue = true;
        }
        Assert.That(containsAudibleValue, Is.True);
    }

    // 실제 에셋 파일을 만들지 않고도 장착 무기 수치 적용을 검증할 수 있도록 메모리 전용 무기 정의를 만듭니다.
    private static WeaponDefinition CreateWeaponDefinition(
        string displayName,
        WeaponTriggerMode triggerMode,
        float damage,
        float range,
        float fireInterval,
        float reloadDuration,
        int magazineSize,
        AmmoType ammoType,
        int pelletCount = 1,
        float spreadAngle = 0f)
    {
        WeaponDefinition definition = ScriptableObject.CreateInstance<WeaponDefinition>();
        var serialized = new SerializedObject(definition);
        serialized.FindProperty("displayName").stringValue = displayName;
        serialized.FindProperty("triggerMode").enumValueIndex = (int)triggerMode;
        serialized.FindProperty("damage").floatValue = damage;
        serialized.FindProperty("range").floatValue = range;
        serialized.FindProperty("fireInterval").floatValue = fireInterval;
        serialized.FindProperty("reloadDuration").floatValue = reloadDuration;
        serialized.FindProperty("magazineSize").intValue = magazineSize;
        serialized.FindProperty("ammoType").enumValueIndex = (int)ammoType;
        // 선택 매개변수를 생략한 기존 테스트는 일반 총기 1발·0도를 그대로 사용합니다.
        serialized.FindProperty("pelletCount").intValue = pelletCount;
        serialized.FindProperty("spreadAngle").floatValue = spreadAngle;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return definition;
    }

    // 무기 전투 에셋을 장착 가능한 ItemDefinition과 연결해 실제 인벤토리 장착 경로를 테스트합니다.
    private static ItemDefinition CreateWeaponItem(
        string itemId,
        string displayName,
        EquipmentSlotType slot,
        WeaponDefinition weaponDefinition)
    {
        ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(item);
        serialized.FindProperty("itemId").stringValue = itemId;
        serialized.FindProperty("displayName").stringValue = displayName;
        serialized.FindProperty("itemType").enumValueIndex = (int)ItemType.Weapon;
        serialized.FindProperty("maxStack").intValue = 1;
        serialized.FindProperty("gridWidth").intValue = 1;
        serialized.FindProperty("gridHeight").intValue = 1;
        serialized.FindProperty("equipmentSlot").enumValueIndex = (int)slot;
        serialized.FindProperty("weaponDefinition").objectReferenceValue = weaponDefinition;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    // 특정 탄종과 충분한 최대 중첩을 가진 메모리 전용 탄약 정의를 만들어 인벤토리 소비 테스트에 사용합니다.
    private static ItemDefinition CreateAmmoItem(string itemId, AmmoType ammoType, int maxStack)
    {
        ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(item);
        serialized.FindProperty("itemId").stringValue = itemId;
        serialized.FindProperty("displayName").stringValue = itemId;
        serialized.FindProperty("itemType").enumValueIndex = (int)ItemType.Ammo;
        serialized.FindProperty("ammoType").enumValueIndex = (int)ammoType;
        serialized.FindProperty("maxStack").intValue = maxStack;
        serialized.FindProperty("gridWidth").intValue = 1;
        serialized.FindProperty("gridHeight").intValue = 1;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    // EditMode에서는 비활성 테스트 오브젝트의 Awake가 자동 실행되지 않으므로 실제 Play 시작과 같은 초기화 메서드를 명시적으로 호출합니다.
    private static void InvokeAwake(MonoBehaviour component)
    {
        MethodInfo awake = component.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(awake, Is.Not.Null, $"{component.GetType().Name}에 Awake 초기화 메서드가 필요합니다.");
        awake.Invoke(component, null);
    }
}
