using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using ZombieSurvival.Combat;
using ZombieSurvival.Core;
using ZombieSurvival.Hideout;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

public sealed class HideoutTests
{
    private string temporarySavePath;

    [SetUp]
    public void SetUp()
    {
        // 정적 저장소는 씬이 바뀌어도 유지되므로 각 테스트 시작 전에 창고, 출격 목록과 직전 결과를 모두 비웁니다.
        HideoutStateStore.ResetForTests();
        RaidResultStore.Clear();
        temporarySavePath = Path.Combine(Path.GetTempPath(), "ZombieSurvivalHideout_" + Guid.NewGuid().ToString("N") + ".json");
        HideoutSaveService.SetDefaultPathForTests(temporarySavePath);
    }

    [TearDown]
    public void TearDown()
    {
        HideoutStateStore.ResetForTests();
        RaidResultStore.Clear();
        HideoutSaveService.ResetTestOverrides();

        // 테스트가 만든 정확한 파일 이름만 제거하며 사용자 persistentDataPath의 실제 저장 파일은 접근하지 않습니다.
        DeleteIfExists(temporarySavePath);
        DeleteIfExists(temporarySavePath + ".tmp");
        DeleteIfExists(temporarySavePath + ".bak");
    }

    // 성공 결과의 루트 인벤토리와 장비가 모두 창고에 지급되고 PendingResult가 비워져 중복 지급되지 않는지 검사합니다.
    [Test]
    public void SuccessfulRaid_IsAppliedToStashExactlyOnce()
    {
        var inventoryItem = new RaidItemSnapshot { itemId = "quest_radio", quantity = 1 };
        var equippedItem = new RaidItemSnapshot { itemId = "weapon_pistol", quantity = 1 };
        var result = new RaidResult
        {
            succeeded = true,
            endReason = RaidEndReason.Extracted,
            inventory = new RaidGridSnapshot()
        };
        result.inventory.items.Add(inventoryItem);
        result.equipment.Add(new RaidEquipmentSnapshot { slot = "SecondaryWeapon", item = equippedItem });
        RaidResultStore.Set(result);

        Assert.That(HideoutStateStore.ApplyPendingRaidResult(), Is.SameAs(result));
        Assert.That(HideoutStateStore.StashItems.Count, Is.EqualTo(2));
        Assert.That(RaidResultStore.PendingResult, Is.Null);

        // 원본 스냅샷을 바꿔도 창고 복사본이 변하지 않아야 씬 결과 객체를 정리한 뒤에도 보상이 안전하게 유지됩니다.
        inventoryItem.quantity = 99;
        Assert.That(HideoutStateStore.StashItems[0].quantity, Is.EqualTo(1));
        Assert.That(HideoutStateStore.ApplyPendingRaidResult(), Is.Null);
        Assert.That(HideoutStateStore.StashItems.Count, Is.EqualTo(2));
    }

    // 사망 결과는 요약을 반환하고 PendingResult를 소비하지만 어떤 창고 보상도 추가하지 않아야 합니다.
    [Test]
    public void FailedRaid_DoesNotAddStashReward()
    {
        var result = new RaidResult
        {
            succeeded = false,
            endReason = RaidEndReason.PlayerDied,
            lostItemCount = 7,
            inventory = new RaidGridSnapshot()
        };
        RaidResultStore.Set(result);

        Assert.That(HideoutStateStore.ApplyPendingRaidResult(), Is.SameAs(result));
        Assert.That(HideoutStateStore.StashItems, Is.Empty);
        Assert.That(HideoutStateStore.LoadoutItems, Is.Empty);
        Assert.That(RaidResultStore.PendingResult, Is.Null);
    }

    // 비정상 종료나 저장 순서 문제로 출격 준비 목록이 남아 있어도 사망 결과를 처리한 뒤 장비가 다시 나타나지 않아야 합니다.
    // 정상 플레이에서는 출격 씬 시작 때 이미 비워지지만, 이 두 번째 방어선이 사망 후 아이템 복구·복제 가능성을 차단합니다.
    [Test]
    public void PlayerDeath_ClearsAnyStalePreparedLoadout()
    {
        var preparedItem = new RaidItemSnapshot { itemId = "weapon_rifle", quantity = 1 };
        var previousSuccess = new RaidResult
        {
            succeeded = true,
            endReason = RaidEndReason.Extracted,
            inventory = new RaidGridSnapshot()
        };
        previousSuccess.inventory.items.Add(preparedItem);
        RaidResultStore.Set(previousSuccess);
        HideoutStateStore.ApplyPendingRaidResult();
        Assert.That(HideoutStateStore.MoveStashItemToLoadout(0), Is.True);
        Assert.That(HideoutStateStore.LoadoutItems.Count, Is.EqualTo(1));

        RaidResultStore.Set(new RaidResult
        {
            succeeded = false,
            endReason = RaidEndReason.PlayerDied,
            lostItemCount = 1,
            inventory = new RaidGridSnapshot()
        });
        HideoutStateStore.ApplyPendingRaidResult();

        Assert.That(HideoutStateStore.StashItems, Is.Empty);
        Assert.That(HideoutStateStore.LoadoutItems, Is.Empty);
    }

