using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZombieSurvival.AI;
using ZombieSurvival.Combat;
using ZombieSurvival.Core;
using ZombieSurvival.Hideout;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;
using ZombieSurvival.Player;
using ZombieSurvival.UI;
using BodyPart = ZombieSurvival.Combat.BodyPart;

public sealed class PrototypeExpansionTests
{
    private string savePath;

    [SetUp]
    public void SetUp()
    {
        // 정적 화폐·임무·튜토리얼 상태가 다른 테스트에서 이어지지 않게 매번 새 게임 값으로 되돌립니다.
        HideoutStateStore.ResetForTests();
        HideoutMissionStateStore.ResetForTests();
        HideoutEconomyStateStore.ResetForTests();
        HideoutCraftQueueStateStore.ResetForTests();
        RaidResultStore.Clear();
        savePath = Path.Combine(Path.GetTempPath(), "ZombiePrototypeExpansion_" + Guid.NewGuid().ToString("N") + ".json");
        HideoutSaveService.SetDefaultPathForTests(savePath);
    }

    [TearDown]
    public void TearDown()
    {
        HideoutStateStore.ResetForTests();
        HideoutMissionStateStore.ResetForTests();
        HideoutEconomyStateStore.ResetForTests();
        HideoutCraftQueueStateStore.ResetForTests();
        RaidResultStore.Clear();
        HideoutSaveService.ResetTestOverrides();
        DeleteIfExists(savePath);
        DeleteIfExists(savePath + ".tmp");
        DeleteIfExists(savePath + ".bak");
    }

    // 타이틀의 게임 로드 버튼은 주 저장이나 백업 중 실제로 복구 가능한 파일이 하나라도 있을 때만 켜져야 합니다.
    [Test]
    public void SaveExistence_AcceptsMainOrBackupFile()
    {
        Assert.That(HideoutSaveService.DefaultSaveExists, Is.False);

        File.WriteAllText(savePath, "{}");
        Assert.That(HideoutSaveService.DefaultSaveExists, Is.True);

        File.Delete(savePath);
        Assert.That(HideoutSaveService.DefaultSaveExists, Is.False);
        File.WriteAllText(savePath + ".bak", "{}");
        Assert.That(HideoutSaveService.DefaultSaveExists, Is.True);
    }

    // 임시 무료 상점에서도 구매 전에 전체 격자 공간을 검사하고 크레딧을 전혀 차감하지 않는지 확인합니다.
    [Test]
    public void Trade_BuyIsAtomicWhenStashHasNoSpace()
    {
        ItemDefinition item = CreateItem("trade_item", ItemType.Food, 1, 1, 1, 100);
        try
        {
            var stash = new PlayerInventory.GridData(1, 1);
            Assert.That(HideoutTradeService.CalculateBuyUnitPrice(item, 0), Is.Zero);
            Assert.That(HideoutTradeService.TryBuy(stash, item, 1, 0, out string success), Is.True);
            Assert.That(success, Does.Contain("무료 구매"));
            Assert.That(HideoutEconomyStateStore.Credits, Is.EqualTo(1200));

            Assert.That(HideoutTradeService.TryBuy(stash, item, 1, 0, out string failure), Is.False);
            Assert.That(failure, Does.Contain("공간"));
            Assert.That(HideoutEconomyStateStore.Credits, Is.EqualTo(1200));
            Assert.That(stash.Entries.Count, Is.EqualTo(1));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(item);
        }
    }

    // 판매는 선택 수량을 먼저 제거한 뒤 계산한 크레딧을 한 번만 지급해야 합니다.
    [Test]
    public void Trade_SellConsumesQuantityAndPaysCalculatedReward()
    {
        ItemDefinition item = CreateItem("sell_item", ItemType.Material, 1, 1, 10, 100);
        try
        {
            var stash = new PlayerInventory.GridData(2, 2);
            var entry = new InventoryEntry(item, 3, Vector2Int.zero);
            stash.Entries.Add(entry);

            Assert.That(HideoutTradeService.TrySell(stash, entry, 2, 0, out _), Is.True);
            Assert.That(entry.Quantity, Is.EqualTo(1));
            Assert.That(HideoutEconomyStateStore.Credits, Is.EqualTo(1310));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(item);
        }
    }

