using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZombieSurvival.AI;
using ZombieSurvival.Combat;
using ZombieSurvival.Core;
using ZombieSurvival.Hideout;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;
using ZombieSurvival.Player;
using ZombieSurvival.UI;

public sealed class RoadmapPlayModeTests
{
    // 실제 MonoBehaviour 생명주기 안에서 Health가 방탄복 계산을 거쳐 체력과 내구도를 함께 줄이는지 검사합니다.
    [UnityTest]
    public IEnumerator EquippedArmor_ReducesHealthDamageAndConsumesDurability()
    {
        ItemDefinition armorItem = CreateItem("playmode_armor", ItemType.Armor);
        SetField(armorItem, "equipmentSlot", EquipmentSlotType.Armor);
        SetField(armorItem, "armorDamageReduction", 0.4f);
        SetField(armorItem, "armorMaxDurability", 100f);

        var player = new GameObject("ArmorPlayModePlayer");
        player.SetActive(false);
        PlayerInventory inventory = player.AddComponent<PlayerInventory>();
        Health health = player.AddComponent<Health>();
        ArmorProtection protection = player.AddComponent<ArmorProtection>();

        var armorEntry = new InventoryEntry(armorItem, 1, Vector2Int.zero);
        inventory.Root.Entries.Add(armorEntry);
        Assert.That(inventory.TryEquip(inventory.Root, armorEntry), Is.True);

        player.SetActive(true);
        yield return null;
        health.TakeDamage(20f, player.transform.position);

        Assert.That(health.CurrentHealth, Is.EqualTo(88f).Within(0.001f));
        Assert.That(protection.CurrentDurability, Is.EqualTo(92f).Within(0.001f));

        Object.Destroy(player);
        Object.Destroy(armorItem);
        yield return null;
    }

    // 좀비의 실제 Died 이벤트가 드롭 컴포넌트까지 전달되어 아이템을 바닥에 뿌리지 않고 시체 격자에 넣는지 검사합니다.
    [UnityTest]
    public IEnumerator ZombieDeath_SpawnsConfiguredWorldLootOnce()
    {
        ItemDefinition ammoItem = CreateItem("playmode_ammo", ItemType.Ammo);
        var zombie = new GameObject("LootPlayModeZombie");
        zombie.SetActive(false);
        Health health = zombie.AddComponent<Health>();
        ZombieLootDrop lootDrop = zombie.AddComponent<ZombieLootDrop>();
        lootDrop.Configure(new ZombieLootEntry
        {
            item = ammoItem,
            dropChance = 1f,
            minimumQuantity = 4,
            maximumQuantity = 4
        });

        zombie.SetActive(true);
        yield return null;
        health.TakeDamage(999f, zombie.transform.position);
        yield return null;

        WorldLootContainer corpse = Object.FindObjectOfType<WorldLootContainer>();
        Assert.That(corpse, Is.Not.Null);
        Assert.That(corpse.LootGrid.Entries.Count, Is.EqualTo(1));
        Assert.That(corpse.LootGrid.Entries[0].Item, Is.SameAs(ammoItem));
        Assert.That(corpse.LootGrid.Entries[0].Quantity, Is.EqualTo(4));
        foreach (Collider corpseCollider in corpse.GetComponentsInChildren<Collider>())
            Assert.That(corpseCollider.isTrigger, Is.True);

        Object.Destroy(corpse.gameObject);
        Object.Destroy(ammoItem);
        yield return null;
    }