    // 한 묶음을 창고에서 출격 목록으로 옮겼다가 다시 돌려도 ItemId, 수량과 컨테이너 내용물이 유지되는지 검사합니다.
    [Test]
    public void StashAndLoadoutMovement_PreservesWholeSnapshot()
    {
        var backpack = new RaidItemSnapshot { itemId = "backpack_small", quantity = 1 };
        backpack.contents.Add(new RaidItemSnapshot { itemId = "ammo_9mm", quantity = 12 });
        var result = new RaidResult { succeeded = true, inventory = new RaidGridSnapshot() };
        result.inventory.items.Add(backpack);
        RaidResultStore.Set(result);
        HideoutStateStore.ApplyPendingRaidResult();

        Assert.That(HideoutStateStore.MoveStashItemToLoadout(0), Is.True);
        Assert.That(HideoutStateStore.StashItems, Is.Empty);
        Assert.That(HideoutStateStore.LoadoutItems.Count, Is.EqualTo(1));
        Assert.That(HideoutStateStore.LoadoutItems[0].contents[0].quantity, Is.EqualTo(12));

        Assert.That(HideoutStateStore.MoveLoadoutItemToStash(0), Is.True);
        Assert.That(HideoutStateStore.LoadoutItems, Is.Empty);
        Assert.That(HideoutStateStore.StashItems[0].itemId, Is.EqualTo("backpack_small"));
    }

    // 저장 좌표가 현재 격자 밖이어도 카탈로그로 정의를 찾고 빈칸에 재배치하며 가방 내부 아이템까지 복원하는지 검사합니다.
    [Test]
    public void SnapshotRestore_RebuildsContainerAndContentsInAvailableSpace()
    {
        ItemDefinition backpackDefinition = CreateDefinition("backpack_small", 2, 2, 1, 2, 2);
        ItemDefinition ammoDefinition = CreateDefinition("ammo_9mm", 1, 1, 30, 0, 0);
        ItemCatalog catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        GameObject player = new GameObject("HideoutRestoreTestPlayer");
        try
        {
            var catalogData = new SerializedObject(catalog);
            SerializedProperty definitions = catalogData.FindProperty("definitions");
            definitions.arraySize = 2;
            definitions.GetArrayElementAtIndex(0).objectReferenceValue = backpackDefinition;
            definitions.GetArrayElementAtIndex(1).objectReferenceValue = ammoDefinition;
            catalogData.ApplyModifiedPropertiesWithoutUndo();

            var snapshot = new RaidItemSnapshot
            {
                itemId = "backpack_small",
                quantity = 1,
                gridX = 99,
                gridY = 99
            };
            snapshot.contents.Add(new RaidItemSnapshot { itemId = "ammo_9mm", quantity = 12 });

            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            Assert.That(RaidSnapshotUtility.TryRestoreItem(inventory.Root, catalog, snapshot, out InventoryEntry restored), Is.True);
            Assert.That(restored.Position, Is.EqualTo(Vector2Int.zero));
            Assert.That(restored.Contents, Is.Not.Null);
            Assert.That(restored.Contents.Entries.Count, Is.EqualTo(1));
            Assert.That(restored.Contents.Entries[0].Item, Is.SameAs(ammoDefinition));
            Assert.That(restored.Contents.Entries[0].Quantity, Is.EqualTo(12));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(backpackDefinition);
            Object.DestroyImmediate(ammoDefinition);
        }
    }

