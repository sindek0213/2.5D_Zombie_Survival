using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

public sealed class InventoryTests
{
    private GameObject player;
    private ItemDefinition definition;

    [SetUp]
    public void SetUp()
    {
        // 테스트용 아이템 정의와 플레이어 인벤토리를 메모리에 생성합니다.
        definition = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(definition);
        serialized.FindProperty("itemId").stringValue = "ammo_test";
        serialized.FindProperty("displayName").stringValue = "테스트 탄약";
        serialized.FindProperty("maxStack").intValue = 30;
        serialized.FindProperty("gridWidth").intValue = 1;
        serialized.FindProperty("gridHeight").intValue = 1;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        player = new GameObject("InventoryTestPlayer");
        player.AddComponent<PlayerInventory>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(player);
        Object.DestroyImmediate(definition);
    }

    // MaxStack 30인 아이템 35개를 추가했을 때 30과 5 두 묶음으로 나뉘는지 확인합니다.
    // 최대 중첩을 넘거나 전체 수량 일부가 사라지는 회귀 오류를 방지하는 테스트입니다.
    [Test]
    public void Add_UsesMultipleSlotsWhenStackIsFull()
    {
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        Assert.That(inventory.TryAdd(definition, 35), Is.True);
        Assert.That(inventory.Entries[0].Quantity, Is.EqualTo(30));
        Assert.That(inventory.Entries[1].Quantity, Is.EqualTo(5));
    }

    // 전체 슬롯 공간보다 많은 수량은 일부만 넣지 않고 거부하는지 검증합니다.
    [Test]
    public void Add_WhenCapacityIsInsufficient_DoesNotChangeInventory()
    {
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        Assert.That(inventory.TryAdd(definition, 1801), Is.False);
        Assert.That(inventory.Entries, Is.Empty);
    }

    // 탄창·내구도·내부 격자처럼 묶음마다 다른 상태를 가진 아이템은 Inspector의 maxStack 값이 잘못되어도 중첩되면 안 됩니다.
    // 실제 InventoryEntry 생성 수량까지 1로 제한되는지 확인해 합치기 과정에서 개별 상태가 사라지는 회귀를 막습니다.
    [TestCase(ItemType.Weapon)]
    [TestCase(ItemType.Armor)]
    [TestCase(ItemType.Container)]
    public void StatefulItems_AlwaysUseSingleItemStacks(ItemType itemType)
    {
        ItemDefinition statefulItem = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            var serialized = new SerializedObject(statefulItem);
            serialized.FindProperty("itemId").stringValue = "stateful_test";
            serialized.FindProperty("displayName").stringValue = "상태 보존 테스트";
            serialized.FindProperty("itemType").enumValueIndex = (int)itemType;
            serialized.FindProperty("maxStack").intValue = 20;
            if (itemType == ItemType.Container)
            {
                serialized.FindProperty("containerWidth").intValue = 4;
                serialized.FindProperty("containerHeight").intValue = 3;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var entry = new InventoryEntry(statefulItem, 5, Vector2Int.zero);
            Assert.That(statefulItem.CanStack, Is.False);
            Assert.That(statefulItem.MaxStack, Is.EqualTo(1));
            Assert.That(entry.Quantity, Is.EqualTo(1));
            Assert.That(entry.Split(1), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(statefulItem);
        }
    }

    // 다중 칸 아이템이 회전하면 가로와 세로 점유 크기가 교환되는지 검증합니다.
    [Test]
    public void Rotate_SwapsGridDimensions()
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty("gridWidth").intValue = 2;
        serialized.FindProperty("gridHeight").intValue = 1;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        Assert.That(inventory.TryAdd(definition, 1), Is.True);
        InventoryEntry entry = inventory.Entries[0];
        Assert.That(inventory.TryRotate(entry), Is.True);
        Assert.That(entry.Width, Is.EqualTo(1));
        Assert.That(entry.Height, Is.EqualTo(2));
    }