    // 실제 Build Settings의 세 씬을 차례로 열어 시작 화면→거점→레이드의 핵심 구성 요소가 모두 연결되어 있는지 확인합니다.
    // 마지막에는 Bootstrap으로 돌아가 다음 테스트가 레이드 씬 상태를 이어받지 않게 합니다.
    [UnityTest]
    public IEnumerator GameLoopScenes_LoadWithRoadmapSystemsConnected()
    {
        yield return SceneManager.LoadSceneAsync(GameSceneNavigator.BootstrapSceneName);
        Assert.That(Object.FindObjectOfType<BootstrapController>(), Is.Not.Null);

        yield return SceneManager.LoadSceneAsync(GameSceneNavigator.HideoutSceneName);
        Assert.That(Object.FindObjectOfType<HideoutController>(), Is.Not.Null);
        // 거점 창고 Canvas는 플레이어가 창고 시설을 열기 전까지 비활성 상태가 정상입니다.
        // includeInactive를 사용해야 화면을 억지로 켜지 않고도 창고·출격 격자 프리팹의 실제 연결 여부를 검사할 수 있습니다.
        Assert.That(Object.FindObjectsOfType<InventoryGridView>(true).Length, Is.GreaterThanOrEqualTo(2));

        yield return SceneManager.LoadSceneAsync(GameSceneNavigator.RaidSceneName);
        Assert.That(Object.FindObjectOfType<RaidSession>(), Is.Not.Null);
        Assert.That(Object.FindObjectOfType<ArmorProtection>(), Is.Not.Null);
        // RaidMap이 실행 시 Collider를 읽어 실제 NavMesh 데이터를 만들었는지 확인합니다.
        // 다음 프레임까지 기다리면 배치된 좀비의 Start도 실행되어 각 NavMeshAgent가 이동 가능한 면 위에 올라갑니다.
        RaidNavigationSurface navigationSurface = Object.FindObjectOfType<RaidNavigationSurface>();
        Assert.That(navigationSurface, Is.Not.Null);
        Assert.That(navigationSurface.IsNavigationReady, Is.True);
        yield return null;

        bool anyZombieUsesNavigation = false;
        foreach (ZombieMovement movement in Object.FindObjectsOfType<ZombieMovement>())
            anyZombieUsesNavigation |= movement.IsUsingNavigation;
        Assert.That(anyZombieUsesNavigation, Is.True);
        // 실제 Raid 씬의 플레이어 프리팹에도 포만감 시스템이 연결되어 있어야
        // 음식 사용, HUD 표시, 낮은 포만감의 이동 속도 감소가 한 흐름으로 동작합니다.
        Assert.That(Object.FindObjectOfType<PlayerSurvival>(), Is.Not.Null);
        Assert.That(Object.FindObjectsOfType<ZombieLootDrop>().Length, Is.GreaterThanOrEqualTo(3));
        Assert.That(Object.FindObjectsOfType<FieldLootSource>().Length, Is.GreaterThanOrEqualTo(3));
        // 새 레이드 맵의 다섯 구역이 실제 Scene에도 생성되어 있어야 시작→파밍→탈출 동선을 직접 테스트할 수 있습니다.
        Assert.That(GameObject.Find("RaidMap/Zone_SouthCamp"), Is.Not.Null);
        Assert.That(GameObject.Find("RaidMap/Zone_WestResidential"), Is.Not.Null);
        Assert.That(GameObject.Find("RaidMap/Zone_CentralCheckpoint"), Is.Not.Null);
        Assert.That(GameObject.Find("RaidMap/Zone_EastWarehouse"), Is.Not.Null);
        Assert.That(GameObject.Find("RaidMap/Zone_NorthExtraction"), Is.Not.Null);

        yield return SceneManager.LoadSceneAsync(GameSceneNavigator.BootstrapSceneName);
    }

    private static ItemDefinition CreateItem(string itemId, ItemType type)
    {
        ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
        SetField(item, "itemId", itemId);
        SetField(item, "displayName", itemId);
        SetField(item, "itemType", type);
        SetField(item, "maxStack", 60);
        SetField(item, "gridWidth", 1);
        SetField(item, "gridHeight", 1);
        return item;
    }

    // PlayMode 어셈블리는 UnityEditor를 참조하지 않으므로 테스트용 ScriptableObject의 비공개 직렬화 필드를 Reflection으로 설정합니다.
    private static void SetField<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"테스트 설정 필드를 찾지 못했습니다: {fieldName}");
        field.SetValue(target, value);
    }
}