    // Hideout에서 선택한 출격품을 RaidLoadoutApplier가 플레이어에게 한 번만 지급하고 출격 준비 목록을 소비하는지 통합 검사합니다.
    [Test]
    public void RaidLoadoutApplier_ConsumesPreparedItemsWithoutDuplication()
    {
        ItemDefinition ammoDefinition = CreateDefinition("ammo_9mm", 1, 1, 30, 0, 0);
        ItemCatalog catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        GameObject player = new GameObject("RaidLoadoutTestPlayer");
        GameObject applierObject = new GameObject("RaidLoadoutTestApplier");
        try
        {
            var catalogData = new SerializedObject(catalog);
            SerializedProperty definitions = catalogData.FindProperty("definitions");
            definitions.arraySize = 1;
            definitions.GetArrayElementAtIndex(0).objectReferenceValue = ammoDefinition;
            catalogData.ApplyModifiedPropertiesWithoutUndo();

            var result = new RaidResult { succeeded = true, inventory = new RaidGridSnapshot() };
            result.inventory.items.Add(new RaidItemSnapshot { itemId = "ammo_9mm", quantity = 12 });
            RaidResultStore.Set(result);
            HideoutStateStore.ApplyPendingRaidResult();
            Assert.That(HideoutStateStore.MoveStashItemToLoadout(0), Is.True);

            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            RaidLoadoutApplier applier = applierObject.AddComponent<RaidLoadoutApplier>();
            applier.Configure(inventory, catalog);
            applier.ApplyPreparedLoadout();

            Assert.That(HideoutStateStore.LoadoutItems, Is.Empty);
            Assert.That(inventory.Entries.Count, Is.EqualTo(1));
            Assert.That(inventory.Entries[0].Quantity, Is.EqualTo(12));
            Assert.That(applier.AppliedItemCount, Is.EqualTo(1));
            Assert.That(File.Exists(temporarySavePath), Is.True);

            // 같은 컴포넌트에서 다시 호출해도 소비된 목록을 재생성하거나 플레이어 아이템을 복제하면 안 됩니다.
            applier.ApplyPreparedLoadout();
            Assert.That(inventory.Entries.Count, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(applierObject);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(ammoDefinition);
        }
    }

    // 출격 목록을 비운 상태의 저장이 실패하면 플레이어가 아이템을 가진 채 레이드를 계속해서는 안 됩니다.
    // 이미 복원한 항목을 다시 창고로 한 번만 반환하고 테스트 권총도 지급하지 않는지 확인해 재시작 복제를 막습니다.
    [Test]
    public void RaidLoadoutApplier_SaveFailureRollsAppliedItemsBackToStash()
    {
        ItemDefinition ammoDefinition = CreateDefinition("rollback_ammo", 1, 1, 30, 0, 0);
        ItemCatalog catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        GameObject player = new GameObject("RaidLoadoutRollbackPlayer");
        GameObject applierObject = new GameObject("RaidLoadoutRollbackApplier");
        try
        {
            var catalogData = new SerializedObject(catalog);
            SerializedProperty definitions = catalogData.FindProperty("definitions");
            definitions.arraySize = 1;
            definitions.GetArrayElementAtIndex(0).objectReferenceValue = ammoDefinition;
            catalogData.ApplyModifiedPropertiesWithoutUndo();

            var result = new RaidResult { succeeded = true, inventory = new RaidGridSnapshot() };
            result.inventory.items.Add(new RaidItemSnapshot { itemId = "rollback_ammo", quantity = 12 });
            RaidResultStore.Set(result);
            HideoutStateStore.ApplyPendingRaidResult();
            Assert.That(HideoutStateStore.MoveStashItemToLoadout(0), Is.True);

            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            RaidLoadoutApplier applier = applierObject.AddComponent<RaidLoadoutApplier>();
            applier.Configure(inventory, catalog);
            // 빈 씬 이름은 저장 실패 뒤 실제 씬 전환을 생략해 이 테스트가 순수 상태만 검사하게 합니다.
            var applierData = new SerializedObject(applier);
            applierData.FindProperty("hideoutSceneName").stringValue = string.Empty;
            applierData.ApplyModifiedPropertiesWithoutUndo();
            HideoutSaveService.SetDefaultPathForTests("\0");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("^Hideout 저장 실패:"));
            LogAssert.Expect(LogType.Error, "출격품 소비 상태를 저장하지 못해 레이드를 취소하고 아이템을 거점으로 반환합니다.");

            applier.ApplyPreparedLoadout();

            Assert.That(applier.LoadoutApplicationFailed, Is.True);
            Assert.That(applier.AppliedItemCount, Is.Zero);
            Assert.That(applier.ReturnedItemCount, Is.EqualTo(1));
            Assert.That(inventory.Root.Entries, Is.Empty);
            Assert.That(inventory.Equipment, Is.Empty);
            Assert.That(HideoutStateStore.LoadoutItems, Is.Empty);
            Assert.That(HideoutStateStore.StashItems.Count, Is.EqualTo(1));
            Assert.That(HideoutStateStore.StashItems[0].itemId, Is.EqualTo("rollback_ammo"));
            Assert.That(HideoutStateStore.StashItems[0].quantity, Is.EqualTo(12));
        }
        finally
        {
            Object.DestroyImmediate(applierObject);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(ammoDefinition);
        }
    }