    // 확장 탄창은 무기 인스턴스의 실제 탄창 크기를 늘리고 스냅샷 복원 뒤에도 같은 부착물 상태를 유지해야 합니다.
    [Test]
    public void Attachment_RoundTripPreservesEffectiveMagazineSize()
    {
        WeaponDefinition weapon = CreateWeapon(AmmoType.NineMillimeter, 10);
        WeaponAttachmentDefinition attachmentData = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
        attachmentData.Configure("확장 탄창", WeaponAttachmentSlot.Magazine, AmmoType.NineMillimeter, 1f, 1f, 1f, 1.1f, 8);
        ItemDefinition weaponItem = CreateWeaponItem("weapon_test", weapon);
        ItemDefinition attachmentItem = CreateAttachmentItem("attachment_test", attachmentData);
        ItemCatalog catalog = CreateCatalog(weaponItem, attachmentItem);
        try
        {
            var entry = new InventoryEntry(weaponItem, 1, Vector2Int.zero);
            Assert.That(entry.TryInstallAttachment(attachmentItem, out ItemDefinition replaced), Is.True);
            Assert.That(replaced, Is.Null);
            Assert.That(entry.EffectiveMagazineSize, Is.EqualTo(18));

            RaidItemSnapshot snapshot = RaidSnapshotUtility.CaptureItem(entry);
            var target = new PlayerInventory.GridData(6, 3);
            Assert.That(RaidSnapshotUtility.TryRestoreItem(target, catalog, snapshot, out InventoryEntry restored), Is.True);
            Assert.That(restored.InstalledAttachments, Has.Count.EqualTo(1));
            Assert.That(restored.GetAttachment(WeaponAttachmentSlot.Magazine), Is.SameAs(attachmentItem));
            Assert.That(restored.EffectiveMagazineSize, Is.EqualTo(18));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(attachmentItem);
            UnityEngine.Object.DestroyImmediate(weaponItem);
            UnityEngine.Object.DestroyImmediate(attachmentData);
            UnityEngine.Object.DestroyImmediate(weapon);
        }
    }

    // 관통력이 방어 등급보다 높으면 방탄복 감소율 일부만 적용되고 낮으면 원래 감소율을 전부 사용합니다.
    [Test]
    public void ArmorPenetration_WeakensProtectionByClassDifference()
    {
        Assert.That(ArmorProtection.CalculatePenetrationReductionMultiplier(1, ArmorClass.Tactical), Is.EqualTo(1f));
        Assert.That(ArmorProtection.CalculatePenetrationReductionMultiplier(2, ArmorClass.Tactical), Is.EqualTo(0.65f));
        Assert.That(ArmorProtection.CalculatePenetrationReductionMultiplier(3, ArmorClass.Tactical), Is.EqualTo(0.35f));
    }

    // 작은 긁힘은 상태 이상을 만들지 않고, 충분한 피해의 확률 경계는 결정적으로 판정되어야 합니다.
    [Test]
    public void PlayerCondition_UsesDamageThresholdAndChanceBoundary()
    {
        Assert.That(PlayerCondition.ShouldApplyCondition(0f, 1f, 4.9f, 5f), Is.False);
        Assert.That(PlayerCondition.ShouldApplyCondition(0.49f, 0.5f, 5f, 5f), Is.True);
        Assert.That(PlayerCondition.ShouldApplyCondition(0.5f, 0.5f, 5f, 5f), Is.False);
    }