    // 테스트 아이템을 Backpack 장비로 설정하고 장착했을 때 루트에서 제거되고 장비 Dictionary로 이동하는지 확인합니다.
    // 동시에 정의의 5x4 크기가 새 컨테이너 Contents에 정확히 복사되는지도 검증합니다.
    [Test]
    public void Equip_MovesEntryIntoMatchingSlot()
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty("equipmentSlot").enumValueIndex = (int)EquipmentSlotType.Backpack;
        serialized.FindProperty("containerWidth").intValue = 5;
        serialized.FindProperty("containerHeight").intValue = 4;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        inventory.TryAdd(definition, 1);
        InventoryEntry entry = inventory.Entries[0];
        Assert.That(inventory.TryEquip(inventory.Root, entry), Is.True);
        Assert.That(inventory.Equipment[EquipmentSlotType.Backpack], Is.SameAs(entry));
        Assert.That(entry.Contents.Width, Is.EqualTo(5));
    }

    // 수량 20 묶음을 절반 분할한 뒤 두 엔트리가 서로 다른 Position을 가지는지 확인합니다.
    // 새 묶음이 원본의 점유 칸 위에 겹쳐 생성됐던 회귀 가능성을 방지합니다.
    [Test]
    public void Split_CreatesNonOverlappingEntry()
    {
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        inventory.TryAdd(definition, 20);
        InventoryEntry original = inventory.Entries[0];
        Assert.That(inventory.TrySplit(inventory.Root, original), Is.True);
        Assert.That(inventory.Entries.Count, Is.EqualTo(2));
        Assert.That(inventory.Entries[0].Position, Is.Not.EqualTo(inventory.Entries[1].Position));
    }

    // 분할해서 서로 다른 칸에 놓인 같은 탄약 묶음을 한쪽 위치로 드롭하면 다시 합쳐지는지 검사합니다.
    // 합친 뒤 엔트리는 하나만 남고, 분할 전 전체 수량 20이 그대로 보존되어야 합니다.
    [Test]
    public void Move_SplitStacksOntoEachOther_MergesThemAgain()
    {
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        Assert.That(inventory.TryAdd(definition, 20), Is.True);

        InventoryEntry original = inventory.Entries[0];
        Assert.That(inventory.TrySplit(inventory.Root, original), Is.True);
        InventoryEntry split = inventory.Entries[1];

        Assert.That(inventory.TryMove(inventory.Root, inventory.Root, split, original.Position), Is.True);
        Assert.That(inventory.Entries.Count, Is.EqualTo(1));
        Assert.That(inventory.Entries[0].Quantity, Is.EqualTo(20));
    }

    // 서로 다른 1x1 아이템 두 개가 있을 때 한 아이템을 다른 아이템 위로 드롭하면 위치가 교환되는지 검사합니다.
    // 교환 과정에서 어느 아이템도 사라지지 않고 각각 상대방이 사용하던 좌표로 이동해야 합니다.
    [Test]
    public void Move_DifferentItemsOntoEachOther_SwapsTheirPositions()
    {
        ItemDefinition secondDefinition = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            // 테스트 전용 두 번째 아이템은 첫 번째 아이템과 다른 ItemDefinition 객체여야 합치기가 아니라 교환이 실행됩니다.
            var serialized = new SerializedObject(secondDefinition);
            serialized.FindProperty("itemId").stringValue = "healing_test";
            serialized.FindProperty("displayName").stringValue = "테스트 치료제";
            serialized.FindProperty("maxStack").intValue = 1;
            serialized.FindProperty("gridWidth").intValue = 1;
            serialized.FindProperty("gridHeight").intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            Assert.That(inventory.TryAdd(definition, 1), Is.True);
            Assert.That(inventory.TryAdd(secondDefinition, 1), Is.True);

            InventoryEntry first = inventory.Entries[0];
            InventoryEntry second = inventory.Entries[1];
            Vector2Int firstPosition = first.Position;
            Vector2Int secondPosition = second.Position;

            Assert.That(inventory.TryMove(inventory.Root, inventory.Root, second, firstPosition), Is.True);
            Assert.That(first.Position, Is.EqualTo(secondPosition));
            Assert.That(second.Position, Is.EqualTo(firstPosition));
            Assert.That(inventory.Entries.Count, Is.EqualTo(2));
        }
        finally
        {
            // 메모리에 만든 ScriptableObject를 테스트가 끝난 즉시 제거해 다음 테스트에 상태가 남지 않게 합니다.
            Object.DestroyImmediate(secondDefinition);
        }
    }

    // 2x2 바운딩 안에서 오른쪽 위가 비어 있는 L자 아이템을 만든 뒤, 그 빈 셀에 1x1 아이템이 들어가는지 검사합니다.
    // 이 테스트가 통과하면 에디터에서 그린 모양이 표시용 데이터가 아니라 실제 자동 배치와 충돌 계산에 사용된다는 뜻입니다.
    [Test]
    public void Add_CustomLShape_AllowsAnotherItemInsideEmptyCell()
    {
        var shapeData = new SerializedObject(definition);
        shapeData.FindProperty("gridWidth").intValue = 2;
        shapeData.FindProperty("gridHeight").intValue = 2;
        SerializedProperty shape = shapeData.FindProperty("gridShape");
        shape.arraySize = 4;
        shape.GetArrayElementAtIndex(0).boolValue = true;
        shape.GetArrayElementAtIndex(1).boolValue = false;
        shape.GetArrayElementAtIndex(2).boolValue = true;
        shape.GetArrayElementAtIndex(3).boolValue = true;
        shapeData.ApplyModifiedPropertiesWithoutUndo();

        ItemDefinition singleCellDefinition = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            var singleCellData = new SerializedObject(singleCellDefinition);
            singleCellData.FindProperty("itemId").stringValue = "single_cell_test";
            singleCellData.FindProperty("displayName").stringValue = "한 칸 테스트 아이템";
            singleCellData.FindProperty("maxStack").intValue = 1;
            singleCellData.FindProperty("gridWidth").intValue = 1;
            singleCellData.FindProperty("gridHeight").intValue = 1;
            singleCellData.ApplyModifiedPropertiesWithoutUndo();

            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            Assert.That(inventory.TryAdd(definition, 1), Is.True);
            Assert.That(inventory.TryAdd(singleCellDefinition, 1), Is.True);

            InventoryEntry lShape = inventory.Entries[0];
            InventoryEntry singleCell = inventory.Entries[1];
            Assert.That(lShape.Position, Is.EqualTo(new Vector2Int(0, 0)));
            Assert.That(singleCell.Position, Is.EqualTo(new Vector2Int(1, 0)));
            Assert.That(lShape.OccupiesGridCell(singleCell.Position.x, singleCell.Position.y), Is.False);
        }
        finally
        {
            // 테스트 중 메모리에 만든 두 번째 정의를 즉시 제거해 다른 테스트의 에셋 상태에 영향을 주지 않게 합니다.
            Object.DestroyImmediate(singleCellDefinition);
        }
    }

    // 총알을 소총의 오른쪽 끝 셀에 놓았을 때 총알은 사용자가 놓은 셀에 남고 소총만 가장 가까운 유효 위치로 비켜나는지 검사합니다.
    // 이전 방식처럼 두 아이템의 기준 좌표를 강제로 맞바꾸면 소총과 총알이 서로 멀리 튀므로 수동 드롭 위치와 지역 이동을 함께 고정합니다.
    [Test]
    public void Move_AmmoOntoRifleEnd_KeepsDropCellAndMovesRifleLocally()
    {
        ItemDefinition rifleDefinition = CreateRifleDefinition();
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            var rifle = new InventoryEntry(rifleDefinition, 1, new Vector2Int(0, 1));
            var ammo = new InventoryEntry(definition, 1, new Vector2Int(4, 0));
            inventory.Root.Entries.Add(rifle);
            inventory.Root.Entries.Add(ammo);

            // 소총은 기존 위치에서 위로 한 칸만 이동하면 총알을 피할 수 있으므로 인벤토리 반대편까지 이동하면 안 됩니다.
            Assert.That(inventory.TryMove(inventory.Root, inventory.Root, ammo, new Vector2Int(4, 1)), Is.True);
            Assert.That(ammo.Position, Is.EqualTo(new Vector2Int(4, 1)));
            Assert.That(rifle.Position, Is.EqualTo(new Vector2Int(0, 0)));
            Assert.That(rifle.OccupiesGridCell(ammo.Position.x, ammo.Position.y), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(rifleDefinition);
        }
    }

    // 작은 총알을 소총의 기준 셀에 놓았을 때 총알은 드롭 셀을 유지하고 소총은 기존 자리에서 가장 가까운 칸으로 이동하는지 검사합니다.
    // 큰 아이템을 작은 아이템의 과거 위치로 보내는 전역 교환 대신 실제 충돌을 해소하는 최소 이동만 수행해야 합니다.
    [Test]
    public void Move_AmmoOntoRifleAnchor_UsesNearestRiflePosition()
    {
        ItemDefinition rifleDefinition = CreateRifleDefinition();
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            var ammo = new InventoryEntry(definition, 1, new Vector2Int(0, 0));
            var rifle = new InventoryEntry(rifleDefinition, 1, new Vector2Int(1, 0));
            inventory.Root.Entries.Add(ammo);
            inventory.Root.Entries.Add(rifle);

            Assert.That(inventory.TryMove(inventory.Root, inventory.Root, ammo, rifle.Position), Is.True);
            Assert.That(ammo.Position, Is.EqualTo(new Vector2Int(1, 0)));
            Assert.That(rifle.Position, Is.EqualTo(new Vector2Int(2, 0)));
            Assert.That(rifle.OccupiesGridCell(ammo.Position.x, ammo.Position.y), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(rifleDefinition);
        }
    }

    // 큰 소총을 작은 총알 위로 직접 드래그하면 소총은 사용자가 놓은 위치를 유지하고 총알만 가장 가까운 빈 모양 셀로 비켜나는지 검사합니다.
    // 수동 정리에서는 잡은 아이템이 마우스를 놓은 기준 좌표에 남아야 화면에서 본 드롭 위치와 실제 결과가 일치합니다.
    [Test]
    public void Move_RifleOntoAmmo_KeepsRifleAtDroppedPosition()
    {
        ItemDefinition rifleDefinition = CreateRifleDefinition();
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            var ammo = new InventoryEntry(definition, 1, new Vector2Int(0, 0));
            var rifle = new InventoryEntry(rifleDefinition, 1, new Vector2Int(1, 0));
            inventory.Root.Entries.Add(ammo);
            inventory.Root.Entries.Add(rifle);

            Assert.That(inventory.TryMove(inventory.Root, inventory.Root, rifle, ammo.Position), Is.True);
            Assert.That(rifle.Position, Is.EqualTo(new Vector2Int(0, 0)));
            Assert.That(ammo.Position, Is.EqualTo(new Vector2Int(1, 1)));
            Assert.That(rifle.OccupiesGridCell(ammo.Position.x, ammo.Position.y), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(rifleDefinition);
        }
    }

    // 소총을 세 개의 총알 위에 동시에 놓아도 즉시 취소하지 않고, 소총은 드롭 위치에 두고 총알들을 가까운 빈칸으로 각각 이동하는지 검사합니다.
    // 여러 총알의 재배치 결과는 서로 겹치면 안 되며 모든 엔트리와 수량이 그대로 보존되어야 합니다.
    [Test]
    public void Move_RifleOverMultipleAmmo_RelocatesEveryOverlapWithoutCollision()
    {
        ItemDefinition rifleDefinition = CreateRifleDefinition();
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            var rifle = new InventoryEntry(rifleDefinition, 1, new Vector2Int(0, 3));
            var firstAmmo = new InventoryEntry(definition, 1, new Vector2Int(2, 0));
            var secondAmmo = new InventoryEntry(definition, 1, new Vector2Int(4, 0));
            var thirdAmmo = new InventoryEntry(definition, 1, new Vector2Int(2, 1));
            inventory.Root.Entries.Add(rifle);
            inventory.Root.Entries.Add(firstAmmo);
            inventory.Root.Entries.Add(secondAmmo);
            inventory.Root.Entries.Add(thirdAmmo);

            Assert.That(inventory.TryMove(inventory.Root, inventory.Root, rifle, new Vector2Int(2, 0)), Is.True);
            Assert.That(rifle.Position, Is.EqualTo(new Vector2Int(2, 0)));
            Assert.That(firstAmmo.Position, Is.EqualTo(new Vector2Int(1, 0)));
            Assert.That(secondAmmo.Position, Is.EqualTo(new Vector2Int(3, 1)));
            Assert.That(thirdAmmo.Position, Is.EqualTo(new Vector2Int(1, 1)));
            Assert.That(inventory.Entries.Count, Is.EqualTo(4));
            AssertGridIsValid(inventory.Root);
        }
        finally
        {
            Object.DestroyImmediate(rifleDefinition);
        }
    }

    // 다른 격자로 이동하며 밀려나는 아이템 중 하나라도 목적 격자에 들어갈 수 없으면 전체 이동을 취소하는지 검사합니다.
    // 계획 도중 앞쪽 아이템의 좌표나 목록을 먼저 바꾸지 않아야 실패 후에도 두 격자가 시작 상태를 정확히 유지합니다.
    [Test]
    public void Move_MultipleOverlapsWithoutDestinationSpace_RollsBackEverything()
    {
        ItemDefinition horizontalDefinition = CreateRectangularDefinition("horizontal_test", 2, 1);
        ItemDefinition verticalDefinition = CreateRectangularDefinition("vertical_test", 1, 2);
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            var source = new PlayerInventory.GridData(2, 1);
            var target = new PlayerInventory.GridData(4, 2);
            var horizontal = new InventoryEntry(horizontalDefinition, 1, Vector2Int.zero);
            var firstVertical = new InventoryEntry(verticalDefinition, 1, Vector2Int.zero);
            var secondVertical = new InventoryEntry(verticalDefinition, 1, new Vector2Int(1, 0));
            source.Entries.Add(horizontal);
            target.Entries.Add(firstVertical);
            target.Entries.Add(secondVertical);

            Assert.That(inventory.TryMove(source, target, horizontal, Vector2Int.zero), Is.False);
            Assert.That(source.Entries.Count, Is.EqualTo(1));
            Assert.That(source.Entries[0], Is.SameAs(horizontal));
            Assert.That(target.Entries.Count, Is.EqualTo(2));
            Assert.That(firstVertical.Position, Is.EqualTo(Vector2Int.zero));
            Assert.That(secondVertical.Position, Is.EqualTo(new Vector2Int(1, 0)));
        }
        finally
        {
            Object.DestroyImmediate(horizontalDefinition);
            Object.DestroyImmediate(verticalDefinition);
        }
    }

    // 5x2 격자 전체를 사용하는 소총은 다른 기준 좌표로 이동할 수 없으므로 빈 모양 셀에 총알이 있어도 교환할 수 없습니다.
    // 실제 여유가 없는 경우에는 두 엔트리의 위치를 전혀 바꾸지 않고 false를 반환해야 합니다.
    [Test]
    public void Swap_WhenNoAlternativeArrangementExists_LeavesPositionsUnchanged()
    {
        ItemDefinition rifleDefinition = CreateRifleDefinition();
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            var tightGrid = new PlayerInventory.GridData(5, 2);
            var rifle = new InventoryEntry(rifleDefinition, 1, Vector2Int.zero);
            var ammo = new InventoryEntry(definition, 1, new Vector2Int(1, 1));
            tightGrid.Entries.Add(rifle);
            tightGrid.Entries.Add(ammo);

            Assert.That(inventory.TryMove(tightGrid, tightGrid, ammo, Vector2Int.zero), Is.False);
            Assert.That(rifle.Position, Is.EqualTo(Vector2Int.zero));
            Assert.That(ammo.Position, Is.EqualTo(new Vector2Int(1, 1)));
        }
        finally
        {
            Object.DestroyImmediate(rifleDefinition);
        }
    }

    // 현재 격자 목록에 들어 있지 않은 엔트리는 분할하거나 회전해도 상태가 바뀌지 않아야 합니다.
    // UI가 이미 제거된 아이템을 계속 선택하고 있거나 외부 코드가 임의 엔트리를 넘기는 경우의 복제와 상태 변경을 막는 검사입니다.
    [Test]
    public void Commands_WithEntryNotOwnedByGrid_AreRejected()
    {
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        var externalEntry = new InventoryEntry(definition, 10, Vector2Int.zero);

        Assert.That(inventory.TrySplit(inventory.Root, externalEntry), Is.False);
        Assert.That(inventory.TryRotate(inventory.Root, externalEntry), Is.False);
        Assert.That(externalEntry.Quantity, Is.EqualTo(10));
        Assert.That(externalEntry.Rotated, Is.False);
        Assert.That(inventory.Entries, Is.Empty);
    }

    // 서로 떨어진 같은 탄약 묶음 두 개를 자동 정리하면 하나의 중첩으로 합쳐지고 왼쪽 위부터 다시 배치되는지 검사합니다.
    // 정리 전후 전체 수량 20이 유지되어야 하며 제거된 두 번째 엔트리 때문에 수량이 사라지면 안 됩니다.
    [Test]
    public void AutoSort_MergesStacksAndCompactsToTopLeft()
    {
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        inventory.Root.Entries.Add(new InventoryEntry(definition, 10, new Vector2Int(8, 5)));
        inventory.Root.Entries.Add(new InventoryEntry(definition, 10, new Vector2Int(9, 5)));

        Assert.That(inventory.TryAutoSort(inventory.Root), Is.True);
        Assert.That(inventory.Entries.Count, Is.EqualTo(1));
        Assert.That(inventory.Entries[0].Quantity, Is.EqualTo(20));
        Assert.That(inventory.Entries[0].Position, Is.EqualTo(Vector2Int.zero));
    }

    // 자동 정리가 불규칙한 소총과 1칸 총알을 다시 배치한 뒤 어느 실제 셀에서도 두 아이템이 겹치지 않는지 검사합니다.
    // 큰 소총을 먼저 배치하고 총알은 소총의 빈 모양 셀을 활용할 수 있어야 합니다.
    [Test]
    public void AutoSort_RifleAndAmmo_NeverOverlapOccupiedCells()
    {
        ItemDefinition rifleDefinition = CreateRifleDefinition();
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            var ammo = new InventoryEntry(definition, 1, new Vector2Int(9, 5));
            var rifle = new InventoryEntry(rifleDefinition, 1, new Vector2Int(3, 3));
            inventory.Root.Entries.Add(ammo);
            inventory.Root.Entries.Add(rifle);

            Assert.That(inventory.TryAutoSort(inventory.Root), Is.True);
            for (int y = 0; y < inventory.Root.Height; y++)
            for (int x = 0; x < inventory.Root.Width; x++)
            {
                int occupants = 0;
                foreach (InventoryEntry entry in inventory.Entries)
                    if (entry.OccupiesGridCell(x, y)) occupants++;
                Assert.That(occupants, Is.LessThanOrEqualTo(1), $"({x}, {y}) 칸에서 아이템이 겹쳤습니다.");
            }
        }
        finally
        {
            Object.DestroyImmediate(rifleDefinition);
        }
    }

    // 자동 정리를 두 번 실행했을 때 두 번째 실행이 수량, 좌표, 회전과 목록 순서를 다시 바꾸지 않는지 검사합니다.
    // 같은 입력의 결과가 매번 달라지면 저장 데이터와 UI가 흔들리므로 결정성은 자동 정리의 중요한 불변조건입니다.
    [Test]
    public void AutoSort_RepeatedCall_IsDeterministicAndPreservesQuantity()
    {
        ItemDefinition rifleDefinition = CreateRifleDefinition();
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            inventory.Root.Entries.Add(new InventoryEntry(rifleDefinition, 1, new Vector2Int(3, 3)));
            inventory.Root.Entries.Add(new InventoryEntry(definition, 10, new Vector2Int(8, 5)));
            inventory.Root.Entries.Add(new InventoryEntry(definition, 15, new Vector2Int(9, 5)));
            int quantityBefore = CountAllQuantities(inventory.Root);

            Assert.That(inventory.TryAutoSort(inventory.Root), Is.True);
            AssertGridIsValid(inventory.Root);
            List<string> firstResult = CaptureGridState(inventory.Root);

            Assert.That(inventory.TryAutoSort(inventory.Root), Is.True);
            AssertGridIsValid(inventory.Root);
            CollectionAssert.AreEqual(firstResult, CaptureGridState(inventory.Root));
            Assert.That(CountAllQuantities(inventory.Root), Is.EqualTo(quantityBefore));
        }
        finally
        {
            Object.DestroyImmediate(rifleDefinition);
        }
    }

    // 현재 격자보다 큰 아이템 때문에 자동 정리 계획을 완성할 수 없으면 기존 엔트리를 전혀 수정하지 않는지 검사합니다.
    // 가상 계획 전에 목록을 비우거나 회전시키면 실패 시 아이템이 사라질 수 있으므로 트랜잭션 처리를 검증합니다.
    [Test]
    public void AutoSort_WhenPlanningFails_DoesNotMutateGrid()
    {
        ItemDefinition rifleDefinition = CreateRifleDefinition();
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            var smallGrid = new PlayerInventory.GridData(4, 2);
            var rifle = new InventoryEntry(rifleDefinition, 1, new Vector2Int(2, 1), true);
            smallGrid.Entries.Add(rifle);

            Assert.That(inventory.TryAutoSort(smallGrid), Is.False);
            Assert.That(smallGrid.Entries.Count, Is.EqualTo(1));
            Assert.That(smallGrid.Entries[0], Is.SameAs(rifle));
            Assert.That(rifle.Position, Is.EqualTo(new Vector2Int(2, 1)));
            Assert.That(rifle.Rotated, Is.True);
            Assert.That(rifle.Quantity, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(rifleDefinition);
        }
    }

    // 드래그 미리보기는 실제 Resolver의 계획만 읽고 엔트리 좌표를 바꾸지 않아야 합니다.
    // 미리보기가 Move라고 표시된 위치에 실제 TryMove를 실행했을 때도 같은 결과가 성공하는지 함께 확인합니다.
    [Test]
    public void PlacementPreview_UsesSamePlanAsMoveWithoutMutatingData()
    {
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        var source = new PlayerInventory.GridData(2, 1);
        var target = new PlayerInventory.GridData(2, 1);
        var entry = new InventoryEntry(definition, 1, Vector2Int.zero);
        source.Entries.Add(entry);

        InventoryPlacementPreview preview = InventoryGridOperations.GetPlacementPreview(
            source,
            target,
            entry,
            new Vector2Int(1, 0));

        Assert.That(preview, Is.EqualTo(InventoryPlacementPreview.Move));
        Assert.That(source.Entries, Does.Contain(entry));
        Assert.That(target.Entries, Is.Empty);
        Assert.That(entry.Position, Is.EqualTo(Vector2Int.zero));
        Assert.That(inventory.TryMove(source, target, entry, new Vector2Int(1, 0)), Is.True);
        Assert.That(entry.Position, Is.EqualTo(new Vector2Int(1, 0)));
    }

    // 새 가방을 장착할 때 기존 가방 객체 자체를 출발 격자로 되돌려 내부 아이템 상태까지 그대로 보존하는지 검사합니다.
    // 장비를 복제해서 교체하면 Contents, 탄창 또는 내구도 같은 인스턴스 상태가 사라질 수 있으므로 같은 참조인지 확인합니다.
    [Test]
    public void EquipSwap_ReturnsOldEquipmentAtomicallyAndPreservesContents()
    {
        ItemDefinition oldBackpack = CreateEquipmentDefinition("old_backpack", EquipmentSlotType.Backpack, 1, 1, 2, 2);
        ItemDefinition newBackpack = CreateEquipmentDefinition("new_backpack", EquipmentSlotType.Backpack, 1, 1, 3, 2);
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            var oldSource = new PlayerInventory.GridData(1, 1);
            var oldEntry = new InventoryEntry(oldBackpack, 1, Vector2Int.zero);
            oldEntry.Contents.Entries.Add(new InventoryEntry(definition, 7, Vector2Int.zero));
            oldSource.Entries.Add(oldEntry);
            Assert.That(inventory.TryEquip(oldSource, oldEntry), Is.True);

            var source = new PlayerInventory.GridData(1, 1);
            var newEntry = new InventoryEntry(newBackpack, 1, Vector2Int.zero);
            source.Entries.Add(newEntry);

            Assert.That(
                inventory.TryEquipOrSwap(source, newEntry, EquipmentSlotType.Backpack, out string failure),
                Is.True,
                failure);
            Assert.That(inventory.Equipment[EquipmentSlotType.Backpack], Is.SameAs(newEntry));
            Assert.That(source.Entries, Has.Count.EqualTo(1));
            Assert.That(source.Entries[0], Is.SameAs(oldEntry));
            Assert.That(oldEntry.Contents.Entries, Has.Count.EqualTo(1));
            Assert.That(oldEntry.Contents.Entries[0].Quantity, Is.EqualTo(7));
        }
        finally
        {
            Object.DestroyImmediate(oldBackpack);
            Object.DestroyImmediate(newBackpack);
        }
    }

    // 기존 장비를 출발 격자와 플레이어 루트 어디에도 둘 수 없으면 교체 전체를 취소해야 합니다.
    // 실패 뒤 새 장비, 기존 장비와 모든 목록 참조가 시작 상태와 같은지 확인해 부분 적용 회귀를 막습니다.
    [Test]
    public void EquipSwap_WhenOldEquipmentHasNoReturnSpace_RollsBackEverything()
    {
        ItemDefinition wideBackpack = CreateEquipmentDefinition("wide_backpack", EquipmentSlotType.Backpack, 2, 1, 2, 2);
        ItemDefinition newBackpack = CreateEquipmentDefinition("new_small_backpack", EquipmentSlotType.Backpack, 1, 1, 2, 2);
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            var oldSource = new PlayerInventory.GridData(2, 1);
            var oldEntry = new InventoryEntry(wideBackpack, 1, Vector2Int.zero);
            oldSource.Entries.Add(oldEntry);
            Assert.That(inventory.TryEquip(oldSource, oldEntry), Is.True);

            // 루트의 모든 60칸을 채워 두 번째 반환 후보도 사용할 수 없게 합니다.
            for (int y = 0; y < inventory.Root.Height; y++)
            for (int x = 0; x < inventory.Root.Width; x++)
                inventory.Root.Entries.Add(new InventoryEntry(definition, 1, new Vector2Int(x, y)));

            var source = new PlayerInventory.GridData(1, 1);
            var newEntry = new InventoryEntry(newBackpack, 1, Vector2Int.zero);
            source.Entries.Add(newEntry);

            Assert.That(
                inventory.TryEquipOrSwap(source, newEntry, EquipmentSlotType.Backpack, out string failure),
                Is.False);
            Assert.That(failure, Does.Contain("공간"));
            Assert.That(inventory.Equipment[EquipmentSlotType.Backpack], Is.SameAs(oldEntry));
            Assert.That(source.Entries, Has.Count.EqualTo(1));
            Assert.That(source.Entries[0], Is.SameAs(newEntry));
        }
        finally
        {
            Object.DestroyImmediate(wideBackpack);
            Object.DestroyImmediate(newBackpack);
        }
    }

    // 컨테이너를 자기 Contents로 옮기면 저장 스냅샷이 순환하므로 이동과 미리보기가 모두 거부되어야 합니다.
    [Test]
    public void Container_CannotBeMovedIntoItsOwnContents()
    {
        ItemDefinition backpack = CreateEquipmentDefinition("cycle_backpack", EquipmentSlotType.Backpack, 1, 1, 2, 2);
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            var entry = new InventoryEntry(backpack, 1, Vector2Int.zero);
            inventory.Root.Entries.Add(entry);

            Assert.That(
                InventoryGridOperations.GetPlacementPreview(inventory.Root, entry.Contents, entry, Vector2Int.zero),
                Is.EqualTo(InventoryPlacementPreview.Invalid));
            Assert.That(inventory.TryMove(inventory.Root, entry.Contents, entry, Vector2Int.zero), Is.False);
            Assert.That(inventory.Root.Entries, Does.Contain(entry));
            Assert.That(entry.Contents.Entries, Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(backpack);
        }
    }

    // 버릴 수 없는 테스트 지급품과 내용물이 든 컨테이너는 false뿐 아니라 UI가 표시할 구체적인 이유를 제공해야 합니다.
    [Test]
    public void DropFailureReason_ExplainsProtectedItems()
    {
        ItemDefinition backpack = CreateEquipmentDefinition("drop_backpack", EquipmentSlotType.Backpack, 1, 1, 2, 2);
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            inventory.DropRequested += (_, __) => { };
            var provided = new InventoryEntry(definition, 1, Vector2Int.zero, false, true);
            inventory.Root.Entries.Add(provided);
            Assert.That(inventory.GetDropFailureReason(inventory.Root, provided), Does.Contain("테스트 지급품"));

            inventory.Root.Entries.Clear();
            var container = new InventoryEntry(backpack, 1, Vector2Int.zero);
            container.Contents.Entries.Add(new InventoryEntry(definition, 1, Vector2Int.zero));
            inventory.Root.Entries.Add(container);
            Assert.That(inventory.GetDropFailureReason(inventory.Root, container), Does.Contain("내용물"));
        }
        finally
        {
            Object.DestroyImmediate(backpack);
        }
    }

    // 격자의 모든 엔트리가 경계 안에 있고 어느 실제 셀에도 두 아이템이 동시에 존재하지 않는지 검사하는 공통 불변조건입니다.
    private static void AssertGridIsValid(PlayerInventory.GridData grid)
    {
        foreach (InventoryEntry entry in grid.Entries)
        {
            Assert.That(entry.Position.x, Is.GreaterThanOrEqualTo(0));
            Assert.That(entry.Position.y, Is.GreaterThanOrEqualTo(0));
            Assert.That(entry.Position.x + entry.Width, Is.LessThanOrEqualTo(grid.Width));
            Assert.That(entry.Position.y + entry.Height, Is.LessThanOrEqualTo(grid.Height));
        }

        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            int occupants = 0;
            foreach (InventoryEntry entry in grid.Entries)
                if (entry.OccupiesGridCell(x, y)) occupants++;
            Assert.That(occupants, Is.LessThanOrEqualTo(1), $"({x}, {y}) 칸에서 실제 점유 셀이 겹쳤습니다.");
        }
    }

    // 자동 정리 전후 총수량 보존을 확인하기 위해 격자에 직접 들어 있는 모든 엔트리 수량을 합산합니다.
    private static int CountAllQuantities(PlayerInventory.GridData grid)
    {
        int total = 0;
        foreach (InventoryEntry entry in grid.Entries) total += entry.Quantity;
        return total;
    }

    // 반복 정리 결과를 비교할 수 있도록 아이템 ID, 수량, 좌표와 회전을 일정한 문자열 목록으로 기록합니다.
    private static List<string> CaptureGridState(PlayerInventory.GridData grid)
    {
        var result = new List<string>();
        foreach (InventoryEntry entry in grid.Entries)
        {
            result.Add(
                entry.Item.ItemId + "|" + entry.Quantity + "|" +
                entry.Position.x + "," + entry.Position.y + "|" + entry.Rotated);
        }
        return result;
    }

    // 실제 weapon_rifle 에셋과 같은 5x2 모양을 메모리에 만들어 소총·총알 교환 테스트에서 사용합니다.
    // 저장 순서는 왼쪽 위부터 행 단위이며 위쪽 다섯 칸과 아래쪽 첫째·셋째 칸만 true입니다.
    private static ItemDefinition CreateRifleDefinition()
    {
        ItemDefinition rifleDefinition = ScriptableObject.CreateInstance<ItemDefinition>();
        var rifleData = new SerializedObject(rifleDefinition);
        rifleData.FindProperty("itemId").stringValue = "rifle_test";
        rifleData.FindProperty("displayName").stringValue = "테스트 소총";
        rifleData.FindProperty("maxStack").intValue = 1;
        rifleData.FindProperty("gridWidth").intValue = 5;
        rifleData.FindProperty("gridHeight").intValue = 2;

        SerializedProperty shape = rifleData.FindProperty("gridShape");
        shape.arraySize = 10;
        bool[] cells = { true, true, true, true, true, true, false, true, false, false };
        for (int i = 0; i < cells.Length; i++)
            shape.GetArrayElementAtIndex(i).boolValue = cells[i];

        rifleData.ApplyModifiedPropertiesWithoutUndo();
        return rifleDefinition;
    }

    // 다중 겹침 실패 테스트가 원하는 가로·세로 모양을 명확하게 만들 수 있도록 전체 셀이 채워진 사각형 정의를 생성합니다.
    // 생성된 ScriptableObject는 호출한 테스트의 finally에서 제거하여 메모리 객체가 다른 테스트에 남지 않게 합니다.
    private static ItemDefinition CreateRectangularDefinition(string itemId, int width, int height)
    {
        ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
        var data = new SerializedObject(item);
        data.FindProperty("itemId").stringValue = itemId;
        data.FindProperty("displayName").stringValue = itemId;
        data.FindProperty("maxStack").intValue = 1;
        data.FindProperty("gridWidth").intValue = width;
        data.FindProperty("gridHeight").intValue = height;
        data.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    // 장비 교체 테스트가 슬롯 종류, 외부 점유 크기와 내부 컨테이너 크기를 독립적으로 지정할 수 있게 정의를 만듭니다.
    private static ItemDefinition CreateEquipmentDefinition(
        string itemId,
        EquipmentSlotType slot,
        int gridWidth,
        int gridHeight,
        int containerWidth,
        int containerHeight)
    {
        ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
        var data = new SerializedObject(item);
        data.FindProperty("itemId").stringValue = itemId;
        data.FindProperty("displayName").stringValue = itemId;
        data.FindProperty("itemType").enumValueIndex = (int)ItemType.Container;
        data.FindProperty("equipmentSlot").enumValueIndex = (int)slot;
        data.FindProperty("maxStack").intValue = 1;
        data.FindProperty("gridWidth").intValue = gridWidth;
        data.FindProperty("gridHeight").intValue = gridHeight;
        data.FindProperty("containerWidth").intValue = containerWidth;
        data.FindProperty("containerHeight").intValue = containerHeight;
        data.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }
}