    // 출격 준비 목록이 비어 있어도 테스트 레이드는 보조무기 슬롯에 무료 권총을 실제 엔트리로 한 번만 장착해야 합니다.
    [Test]
    public void RaidLoadoutApplier_NoPreparedSidearm_EquipsOneProvidedPistol()
    {
        WeaponDefinition weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
        ItemDefinition pistol = CreateDefinition("weapon_pistol", 2, 1, 1, 0, 0);
        ItemCatalog catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        GameObject player = new GameObject("ProvidedPistolTestPlayer");
        GameObject applierObject = new GameObject("ProvidedPistolTestApplier");
        try
        {
            var pistolData = new SerializedObject(pistol);
            pistolData.FindProperty("itemType").enumValueIndex = (int)ItemType.Weapon;
            pistolData.FindProperty("equipmentSlot").enumValueIndex = (int)EquipmentSlotType.SecondaryWeapon;
            pistolData.FindProperty("weaponDefinition").objectReferenceValue = weapon;
            pistolData.ApplyModifiedPropertiesWithoutUndo();

            var catalogData = new SerializedObject(catalog);
            SerializedProperty definitions = catalogData.FindProperty("definitions");
            definitions.arraySize = 1;
            definitions.GetArrayElementAtIndex(0).objectReferenceValue = pistol;
            catalogData.ApplyModifiedPropertiesWithoutUndo();

            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            RaidLoadoutApplier applier = applierObject.AddComponent<RaidLoadoutApplier>();
            applier.Configure(inventory, catalog);
            applier.ApplyPreparedLoadout();

            Assert.That(applier.ProvidedTestSidearm, Is.True);
            Assert.That(inventory.Equipment.Count, Is.EqualTo(1));
            Assert.That(inventory.Equipment[EquipmentSlotType.SecondaryWeapon].Item, Is.SameAs(pistol));
            Assert.That(inventory.Equipment[EquipmentSlotType.SecondaryWeapon].IsRaidProvided, Is.True);

            // 같은 초기화 컴포넌트를 다시 호출해도 두 번째 권총이나 추가 장비 엔트리가 생기면 안 됩니다.
            applier.ApplyPreparedLoadout();
            Assert.That(inventory.Equipment.Count, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(applierObject);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(pistol);
            Object.DestroyImmediate(weapon);
        }
    }

    // 창고와 출격 목록을 JSON으로 저장한 뒤 메모리를 비우고 다시 불러와 중첩 contents까지 동일하게 복원되는지 검사합니다.
    [Test]
    public void JsonSaveAndLoad_RoundTripPreservesStashLoadoutAndContents()
    {
        var backpack = new RaidItemSnapshot { itemId = "backpack_small", quantity = 1 };
        backpack.contents.Add(new RaidItemSnapshot { itemId = "ammo_9mm", quantity = 17 });
        var medkit = new RaidItemSnapshot { itemId = "medkit_small", quantity = 2 };
        var result = new RaidResult { succeeded = true, inventory = new RaidGridSnapshot() };
        result.inventory.items.Add(backpack);
        result.inventory.items.Add(medkit);
        RaidResultStore.Set(result);
        HideoutStateStore.ApplyPendingRaidResult();
        Assert.That(HideoutStateStore.MoveStashItemToLoadout(0), Is.True);

        Assert.That(HideoutSaveService.SaveDefault(), Is.True);
        Assert.That(File.Exists(temporarySavePath), Is.True);
        // 같은 경로에 다시 저장해 기존 파일을 File.Replace로 교체하는 자동 저장 갱신 경로도 함께 검증합니다.
        Assert.That(HideoutSaveService.SaveDefault(), Is.True);
        HideoutStateStore.ResetForTests();
        Assert.That(HideoutStateStore.StashItems, Is.Empty);

        Assert.That(HideoutSaveService.LoadFromPath(temporarySavePath), Is.True);
        Assert.That(HideoutStateStore.StashItems.Count, Is.EqualTo(1));
        Assert.That(HideoutStateStore.StashItems[0].itemId, Is.EqualTo("medkit_small"));
        Assert.That(HideoutStateStore.LoadoutItems.Count, Is.EqualTo(1));
        Assert.That(HideoutStateStore.LoadoutItems[0].itemId, Is.EqualTo("backpack_small"));
        Assert.That(HideoutStateStore.LoadoutItems[0].contents[0].quantity, Is.EqualTo(17));
    }

    // 0발 탄창도 값이 없는 이전 저장과 구분되어 JSON 저장·불러오기 뒤 정확히 빈 상태로 유지되는지 검사합니다.
    [Test]
    public void JsonSaveAndLoad_PreservesEmptyWeaponMagazine()
    {
        var emptyPistol = new RaidItemSnapshot
        {
            itemId = "weapon_pistol",
            quantity = 1,
            hasMagazineState = true,
            magazineRounds = 0
        };
        var result = new RaidResult { succeeded = true, inventory = new RaidGridSnapshot() };
        result.inventory.items.Add(emptyPistol);
        RaidResultStore.Set(result);
        HideoutStateStore.ApplyPendingRaidResult();

        Assert.That(HideoutSaveService.SaveDefault(), Is.True);
        HideoutStateStore.ResetForTests();
        Assert.That(HideoutSaveService.LoadFromPath(temporarySavePath), Is.True);

        Assert.That(HideoutStateStore.StashItems.Count, Is.EqualTo(1));
        Assert.That(HideoutStateStore.StashItems[0].hasMagazineState, Is.True);
        Assert.That(HideoutStateStore.StashItems[0].magazineRounds, Is.EqualTo(0));
    }

    // 탄창 필드가 없던 버전 1 저장도 계속 읽어 사용자의 기존 창고를 삭제하지 않는지 검사합니다.
    [Test]
    public void VersionOneSave_RemainsLoadableAfterMagazineUpgrade()
    {
        File.WriteAllText(
            temporarySavePath,
            "{\"version\":1,\"stashItems\":[{\"itemId\":\"weapon_pistol\",\"quantity\":1,\"contents\":[]}],\"loadoutItems\":[]}");

        Assert.That(HideoutSaveService.LoadFromPath(temporarySavePath), Is.True);
        Assert.That(HideoutStateStore.StashItems.Count, Is.EqualTo(1));
        Assert.That(HideoutStateStore.StashItems[0].itemId, Is.EqualTo("weapon_pistol"));
        Assert.That(HideoutStateStore.StashItems[0].hasMagazineState, Is.False);
    }

    // 구조상 유효하지 않은 JSON 파일을 읽으면 false를 반환하고 현재 정상 창고 목록을 먼저 비우지 않는지 검사합니다.
    [Test]
    public void InvalidSaveFile_DoesNotOverwriteCurrentMemoryState()
    {
        var result = new RaidResult { succeeded = true, inventory = new RaidGridSnapshot() };
        result.inventory.items.Add(new RaidItemSnapshot { itemId = "quest_radio", quantity = 1 });
        RaidResultStore.Set(result);
        HideoutStateStore.ApplyPendingRaidResult();

        // JsonUtility가 파싱할 수는 있지만 필수 ItemId와 양수 Quantity가 빠진 데이터로 의미 검증 실패 경로를 재현합니다.
        File.WriteAllText(
            temporarySavePath,
            "{\"version\":1,\"stashItems\":[{\"itemId\":\"\",\"quantity\":0,\"contents\":[]}],\"loadoutItems\":[]}");

        Assert.That(HideoutSaveService.LoadFromPath(temporarySavePath), Is.False);
        Assert.That(HideoutStateStore.StashItems.Count, Is.EqualTo(1));
        Assert.That(HideoutStateStore.StashItems[0].itemId, Is.EqualTo("quest_radio"));
    }

    // Bootstrap과 Hideout이 같은 손상 파일을 연속 확인해도 최초 실패 결과가 성공으로 바뀌지 않는지 검사합니다.
    [Test]
    public void InvalidDefaultSave_RepeatedLoadKeepsFailureResult()
    {
        File.WriteAllText(temporarySavePath, "{\"version\":999,\"stashItems\":[],\"loadoutItems\":[]}");

        Assert.That(HideoutSaveService.LoadDefaultIfNeeded(), Is.False);
        Assert.That(HideoutSaveService.LoadDefaultIfNeeded(), Is.False);
        Assert.That(HideoutStateStore.StashItems, Is.Empty);
        Assert.That(HideoutStateStore.LoadoutItems, Is.Empty);
    }

    // 눈에 보이지 않는 앞뒤 공백만 다른 시설 ID 두 개를 허용하면 하나의 시설에 서로 다른 레벨이 저장될 수 있습니다.
    // 버전 5 불러오기는 ID를 정리한 뒤 중복을 검사하고, 손상 파일일 때 현재 메모리 상태를 건드리지 않아야 합니다.
    [Test]
    public void VersionFiveSave_RejectsFacilityIdsThatDuplicateAfterTrimming()
    {
        File.WriteAllText(
            temporarySavePath,
            "{\"version\":5,\"stashItems\":[],\"loadoutItems\":[]," +
            "\"facilityLevels\":[{\"facilityId\":\"storage\",\"level\":2},{\"facilityId\":\" storage \",\"level\":3}]," +
            "\"activeMissionId\":\"\",\"activeMissionZombieKills\":0,\"activeMissionProgress\":0,\"missionCompletions\":[]}");

        Assert.That(HideoutSaveService.LoadFromPath(temporarySavePath), Is.False);
        Assert.That(HideoutFacilityStateStore.CaptureState(), Is.Empty);
    }

    // 임무는 메모리에서 정상적으로 수락됐지만 디스크 저장만 실패한 경우, 성공 문구만 보여 주면 사용자가 저장된 것으로 오해할 수 있습니다.
    // 동작 결과는 true를 유지하되 보드 안내에는 자동 저장 실패가 포함되는지 확인합니다.
    [Test]
    public void MissionBoard_AcceptReportsAutomaticSaveFailure()
    {
        HideoutMissionDefinition mission = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
        GameObject boardObject = new GameObject("MissionBoardSaveFailureTest");
        try
        {
            mission.Configure("save_failure_mission", "저장 실패 임무", "테스트", 1, "테스트 보상");
            HideoutMissionBoard board = boardObject.AddComponent<HideoutMissionBoard>();
            board.Configure(new[] { mission });
            HideoutSaveService.SetDefaultPathForTests("\0");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("^Hideout 저장 실패:"));

            Assert.That(board.TryAcceptSelected(), Is.True);
            Assert.That(HideoutMissionStateStore.ActiveMissionId, Is.EqualTo("save_failure_mission"));
            Assert.That(board.LastOperationMessage, Does.Contain("자동 저장에 실패"));
        }
        finally
        {
            Object.DestroyImmediate(boardObject);
            Object.DestroyImmediate(mission);
        }
    }