    // 플레이어 부위별 손상은 없지만 적 머리 사격의 피해 배율에 필요한 명중 위치 판정은 계속 유지되는지 확인합니다.
    [Test]
    public void Health_ResolvesHitLocationFromHeightForDamageMultiplier()
    {
        var target = new GameObject("BodyPartTarget");
        CharacterController controller = target.AddComponent<CharacterController>();
        controller.height = 2f;
        try
        {
            Assert.That(Health.ResolveBodyPart(target.transform, new Vector3(0f, 1.8f, 0f)), Is.EqualTo(BodyPart.Head));
            Assert.That(Health.ResolveBodyPart(target.transform, new Vector3(0f, 1f, 0f)), Is.EqualTo(BodyPart.Torso));
            Assert.That(Health.ResolveBodyPart(target.transform, new Vector3(0f, 0.3f, 0f)), Is.EqualTo(BodyPart.Legs));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    // 출혈 테두리는 완전히 사라지거나 화면을 가리지 않고 설정한 낮은 알파 범위 안에서만 천천히 점멸해야 합니다.
    [TestCase(0f)]
    [TestCase(0.5f)]
    [TestCase(10f)]
    public void PlayerConditionHud_BleedingBorderAlphaStaysInReadableRange(float time)
    {
        float alpha = PlayerConditionHud.CalculateBleedingBorderAlpha(time);
        Assert.That(alpha, Is.InRange(0.32f, 0.58f));
    }

    // 첫 임무를 완료하기 전에는 다음 임무가 잠겨 있고 완료 기록이 생긴 직후에만 해금되어야 합니다.
    [Test]
    public void MissionChain_UnlocksAfterPrerequisiteCompletion()
    {
        HideoutMissionDefinition first = CreateMission("first", null, 100, 1);
        HideoutMissionDefinition second = CreateMission("second", "first", 250, 3);
        try
        {
            Assert.That(second.IsUnlocked(), Is.False);
            Assert.That(HideoutMissionStateStore.TryAccept(first), Is.True);
            HideoutMissionStateStore.ApplyRaidResult(new RaidResult { zombieKills = 1 }, first);
            Assert.That(HideoutMissionStateStore.TryComplete(first), Is.True);
            Assert.That(second.IsUnlocked(), Is.True);
            Assert.That(second.CreditReward, Is.EqualTo(250));
            Assert.That(second.RiskRating, Is.EqualTo(3));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(second);
            UnityEngine.Object.DestroyImmediate(first);
        }
    }

    // 튜토리얼 진행도는 앞으로만 이동하고 각 단계 안내에는 실제 조작 키가 포함되어야 합니다.
    [Test]
    public void Tutorial_ProgressNeverMovesBackwardAndExplainsControls()
    {
        Assert.That(TutorialProgressStore.AdvanceTo(TutorialStep.LearnCombat), Is.True);
        Assert.That(TutorialProgressStore.AdvanceTo(TutorialStep.OpenFacility), Is.False);
        Assert.That(TutorialProgressStore.CurrentStep, Is.EqualTo(TutorialStep.LearnCombat));
        Assert.That(TutorialHud.BuildTutorialMessage(TutorialStep.StartRaid), Does.Contain("WASD"));
        Assert.That(TutorialHud.BuildTutorialMessage(TutorialStep.StartRaid), Does.Contain("우클릭"));
    }

    // 현재 저장 형식은 기존 인벤토리 상태와 함께 화폐, 튜토리얼 단계와 무기 부착물 ID를 보존해야 합니다.
    [Test]
    public void CurrentSave_PreservesCreditsTutorialAndAttachments()
    {
        var result = new RaidResult { succeeded = true, inventory = new RaidGridSnapshot() };
        var weapon = new RaidItemSnapshot { itemId = "weapon_test", quantity = 1 };
        weapon.attachmentItemIds.Add("attachment_test");
        result.inventory.items.Add(weapon);
        RaidResultStore.Set(result);
        HideoutStateStore.ApplyPendingRaidResult();
        Assert.That(HideoutEconomyStateStore.TrySpend(200), Is.True);
        TutorialProgressStore.AdvanceTo(TutorialStep.LootItem);

        Assert.That(HideoutSaveService.SaveDefault(), Is.True);
        HideoutStateStore.ResetForTests();
        Assert.That(HideoutSaveService.LoadFromPath(savePath), Is.True);

        Assert.That(HideoutEconomyStateStore.Credits, Is.EqualTo(1000));
        Assert.That(TutorialProgressStore.CurrentStep, Is.EqualTo(TutorialStep.LootItem));
        Assert.That(HideoutStateStore.StashItems[0].attachmentItemIds, Is.EqualTo(new[] { "attachment_test" }));
    }

    // 버전 10에서 새로 저장하는 상인 진행도와 제작 대기열이 게임을 다시 켠 뒤에도 같은 값으로 복원되는지 확인합니다.
    // 발전기를 끈 상태도 함께 검사해 불러오기만으로 제작 시간이 임의로 다시 흐르는 문제를 막습니다.
    [Test]
    public void VersionTenSave_PreservesTraderAndCraftQueueState()
    {
        HideoutEconomyStateStore.AddTraderReputation(75);
        HideoutEconomyStateStore.AdvanceStockRotation();
        HideoutEconomyStateStore.AdvanceStockRotation();
        HideoutCraftQueueStateStore.Restore(
            new[]
            {
                new HideoutCraftJobState
                {
                    recipeId = "craft_test",
                    displayName = "테스트 제작",
                    outputItemId = "ammo_9mm",
                    outputQuantity = 24,
                    remainingSeconds = 12.5f,
                    completed = false
                }
            },
            false,
            321f);

        Assert.That(HideoutSaveService.SaveDefault(), Is.True);
        HideoutEconomyStateStore.ResetForTests();
        HideoutCraftQueueStateStore.ResetForTests();

        Assert.That(HideoutSaveService.LoadFromPath(savePath), Is.True);
        Assert.That(HideoutEconomyStateStore.TraderReputation, Is.EqualTo(75));
        Assert.That(HideoutEconomyStateStore.TraderLevel, Is.EqualTo(2));
        Assert.That(HideoutEconomyStateStore.StockRefreshIndex, Is.EqualTo(2));
        Assert.That(HideoutCraftQueueStateStore.GeneratorEnabled, Is.False);
        Assert.That(HideoutCraftQueueStateStore.GeneratorFuelSeconds, Is.EqualTo(321f).Within(0.001f));
        Assert.That(HideoutCraftQueueStateStore.Jobs, Has.Count.EqualTo(1));
        Assert.That(HideoutCraftQueueStateStore.Jobs[0].recipeId, Is.EqualTo("craft_test"));
        Assert.That(HideoutCraftQueueStateStore.Jobs[0].outputQuantity, Is.EqualTo(24));
        Assert.That(HideoutCraftQueueStateStore.Jobs[0].remainingSeconds, Is.EqualTo(12.5f).Within(0.001f));
        Assert.That(HideoutCraftQueueStateStore.Jobs[0].completed, Is.False);
    }

    // 정상 저장을 두 번 수행하면 두 번째 교체 직전 파일이 .bak으로 남아야 합니다.
    // 최신 파일이 손상되더라도 기본 불러오기가 백업을 사용해 마지막 정상 진행을 복구하는 전체 경로를 검사합니다.
    [Test]
    public void DefaultLoad_RecoversPreviousValidBackupWhenMainSaveIsCorrupted()
    {
        Assert.That(HideoutEconomyStateStore.TrySpend(200), Is.True);
        Assert.That(HideoutSaveService.SaveDefault(), Is.True);

        HideoutEconomyStateStore.AddCredits(50);
        Assert.That(HideoutSaveService.SaveDefault(), Is.True);
        Assert.That(File.Exists(savePath + ".bak"), Is.True);

        // JSON 문법은 읽히지만 지원 범위를 벗어난 버전으로 바꿔 주 저장 파일의 의미 검증 실패를 재현합니다.
        File.WriteAllText(savePath, "{\"version\":999,\"stashItems\":[],\"loadoutItems\":[]}");
        HideoutEconomyStateStore.ResetForTests();
        HideoutSaveService.SetDefaultPathForTests(savePath);

        Assert.That(HideoutSaveService.LoadDefaultIfNeeded(), Is.True);
        Assert.That(HideoutEconomyStateStore.Credits, Is.EqualTo(1000));
    }

    // 소음 이벤트는 0 이하 반경을 무시하고 유효한 총성만 위치와 반경을 그대로 전달해야 합니다.
    [Test]
    public void ZombieNoise_EmitsOnlyPositiveRadius()
    {
        int received = 0;
        ZombieNoiseSystem.NoiseEvent last = default;
        void Handler(ZombieNoiseSystem.NoiseEvent value)
        {
            received++;
            last = value;
        }

        ZombieNoiseSystem.NoiseEmitted += Handler;
        try
        {
            ZombieNoiseSystem.Emit(Vector3.one, 0f);
            ZombieNoiseSystem.Emit(new Vector3(2f, 0f, 3f), 12f);
            Assert.That(received, Is.EqualTo(1));
            Assert.That(last.Position, Is.EqualTo(new Vector3(2f, 0f, 3f)));
            Assert.That(last.Radius, Is.EqualTo(12f));
        }
        finally
        {
            ZombieNoiseSystem.NoiseEmitted -= Handler;
        }
    }

    [Test]
    public void ZombieVisionCone_DistinguishesFrontSideAndRearTargets()
    {
        Assert.That(ZombieBrain.IsInsideVisionCone(Vector3.forward, Vector3.forward, 120f), Is.True);
        Assert.That(ZombieBrain.IsInsideVisionCone(Vector3.forward, Vector3.right, 120f), Is.False);
        Assert.That(ZombieBrain.IsInsideVisionCone(Vector3.forward, Vector3.back, 120f), Is.False);
    }

    [Test]
    public void LootDangerWeight_OnlyRaisesUncommonAndRareChanceWeights()
    {
        Assert.That(LootTableDefinition.GetDangerWeightMultiplier(ItemRarity.Common, 5), Is.EqualTo(1f));
        Assert.That(LootTableDefinition.GetDangerWeightMultiplier(ItemRarity.Uncommon, 5), Is.GreaterThan(1f));
        Assert.That(LootTableDefinition.GetDangerWeightMultiplier(ItemRarity.Rare, 5),
            Is.GreaterThan(LootTableDefinition.GetDangerWeightMultiplier(ItemRarity.Uncommon, 5)));
        Assert.That(LootTableDefinition.GetDangerWeightMultiplier(ItemRarity.Quest, 5), Is.EqualTo(1f));
    }

    // 같은 sourceId라도 Container, Ground와 Monster는 서로 다른 표여야 출처 전용 아이템 구성이 섞이지 않습니다.
    [Test]
    public void RegionLootProfile_SeparatesTablesBySourceTypeAndId()
    {
        RegionLootProfile profile = ScriptableObject.CreateInstance<RegionLootProfile>();
        try
        {
            var container = new RegionLootSourceTable(RegionLootSourceType.Container, "shared", 2, 3);
            var ground = new RegionLootSourceTable(RegionLootSourceType.Ground, "shared", 1, 1);
            var monsterFallback = new RegionLootSourceTable(RegionLootSourceType.Monster, "*", 1, 1);
            profile.Configure(null, 0.05f, container, ground, monsterFallback);

            Assert.That(profile.FindTable(RegionLootSourceType.Container, "shared"), Is.SameAs(container));
            Assert.That(profile.FindTable(RegionLootSourceType.Ground, "shared"), Is.SameAs(ground));
            Assert.That(profile.FindTable(RegionLootSourceType.Monster, "unknown_zombie"), Is.SameAs(monsterFallback));
            Assert.That(profile.FindTable(RegionLootSourceType.Container, "missing"), Is.Null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    // 회수 임무 중 목표 품목은 기본 0%여도 5%가 되고, 임무를 포기하면 에셋 원본 수정 없이 즉시 0%로 돌아와야 합니다.
    [Test]
    public void RegionLootProfile_ActiveItemMissionAddsFivePercentAndAbandonRestoresBaseChance()
    {
        ItemDefinition targetItem = CreateItem("mission_drop", ItemType.Quest, 1, 1, 1, 100);
        HideoutMissionDefinition mission = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
        RegionLootProfile profile = ScriptableObject.CreateInstance<RegionLootProfile>();
        var entry = new RegionLootEntry(targetItem, 0f, 0f, 1, 1);
        try
        {
            profile.Configure(
                null,
                0.05f,
                new RegionLootSourceTable(RegionLootSourceType.Ground, "test_floor", 1, 1, entry));
            mission.Configure(
                "recover_test_item",
                "시험 회수",
                string.Empty,
                HideoutMissionObjectiveType.CollectedItem,
                targetItem,
                1,
                true);

            Assert.That(profile.GetEffectiveDropChance(entry), Is.EqualTo(0f));
            Assert.That(HideoutMissionStateStore.TryAccept(mission), Is.True);
            Assert.That(profile.GetEffectiveDropChance(entry), Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(profile.GetEffectiveWeight(entry), Is.GreaterThan(0f));

            Assert.That(HideoutMissionStateStore.Abandon(), Is.True);
            Assert.That(profile.GetEffectiveDropChance(entry), Is.EqualTo(0f));
            Assert.That(profile.GetEffectiveWeight(entry), Is.EqualTo(0f));

            // 같은 임무를 다시 받아 실제 아이템 회수 결과로 완료한 경우에도 활성 임무가 비워져 기본값으로 돌아오는지 확인합니다.
            Assert.That(HideoutMissionStateStore.TryAccept(mission), Is.True);
            var result = new RaidResult { succeeded = true, inventory = new RaidGridSnapshot() };
            result.inventory.items.Add(new RaidItemSnapshot { itemId = targetItem.ItemId, quantity = 1 });
            HideoutMissionStateStore.ApplyRaidResult(result, mission);
            Assert.That(HideoutMissionStateStore.TryComplete(mission), Is.True);
            Assert.That(profile.GetEffectiveDropChance(entry), Is.EqualTo(0f));
            Assert.That(entry.dropChance, Is.EqualTo(0f), "임무 보정이 ScriptableObject 원본 수치를 변경하면 안 됩니다.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(mission);
            UnityEngine.Object.DestroyImmediate(targetItem);
        }
    }

    // 학교 실제 에셋에는 세 출처가 모두 있고, 무전기는 바닥 전용·천 조각은 몬스터 전용으로 구성되어야 합니다.
    [Test]
    public void SchoolRegionLootAsset_ContainsSeparatedContainerGroundAndMonsterTables()
    {
        RegionLootProfile profile = AssetDatabase.LoadAssetAtPath<RegionLootProfile>(
            "Assets/_Game/Data/RegionalLoot/region_school_loot.asset");
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile.RegionId, Is.EqualTo("school"));
        Assert.That(profile.FindTable(RegionLootSourceType.Container, "supply_crate"), Is.Not.Null);
        Assert.That(profile.FindTable(RegionLootSourceType.Ground, "school_floor"), Is.Not.Null);
        Assert.That(profile.FindTable(RegionLootSourceType.Monster, "walker"), Is.Not.Null);

        Assert.That(CountItemInSource(profile, "quest_radio", RegionLootSourceType.Ground), Is.GreaterThan(0));
        Assert.That(CountItemInSource(profile, "quest_radio", RegionLootSourceType.Container), Is.EqualTo(0));
        Assert.That(CountItemInSource(profile, "quest_radio", RegionLootSourceType.Monster), Is.EqualTo(0));
        Assert.That(CountItemInSource(profile, "material_cloth", RegionLootSourceType.Monster), Is.GreaterThan(0));
        Assert.That(CountItemInSource(profile, "material_cloth", RegionLootSourceType.Container), Is.EqualTo(0));
        Assert.That(CountItemInSource(profile, "material_cloth", RegionLootSourceType.Ground), Is.EqualTo(0));
    }

    [Test]
    public void Mission_WeaponKillAndNoDamageExtractionUseDetailedRaidStats()
    {
        ItemDefinition pistol = CreateItem("mission_pistol", ItemType.Weapon, 1, 1, 1, 100);
        HideoutMissionDefinition weaponMission = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
        HideoutMissionDefinition noDamageMission = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
        try
        {
            weaponMission.Configure("weapon_kill", "권총 처치", string.Empty,
                HideoutMissionObjectiveType.WeaponKills, pistol, 2, false);
            Assert.That(HideoutMissionStateStore.TryAccept(weaponMission), Is.True);
            var weaponResult = new RaidResult
            {
                succeeded = false,
                weaponKills = new System.Collections.Generic.List<RaidWeaponKillStat>
                {
                    new RaidWeaponKillStat { weaponItemId = pistol.ItemId, kills = 2 }
                }
            };
            HideoutMissionStateStore.ApplyRaidResult(weaponResult, weaponMission);
            Assert.That(HideoutMissionStateStore.CanComplete(weaponMission), Is.True);

            HideoutMissionStateStore.ResetForTests();
            noDamageMission.Configure("clean_exit", "무피해 탈출", string.Empty,
                HideoutMissionObjectiveType.NoDamageExtraction, null, 1, false);
            Assert.That(HideoutMissionStateStore.TryAccept(noDamageMission), Is.True);
            HideoutMissionStateStore.ApplyRaidResult(
                new RaidResult { succeeded = true, damageTaken = 0f },
                noDamageMission);
            Assert.That(HideoutMissionStateStore.CanComplete(noDamageMission), Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(noDamageMission);
            UnityEngine.Object.DestroyImmediate(weaponMission);
            UnityEngine.Object.DestroyImmediate(pistol);
        }
    }

    [Test]
    public void Economy_ReputationUnlocksTraderLevelsAndStockRotationIsStable()
    {
        ItemDefinition rareWeapon = CreateItem("rare_weapon", ItemType.Weapon, 1, 1, 1, 1000);
        var serialized = new SerializedObject(rareWeapon);
        serialized.FindProperty("rarity").enumValueIndex = (int)ItemRarity.Rare;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        try
        {
            Assert.That(HideoutEconomyStateStore.TraderLevel, Is.EqualTo(1));
            Assert.That(HideoutTradeService.IsStockAvailable(rareWeapon, 1, 0), Is.False);
            HideoutEconomyStateStore.AddTraderReputation(140);
            Assert.That(HideoutEconomyStateStore.TraderLevel, Is.EqualTo(3));
            bool first = HideoutTradeService.IsStockAvailable(rareWeapon, 3, 7);
            bool second = HideoutTradeService.IsStockAvailable(rareWeapon, 3, 7);
            Assert.That(second, Is.EqualTo(first));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rareWeapon);
        }
    }

    private static ItemDefinition CreateItem(
        string id,
        ItemType type,
        int width,
        int height,
        int maxStack,
        int baseValue)
    {
        ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(item);
        serialized.FindProperty("itemId").stringValue = id;
        serialized.FindProperty("displayName").stringValue = id;
        serialized.FindProperty("itemType").enumValueIndex = (int)type;
        serialized.FindProperty("gridWidth").intValue = width;
        serialized.FindProperty("gridHeight").intValue = height;
        serialized.FindProperty("maxStack").intValue = maxStack;
        serialized.FindProperty("baseValue").intValue = baseValue;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    // 통합 에셋 테스트가 특정 ItemId가 어느 출처 표에 몇 번 들어 있는지 간단히 셉니다.
    private static int CountItemInSource(
        RegionLootProfile profile,
        string itemId,
        RegionLootSourceType sourceType)
    {
        int count = 0;
        foreach (RegionLootSourceTable table in profile.SourceTables)
        {
            if (table == null || table.SourceType != sourceType) continue;
            foreach (RegionLootEntry entry in table.Entries)
                if (entry?.item != null && entry.item.ItemId == itemId) count++;
        }
        return count;
    }

    private static WeaponDefinition CreateWeapon(AmmoType caliber, int magazineSize)
    {
        WeaponDefinition weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
        var serialized = new SerializedObject(weapon);
        serialized.FindProperty("ammoType").enumValueIndex = (int)caliber;
        serialized.FindProperty("magazineSize").intValue = magazineSize;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return weapon;
    }

    private static ItemDefinition CreateWeaponItem(string id, WeaponDefinition weapon)
    {
        ItemDefinition item = CreateItem(id, ItemType.Weapon, 2, 1, 1, 500);
        var serialized = new SerializedObject(item);
        serialized.FindProperty("equipmentSlot").enumValueIndex = (int)EquipmentSlotType.PrimaryWeapon;
        serialized.FindProperty("weaponDefinition").objectReferenceValue = weapon;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    private static ItemDefinition CreateAttachmentItem(string id, WeaponAttachmentDefinition attachment)
    {
        ItemDefinition item = CreateItem(id, ItemType.Attachment, 1, 1, 1, 100);
        var serialized = new SerializedObject(item);
        serialized.FindProperty("weaponAttachmentDefinition").objectReferenceValue = attachment;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    private static ItemCatalog CreateCatalog(params ItemDefinition[] items)
    {
        ItemCatalog catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        var serialized = new SerializedObject(catalog);
        SerializedProperty definitions = serialized.FindProperty("definitions");
        definitions.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++)
            definitions.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return catalog;
    }

    private static HideoutMissionDefinition CreateMission(string id, string prerequisite, int credits, int risk)
    {
        HideoutMissionDefinition mission = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
        mission.Configure(
            id,
            id,
            id,
            HideoutMissionObjectiveType.ZombieKills,
            null,
            1,
            false,
            Array.Empty<HideoutMissionItemReward>());
        mission.ConfigureProgression(prerequisite, credits, risk);
        return mission;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