    // Bootstrap 초기화가 디스크 창고를 복원하고 새 게임 실행에 남으면 안 되는 직전 RaidResult만 제거하는지 통합 검사합니다.
    [Test]
    public void BootstrapInitialization_LoadsPersistentStateAndClearsTransientResult()
    {
        var savedResult = new RaidResult { succeeded = true, inventory = new RaidGridSnapshot() };
        savedResult.inventory.items.Add(new RaidItemSnapshot { itemId = "medkit_small", quantity = 2 });
        RaidResultStore.Set(savedResult);
        HideoutStateStore.ApplyPendingRaidResult();
        Assert.That(HideoutSaveService.SaveDefault(), Is.True);

        // 새 실행처럼 메모리만 비우고 같은 임시 JSON 경로를 아직 읽지 않은 상태로 되돌립니다.
        HideoutStateStore.ResetForTests();
        HideoutSaveService.SetDefaultPathForTests(temporarySavePath);
        RaidResultStore.Set(new RaidResult { succeeded = false, lostItemCount = 99 });

        var bootstrapObject = new GameObject("BootstrapInitializationTest");
        bootstrapObject.SetActive(false);
        try
        {
            BootstrapController controller = bootstrapObject.AddComponent<BootstrapController>();
            Assert.That(controller.Initialize(), Is.True);
            Assert.That(controller.IsInitialized, Is.True);
            Assert.That(controller.StashStackCount, Is.EqualTo(1));
            Assert.That(HideoutStateStore.StashItems[0].itemId, Is.EqualTo("medkit_small"));
            Assert.That(RaidResultStore.PendingResult, Is.Null);

            // 두 번째 호출은 이미 복원된 목록을 다시 더하지 않고 같은 결과만 반환해야 합니다.
            Assert.That(controller.Initialize(), Is.True);
            Assert.That(controller.StashStackCount, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(bootstrapObject);
        }
    }

    // 출격 직전 저장이 실패하면 씬을 바꾸지 않고 거점에 남아야 이전 저장의 출격품이 다시 나타나는 복제 가능성을 막을 수 있습니다.
    [Test]
    public void StartRaid_SaveFailureCancelsSceneTransition()
    {
        HideoutSaveService.SetDefaultPathForTests("\0");
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("^Hideout 저장 실패:"));

        var hideoutObject = new GameObject("SaveFailureHideoutTest");
        try
        {
            HideoutController controller = hideoutObject.AddComponent<HideoutController>();

            Assert.That(controller.StartRaid(), Is.False);
            Assert.That(controller.OperationStatus, Does.Contain("출격을 취소"));
            Assert.That(controller.PersistenceStatus, Does.Contain("실패"));
        }
        finally
        {
            Object.DestroyImmediate(hideoutObject);
        }
    }

    // 한 시설은 설정된 최대 레벨까지만 올라가며 최대 레벨에서 다시 강화해도 저장 상태가 변하지 않아야 합니다.
    [Test]
    public void FacilityUpgrade_StopsAtConfiguredMaximumLevel()
    {
        HideoutFacilityDefinition facility = ScriptableObject.CreateInstance<HideoutFacilityDefinition>();
        try
        {
            facility.Configure("storage_test", "테스트 창고", "강화 한도 검사", HideoutFacilityType.Storage, 1, 3);

            Assert.That(HideoutFacilityStateStore.GetLevel(facility), Is.EqualTo(1));
            Assert.That(HideoutFacilityStateStore.TryUpgrade(facility), Is.True);
            Assert.That(HideoutFacilityStateStore.TryUpgrade(facility), Is.True);
            Assert.That(HideoutFacilityStateStore.GetLevel(facility), Is.EqualTo(3));
            Assert.That(HideoutFacilityStateStore.TryUpgrade(facility), Is.False);
            Assert.That(HideoutFacilityStateStore.GetLevel(facility), Is.EqualTo(3));
        }
        finally
        {
            Object.DestroyImmediate(facility);
        }
    }

    // 임무는 한 번에 하나만 받고 성공·사망 여부와 관계없이 처치 수를 누적하며 목표를 채운 뒤 완료 상태를 비울 수 있습니다.
    [Test]
    public void MissionProgress_AcceptsOneMissionAccumulatesKillsAndCanBeAbandoned()
    {
        HideoutMissionDefinition first = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
        HideoutMissionDefinition second = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
        try
        {
            first.Configure("mission_first", "첫 임무", "테스트", 5, "테스트 보상");
            second.Configure("mission_second", "둘째 임무", "테스트", 8, "테스트 보상");

            Assert.That(HideoutMissionStateStore.TryAccept(first), Is.True);
            Assert.That(HideoutMissionStateStore.TryAccept(second), Is.False);
            HideoutMissionStateStore.ApplyRaidResult(new RaidResult { succeeded = true, zombieKills = 2 });
            HideoutMissionStateStore.ApplyRaidResult(new RaidResult { succeeded = false, zombieKills = 3 });

            Assert.That(HideoutMissionStateStore.ActiveMissionId, Is.EqualTo("mission_first"));
            Assert.That(HideoutMissionStateStore.ZombieKillProgress, Is.EqualTo(5));
            Assert.That(HideoutMissionStateStore.CanComplete(first), Is.True);
            Assert.That(HideoutMissionStateStore.TryComplete(first), Is.True);
            Assert.That(HideoutMissionStateStore.HasActiveMission, Is.False);
            Assert.That(HideoutMissionStateStore.ZombieKillProgress, Is.Zero);

            // 완료 뒤에는 다른 임무를 새로 받을 수 있고 포기하면 다시 빈 상태로 돌아갑니다.
            Assert.That(HideoutMissionStateStore.TryAccept(second), Is.True);
            Assert.That(HideoutMissionStateStore.Abandon(), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }
    }

    // 버전 5 JSON은 아이템뿐 아니라 시설 레벨, 공통 임무 진행도와 완료 기록까지 저장하고 같은 값으로 복원해야 합니다.
    [Test]
    public void VersionFiveSave_RoundTripPreservesFacilityAndMissionProgress()
    {
        HideoutFacilityDefinition facility = ScriptableObject.CreateInstance<HideoutFacilityDefinition>();
        HideoutMissionDefinition mission = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
        try
        {
            facility.Configure("storage_save_test", "저장 테스트 창고", "테스트", HideoutFacilityType.Storage, 1, 5);
            mission.Configure("mission_save_test", "저장 테스트 임무", "테스트", 10, "테스트 보상");
            Assert.That(HideoutFacilityStateStore.TryUpgrade(facility), Is.True);
            Assert.That(HideoutFacilityStateStore.TryUpgrade(facility), Is.True);
            Assert.That(HideoutMissionStateStore.TryAccept(mission), Is.True);
            HideoutMissionStateStore.ApplyRaidResult(new RaidResult { zombieKills = 4 });

            Assert.That(HideoutSaveService.SaveDefault(), Is.True);
            Assert.That(File.ReadAllText(temporarySavePath), Does.Contain("\"version\": 5"));

            HideoutStateStore.ResetForTests();
            Assert.That(HideoutSaveService.LoadFromPath(temporarySavePath), Is.True);
            Assert.That(HideoutFacilityStateStore.GetLevel(facility), Is.EqualTo(3));
            Assert.That(HideoutMissionStateStore.ActiveMissionId, Is.EqualTo("mission_save_test"));
            Assert.That(HideoutMissionStateStore.ZombieKillProgress, Is.EqualTo(4));
        }
        finally
        {
            Object.DestroyImmediate(facility);
            Object.DestroyImmediate(mission);
        }
    }

    // 버전 4의 좀비 처치 전용 진행도는 버전 5의 공통 진행도로 옮겨지고 완료 기록은 빈 목록으로 시작해야 합니다.
    [Test]
    public void VersionFourSave_LoadsMissionProgressIntoVersionFiveState()
    {
        File.WriteAllText(
            temporarySavePath,
            "{\"version\":4,\"stashItems\":[],\"loadoutItems\":[]," +
            "\"facilityLevels\":[{\"facilityId\":\"storage\",\"level\":2}]," +
            "\"activeMissionId\":\"legacy_kills\",\"activeMissionZombieKills\":6}");

        Assert.That(HideoutSaveService.LoadFromPath(temporarySavePath), Is.True);
        Assert.That(HideoutMissionStateStore.ActiveMissionId, Is.EqualTo("legacy_kills"));
        Assert.That(HideoutMissionStateStore.ObjectiveProgress, Is.EqualTo(6));
        Assert.That(HideoutMissionStateStore.CaptureCompletionState(), Is.Empty);
        Assert.That(HideoutFacilityStateStore.CaptureState()[0].level, Is.EqualTo(2));
    }

    // 회수 임무는 성공 결과 안의 가방 내용물까지 세고, 탈출 임무는 실패 레이드가 아닌 성공 레이드만 진행돼야 합니다.
    [Test]
    public void MissionObjectives_CountCollectedItemsAndSuccessfulExtractions()
    {
        ItemDefinition targetItem = CreateDefinition("mission_food", 1, 1, 5, 0, 0);
        HideoutMissionDefinition collection = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
        HideoutMissionDefinition extraction = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
        try
        {
            collection.Configure(
                "collect_food",
                "식량 회수",
                "테스트",
                HideoutMissionObjectiveType.CollectedItem,
                targetItem,
                3,
                true,
                new HideoutMissionItemReward(targetItem, 1));
            extraction.Configure(
                "extract_once",
                "무사 귀환",
                "테스트",
                HideoutMissionObjectiveType.SuccessfulExtraction,
                null,
                1,
                true,
                new HideoutMissionItemReward(targetItem, 1));

            Assert.That(HideoutMissionStateStore.TryAccept(collection), Is.True);
            var result = new RaidResult { succeeded = true, inventory = new RaidGridSnapshot() };
            var backpack = new RaidItemSnapshot { itemId = "backpack", quantity = 1 };
            backpack.contents.Add(new RaidItemSnapshot { itemId = "mission_food", quantity = 3 });
            result.inventory.items.Add(backpack);
            HideoutMissionStateStore.ApplyRaidResult(result, collection);
            Assert.That(HideoutMissionStateStore.ObjectiveProgress, Is.EqualTo(3));
            Assert.That(HideoutMissionStateStore.TryComplete(collection), Is.True);

            Assert.That(HideoutMissionStateStore.TryAccept(extraction), Is.True);
            HideoutMissionStateStore.ApplyRaidResult(new RaidResult { succeeded = false }, extraction);
            Assert.That(HideoutMissionStateStore.ObjectiveProgress, Is.Zero);
            HideoutMissionStateStore.ApplyRaidResult(new RaidResult { succeeded = true }, extraction);
            Assert.That(HideoutMissionStateStore.ObjectiveProgress, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(targetItem);
            Object.DestroyImmediate(collection);
            Object.DestroyImmediate(extraction);
        }
    }

    // 보상 여러 개 중 하나라도 들어갈 수 없으면 실제 창고는 전혀 바뀌지 않고, 공간이 생긴 뒤에는 전체 보상이 지급돼야 합니다.
    [Test]
    public void MissionReward_UsesAllOrNothingStorageCapacityCheck()
    {
        ItemDefinition blocker = CreateDefinition("blocker", 1, 1, 1, 0, 0);
        ItemDefinition rewardItem = CreateDefinition("reward", 1, 1, 10, 0, 0);
        HideoutMissionDefinition mission = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
        try
        {
            mission.Configure(
                "reward_test",
                "보상 테스트",
                "테스트",
                HideoutMissionObjectiveType.ZombieKills,
                null,
                1,
                true,
                new HideoutMissionItemReward(rewardItem, 4));
            var stash = new PlayerInventory.GridData(1, 1);
            stash.Entries.Add(new InventoryEntry(blocker, 1, Vector2Int.zero));

            Assert.That(HideoutMissionRewardService.TryGrant(stash, mission), Is.False);
            Assert.That(stash.Entries.Count, Is.EqualTo(1));
            Assert.That(stash.Entries[0].Item, Is.SameAs(blocker));

            stash.Entries.Clear();
            Assert.That(HideoutMissionRewardService.TryGrant(stash, mission), Is.True);
            Assert.That(stash.Entries.Count, Is.EqualTo(1));
            Assert.That(stash.Entries[0].Item, Is.SameAs(rewardItem));
            Assert.That(stash.Entries[0].Quantity, Is.EqualTo(4));
        }
        finally
        {
            Object.DestroyImmediate(blocker);
            Object.DestroyImmediate(rewardItem);
            Object.DestroyImmediate(mission);
        }
    }

    // 일회성 임무는 한 번 완료한 뒤 다시 받을 수 없고 완료 횟수는 저장 파일을 거쳐도 유지돼야 합니다.
    [Test]
    public void NonRepeatableMission_CompletionHistoryPersistsAndBlocksAcceptance()
    {
        ItemDefinition rewardItem = CreateDefinition("history_reward", 1, 1, 10, 0, 0);
        HideoutMissionDefinition mission = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
        try
        {
            mission.Configure(
                "one_time",
                "일회성 임무",
                "테스트",
                HideoutMissionObjectiveType.ZombieKills,
                null,
                1,
                false,
                new HideoutMissionItemReward(rewardItem, 1));
            Assert.That(HideoutMissionStateStore.TryAccept(mission), Is.True);
            HideoutMissionStateStore.ApplyRaidResult(new RaidResult { zombieKills = 1 }, mission);
            Assert.That(HideoutMissionStateStore.TryComplete(mission), Is.True);
            Assert.That(HideoutMissionStateStore.GetCompletionCount("one_time"), Is.EqualTo(1));
            Assert.That(HideoutMissionStateStore.TryAccept(mission), Is.False);

            Assert.That(HideoutSaveService.SaveDefault(), Is.True);
            HideoutStateStore.ResetForTests();
            Assert.That(HideoutSaveService.LoadFromPath(temporarySavePath), Is.True);
            Assert.That(HideoutMissionStateStore.GetCompletionCount("one_time"), Is.EqualTo(1));
            Assert.That(HideoutMissionStateStore.TryAccept(mission), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(rewardItem);
            Object.DestroyImmediate(mission);
        }
    }

    // 발전기 할인은 각 재료 수량에 적용되고, 재료 소비 뒤 결과가 들어갈 공간까지 없으면 원본 창고를 바꾸지 않아야 합니다.
    [Test]
    public void Crafting_UsesGeneratorDiscountAndKeepsInventoryWhenOutputCannotFit()
    {
        ItemDefinition scrap = CreateDefinition("craft_scrap", 1, 1, 20, 0, 0);
        ItemDefinition output = CreateDefinition("craft_output", 1, 1, 20, 0, 0);
        HideoutFacilityDefinition bench = ScriptableObject.CreateInstance<HideoutFacilityDefinition>();
        HideoutFacilityDefinition generator = ScriptableObject.CreateInstance<HideoutFacilityDefinition>();
        HideoutRecipeDefinition recipe = ScriptableObject.CreateInstance<HideoutRecipeDefinition>();
        try
        {
            bench.Configure("bench_test", "제작대", "테스트", HideoutFacilityType.CraftingBench, 1, 3, 1, 1, "제작법 수");
            generator.Configure("generator_test", "발전기", "테스트", HideoutFacilityType.Generator, 1, 3, 0, 10, "할인율");
            recipe.Configure(
                "craft_test",
                "제작 테스트",
                HideoutFacilityType.CraftingBench,
                1,
                output,
                4,
                0,
                new HideoutFacilityItemCost(scrap, 10));

            var blockedStash = new PlayerInventory.GridData(1, 1);
            blockedStash.Entries.Add(new InventoryEntry(scrap, 12, Vector2Int.zero));
            Assert.That(HideoutCraftingService.TryCraft(blockedStash, bench, generator, recipe, out _), Is.False);
            Assert.That(blockedStash.Entries[0].Quantity, Is.EqualTo(12));

            // 발전기를 2레벨로 올리면 비용 10에서 10%가 줄어 고철 9개만 소비됩니다.
            Assert.That(HideoutFacilityStateStore.TryUpgrade(generator), Is.True);
            var stash = new PlayerInventory.GridData(2, 1);
            stash.Entries.Add(new InventoryEntry(scrap, 12, Vector2Int.zero));
            Assert.That(HideoutCraftingService.TryCraft(stash, bench, generator, recipe, out string message), Is.True);
            Assert.That(stash.Entries[0].Quantity, Is.EqualTo(3));
            Assert.That(stash.Entries[1].Item, Is.SameAs(output));
            Assert.That(stash.Entries[1].Quantity, Is.EqualTo(4));
            Assert.That(message, Does.Contain("10%"));
        }
        finally
        {
            Object.DestroyImmediate(scrap);
            Object.DestroyImmediate(output);
            Object.DestroyImmediate(bench);
            Object.DestroyImmediate(generator);
            Object.DestroyImmediate(recipe);
        }
    }

    // 시설 정의는 현재 레벨에 해당하는 비용과 효과만 반환하고 최대 레벨을 넘겨 강화되지 않아야 합니다.
    [Test]
    public void FacilityDefinition_ProvidesLevelCostsAndScaledEffects()
    {
        ItemDefinition scrap = CreateDefinition("upgrade_scrap", 1, 1, 20, 0, 0);
        HideoutFacilityDefinition facility = ScriptableObject.CreateInstance<HideoutFacilityDefinition>();
        try
        {
            facility.Configure(
                "scaled_storage",
                "확장 창고",
                "테스트",
                HideoutFacilityType.Storage,
                1,
                3,
                0,
                2,
                "추가 행",
                new HideoutFacilityUpgradeStep(2, new HideoutFacilityItemCost(scrap, 3)),
                new HideoutFacilityUpgradeStep(3, new HideoutFacilityItemCost(scrap, 5)));

            Assert.That(facility.GetEffectValue(1), Is.Zero);
            Assert.That(facility.GetEffectValue(3), Is.EqualTo(4));
            Assert.That(facility.GetUpgradeCosts(1)[0].quantity, Is.EqualTo(3));
            Assert.That(facility.GetUpgradeCosts(2)[0].quantity, Is.EqualTo(5));
            Assert.That(HideoutFacilityStateStore.TryUpgrade(facility), Is.True);
            Assert.That(HideoutFacilityStateStore.TryUpgrade(facility), Is.True);
            Assert.That(HideoutFacilityStateStore.TryUpgrade(facility), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(scrap);
            Object.DestroyImmediate(facility);
        }
    }

    // 테스트에서 사용할 ItemDefinition의 ID, 격자 크기, 중첩과 선택적 컨테이너 크기를 메모리에 설정합니다.
    private static ItemDefinition CreateDefinition(
        string itemId,
        int width,
        int height,
        int maxStack,
        int containerWidth,
        int containerHeight)
    {
        ItemDefinition definition = ScriptableObject.CreateInstance<ItemDefinition>();
        var data = new SerializedObject(definition);
        data.FindProperty("itemId").stringValue = itemId;
        data.FindProperty("displayName").stringValue = itemId;
        data.FindProperty("gridWidth").intValue = width;
        data.FindProperty("gridHeight").intValue = height;
        data.FindProperty("maxStack").intValue = maxStack;
        data.FindProperty("containerWidth").intValue = containerWidth;
        data.FindProperty("containerHeight").intValue = containerHeight;
        data.ApplyModifiedPropertiesWithoutUndo();
        return definition;
    }

    private static void DeleteIfExists(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
    }
}
