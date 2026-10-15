using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
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
    // 실제 CharacterController를 바닥에 접지시켜 WASD 이동 방향, 최대 기력의 25% 지불과 구르기 중에만 적용되는 무적을 검사합니다.
    [UnityTest]
    public IEnumerator Roll_MovesTowardMovementInputAndSpendsOneQuarterOfMaximumStamina()
    {
        GameObject floor = CreateRollTestFloor();
        GameObject player = CreateRollTestPlayer();
        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        PlayerSurvival survival = player.GetComponent<PlayerSurvival>();
        Health health = player.GetComponent<Health>();
        PlayerAim aim = player.AddComponent<PlayerAim>();
        aim.enabled = false;
        float previousTimeScale = Time.timeScale;
        float previousCaptureDeltaTime = Time.captureDeltaTime;
        try
        {
            Time.timeScale = 1f;
            // 그래픽 없는 배치 실행은 초당 수만 프레임이 나와 CharacterController의 최소 이동량보다 작은 명령이 무시될 수 있습니다.
            // 0.24초와 나누어떨어지지 않는 0.07초 프레임을 사용해 실제 저프레임 환경의 마지막 이동량 제한까지 검사합니다.
            Time.captureDeltaTime = 0.07f;
            player.SetActive(true);
            yield return WaitForRollTestGrounding(player);

            // 일시정지와 조작 잠금은 기력 차감 전에 거부되어야 합니다.
            Time.timeScale = 0f;
            Assert.That(movement.TryBeginRoll(), Is.False);
            Time.timeScale = 1f;
            movement.enabled = false;
            Assert.That(movement.TryBeginRoll(), Is.False);
            movement.enabled = true;
            Assert.That(survival.CurrentStamina, Is.EqualTo(200f));

            // 마우스 조준은 +Z로 둔 상태에서 이동 입력만 +X로 전달해 구르기가 조준이 아니라 WASD 방향을 선택하는지 확인합니다.
            aim.enabled = true;
            typeof(PlayerAim).GetProperty(nameof(PlayerAim.AimDirection)).SetValue(aim, Vector3.forward);
            Vector3 start = player.transform.position;
            Assert.That(movement.TryBeginRoll(Vector2.right), Is.True);
            Assert.That(survival.CurrentStamina, Is.EqualTo(150f));
            Assert.That(movement.TryBeginRoll(), Is.False);
            Assert.That(survival.CurrentStamina, Is.EqualTo(150f));
            health.TakeDamage(30f, player.transform.position);
            Assert.That(health.CurrentHealth, Is.EqualTo(100f));
            Assert.That(health.IsInvulnerable, Is.True);

            float deadline = Time.realtimeSinceStartup + 3f;
            while (movement.IsRolling && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(movement.IsRolling, Is.False);
            Assert.That(player.transform.position.x - start.x, Is.EqualTo(3f).Within(0.05f));
            Assert.That(player.transform.position.z - start.z, Is.EqualTo(0f).Within(0.05f));
            Assert.That(health.IsInvulnerable, Is.False);
            health.TakeDamage(30f, player.transform.position);
            Assert.That(health.CurrentHealth, Is.EqualTo(70f));
            Assert.That(movement.TryBeginRoll(), Is.False, "완료 직후에는 재사용 대기 시간이 적용되어야 합니다.");
            Assert.That(survival.CurrentStamina, Is.EqualTo(150f));
        }
        finally
        {
            Time.timeScale = previousTimeScale;
            Time.captureDeltaTime = previousCaptureDeltaTime;
            Object.Destroy(player);
            Object.Destroy(floor);
        }
        yield return null;
    }

    // 구르기 이동도 벽과 충돌해야 하며, UI가 이동을 잠그면 진행 중 무적이 즉시 사라져야 합니다.
    [UnityTest]
    public IEnumerator Roll_CollidesWithWallsAndReleasesInvulnerabilityWhenControlsLock()
    {
        GameObject floor = CreateRollTestFloor();
        GameObject player = CreateRollTestPlayer();
        var wall = new GameObject("RollTestWall", typeof(BoxCollider));
        wall.transform.position = new Vector3(200f, 1f, 201.5f);
        wall.GetComponent<BoxCollider>().size = new Vector3(4f, 3f, 0.2f);
        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        Health health = player.GetComponent<Health>();
        PlayerControlGate gate = player.AddComponent<PlayerControlGate>();
        SetField(movement, "rollDuration", 0.6f);
        float previousTimeScale = Time.timeScale;
        float previousCaptureDeltaTime = Time.captureDeltaTime;
        try
        {
            Time.timeScale = 1f;
            // 테스트의 벽 충돌도 일정한 물리 이동량으로 재현하고 종료 시 원래 시간 설정을 복구합니다.
            Time.captureDeltaTime = 0.05f;
            player.SetActive(true);
            yield return WaitForRollTestGrounding(player);
            Assert.That(movement.TryBeginRoll(), Is.True);
            yield return new WaitForSeconds(0.35f);
            Assert.That(movement.IsRolling, Is.True);
            Assert.That(player.transform.position.z, Is.InRange(200.8f, 201.15f));

            Assert.That(gate.AcquireLock(wall), Is.True);
            Assert.That(movement.IsRolling, Is.False);
            Assert.That(health.IsInvulnerable, Is.False);
            health.TakeDamage(10f, player.transform.position);
            Assert.That(health.CurrentHealth, Is.EqualTo(90f));
            Assert.That(movement.TryBeginRoll(), Is.False);
            Assert.That(player.GetComponent<PlayerSurvival>().CurrentStamina, Is.EqualTo(150f));

            // 별도 기능이 준 시간제 무적은 구르기 종료가 지우면 안 됩니다.
            health.BeginInvulnerability(1f);
            gate.ReleaseLock(wall);
            gate.AcquireLock(wall);
            Assert.That(health.IsInvulnerable, Is.True);
        }
        finally
        {
            Time.timeScale = previousTimeScale;
            Time.captureDeltaTime = previousCaptureDeltaTime;
            Object.Destroy(player);
            Object.Destroy(floor);
            Object.Destroy(wall);
        }
        yield return null;
    }

    // 다른 테스트가 남긴 씬의 배치와 겹치지 않는 외곽 좌표에서 실제 물리 충돌을 검사합니다.
    private static GameObject CreateRollTestFloor()
    {
        var floor = new GameObject("RollTestFloor", typeof(BoxCollider));
        floor.transform.position = new Vector3(200f, -0.5f, 200f);
        floor.GetComponent<BoxCollider>().size = new Vector3(30f, 1f, 30f);
        return floor;
    }

    private static GameObject CreateRollTestPlayer()
    {
        var player = new GameObject("RollTestPlayer");
        player.SetActive(false);
        player.transform.position = new Vector3(200f, 0.05f, 200f);
        CharacterController controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.3f;
        controller.center = Vector3.up * 0.9f;
        player.AddComponent<Health>();
        PlayerSurvival survival = player.AddComponent<PlayerSurvival>();
        SetField(survival, "maximumStamina", 200f);
        SetField(survival, "startingStamina", 200f);
        // 검사 중 자연 회복이 차감량을 가리지 않게 끄고 실제 비용만 확인합니다.
        SetField(survival, "staminaRecoveryPerSecond", 0f);
        player.AddComponent<PlayerMovement>();
        return player;
    }

    private static IEnumerator WaitForRollTestGrounding(GameObject player)
    {
        Physics.SyncTransforms();
        CharacterController controller = player.GetComponent<CharacterController>();
        float deadline = Time.realtimeSinceStartup + 3f;
        while (!controller.isGrounded && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(controller.isGrounded, Is.True);
    }

    // 재장전은 일반 UI 잠금·조준·인벤토리 정리에는 유지되고, 전투 흐름을 실제로 바꾸는 행동에만 취소되어야 합니다.
    // 발사, 마우스 휠 무기 전환, 장비 변경, 아이템 사용과 구르기를 한 테스트에서 차례로 실행해 취소 목록이 넓어지는 회귀를 막습니다.
    [UnityTest]
    public IEnumerator Reload_CancelsOnlyForExplicitInterruptActions()
    {
        GameObject floor = CreateRollTestFloor();
        GameObject player = CreateRollTestPlayer();
        PlayerInventory inventory = player.AddComponent<PlayerInventory>();
        PlayerAim aim = player.AddComponent<PlayerAim>();
        PlayerCombat combat = player.AddComponent<PlayerCombat>();
        Health health = player.GetComponent<Health>();
        PlayerMovement movement = player.GetComponent<PlayerMovement>();

        WeaponDefinition pistolDefinition = ScriptableObject.CreateInstance<WeaponDefinition>();
        SetField(pistolDefinition, "reloadDuration", 5f);
        SetField(pistolDefinition, "magazineSize", 8);
        SetField(pistolDefinition, "ammoType", AmmoType.NineMillimeter);
        ItemDefinition primaryPistol = CreateItem("reload_primary", ItemType.Weapon);
        SetField(primaryPistol, "equipmentSlot", EquipmentSlotType.PrimaryWeapon);
        SetField(primaryPistol, "weaponDefinition", pistolDefinition);
        ItemDefinition secondaryPistol = CreateItem("reload_secondary", ItemType.Weapon);
        SetField(secondaryPistol, "equipmentSlot", EquipmentSlotType.SecondaryWeapon);
        SetField(secondaryPistol, "weaponDefinition", pistolDefinition);
        ItemDefinition ammo = CreateItem("reload_ammo", ItemType.Ammo);
        SetField(ammo, "ammoType", AmmoType.NineMillimeter);
        ItemDefinition armor = CreateItem("reload_armor", ItemType.Armor);
        SetField(armor, "equipmentSlot", EquipmentSlotType.Armor);
        ItemDefinition medkit = CreateItem("reload_medkit", ItemType.Healing);
        SetField(medkit, "usable", true);
        SetField(medkit, "healthRestoreAmount", 25f);

        var primaryEntry = new InventoryEntry(primaryPistol, 1, Vector2Int.zero);
        var secondaryEntry = new InventoryEntry(secondaryPistol, 1, Vector2Int.right);
        var armorEntry = new InventoryEntry(armor, 1, Vector2Int.right * 2);
        var medkitEntry = new InventoryEntry(medkit, 1, Vector2Int.right * 3);
        inventory.Root.Entries.AddRange(new[] { primaryEntry, secondaryEntry, armorEntry, medkitEntry });
        Assert.That(inventory.TryEquip(inventory.Root, primaryEntry), Is.True);
        Assert.That(inventory.TryEquip(inventory.Root, secondaryEntry), Is.True);
        Assert.That(inventory.TryAdd(ammo, 30), Is.True);
        for (int index = 0; index < 4; index++) Assert.That(primaryEntry.TryConsumeMagazineRound(), Is.True);
        for (int index = 0; index < 4; index++) Assert.That(secondaryEntry.TryConsumeMagazineRound(), Is.True);

        float previousTimeScale = Time.timeScale;
        try
        {
            Time.timeScale = 1f;
            player.SetActive(true);
            yield return WaitForRollTestGrounding(player);
            Assert.That(combat.ActiveWeaponSlot, Is.EqualTo(EquipmentSlotType.PrimaryWeapon));

            Assert.That(combat.TryStartReload(), Is.True);
            Assert.That(combat.IsReloading, Is.True);

            // 인벤토리 열기처럼 전투 컴포넌트가 잠시 꺼지거나, 탄약 묶음을 옮기거나, 조준해도 재장전은 유지되어야 합니다.
            combat.enabled = false;
            yield return null;
            Assert.That(combat.IsReloading, Is.True);
            combat.enabled = true;
            InventoryEntry ammoEntry = inventory.Root.Entries.Find(entry => entry.Item == ammo);
            Assert.That(inventory.TryMove(inventory.Root, inventory.Root, ammoEntry, new Vector2Int(5, 0)), Is.True);
            combat.SetAiming(true);
            Assert.That(combat.IsReloading, Is.True);

            // 발사 시도는 장전만 취소하며 같은 입력으로 탄창의 총알이 빠지면 안 됩니다.
            int roundsBeforeFireCancel = primaryEntry.MagazineRounds;
            Assert.That(combat.TryFire(), Is.False);
            Assert.That(combat.IsReloading, Is.False);
            Assert.That(primaryEntry.MagazineRounds, Is.EqualTo(roundsBeforeFireCancel));

            Assert.That(combat.TryStartReload(), Is.True);
            Assert.That(combat.TryCycleEquippedWeapon(1f), Is.True);
            Assert.That(combat.IsReloading, Is.False);
            Assert.That(combat.ActiveWeaponSlot, Is.EqualTo(EquipmentSlotType.SecondaryWeapon));

            Assert.That(combat.TryStartReload(), Is.True);
            Assert.That(inventory.TryEquip(inventory.Root, armorEntry), Is.True);
            Assert.That(combat.IsReloading, Is.False);

            Assert.That(combat.TryStartReload(), Is.True);
            // 체력이 가득 차 실패한 사용 시도는 실제 아이템 사용이 아니므로 재장전을 취소하면 안 됩니다.
            Assert.That(inventory.TryStartUse(inventory.Root, medkitEntry), Is.False);
            Assert.That(combat.IsReloading, Is.True);
            health.TakeDamage(20f, player.transform.position);
            Assert.That(inventory.TryStartUse(inventory.Root, medkitEntry), Is.True);
            Assert.That(combat.IsReloading, Is.False);
            inventory.CancelItemUse();

            Assert.That(combat.TryStartReload(), Is.True);
            Assert.That(movement.TryBeginRoll(Vector2.up), Is.True);
            Assert.That(combat.IsReloading, Is.False);
        }
        finally
        {
            Time.timeScale = previousTimeScale;
            Object.Destroy(player);
            Object.Destroy(floor);
            Object.Destroy(primaryPistol);
            Object.Destroy(secondaryPistol);
            Object.Destroy(ammo);
            Object.Destroy(armor);
            Object.Destroy(medkit);
            Object.Destroy(pistolDefinition);
        }
        yield return null;
    }

    // 출혈은 체력을 줄이되 일반 피격 연출과 방탄 내구도에 영향을 주지 않고, 붕대 사용을 방해하지 않아야 합니다.
    // 같은 실행에서 부위와 무관한 골절 디버프와 붕대·부목·구급상자의 서로 다른 치료 역할도 확인합니다.
    [UnityTest]
    public IEnumerator PlayerCondition_BleedingUsesBorderAndMedicalItemsTreatOnlyTheirConditions()
    {
        var player = new GameObject("ConditionPlayModePlayer");
        player.SetActive(false);
        Health health = player.AddComponent<Health>();
        PlayerCondition condition = player.AddComponent<PlayerCondition>();
        PlayerInventory inventory = player.AddComponent<PlayerInventory>();
        ArmorProtection armor = player.AddComponent<ArmorProtection>();
        var hudObject = new GameObject("ConditionTestHud");
        PlayerConditionHud conditionHud = hudObject.AddComponent<PlayerConditionHud>();
        PlayerDamageHud damageHud = hudObject.AddComponent<PlayerDamageHud>();
        var borderObject = new GameObject("BleedingScreenBorder", typeof(RectTransform), typeof(CanvasGroup));
        borderObject.transform.SetParent(hudObject.transform, false);
        CanvasGroup border = borderObject.GetComponent<CanvasGroup>();

        ItemDefinition armorItem = CreateItem("condition_armor", ItemType.Armor);
        SetField(armorItem, "equipmentSlot", EquipmentSlotType.Armor);
        SetField(armorItem, "armorDamageReduction", 0.4f);
        SetField(armorItem, "armorMaxDurability", 100f);
        ItemDefinition bandage = CreateItem("condition_bandage", ItemType.Healing);
        SetField(bandage, "usable", true);
        SetField(bandage, "treatsBleeding", true);
        ItemDefinition splint = CreateItem("condition_splint", ItemType.Healing);
        SetField(splint, "usable", true);
        SetField(splint, "treatsFracture", true);
        ItemDefinition medkit = CreateItem("condition_medkit", ItemType.Healing);
        SetField(medkit, "usable", true);
        SetField(medkit, "healthRestoreAmount", 100f);

        float previousTimeScale = Time.timeScale;
        try
        {
            Time.timeScale = 1f;
            SetField(inventory, "bandageUseDuration", 0.2f);
            var armorEntry = new InventoryEntry(armorItem, 1, Vector2Int.zero);
            inventory.Root.Entries.Add(armorEntry);
            Assert.That(inventory.TryEquip(inventory.Root, armorEntry), Is.True);
            var bandageEntry = new InventoryEntry(bandage, 2, Vector2Int.zero);
            var splintEntry = new InventoryEntry(splint, 2, Vector2Int.right);
            var medkitEntry = new InventoryEntry(medkit, 2, Vector2Int.right * 2);
            inventory.Root.Entries.AddRange(new[] { bandageEntry, splintEntry, medkitEntry });

            player.SetActive(true);
            conditionHud.ConfigureViews(null, null, null, null, border);
            conditionHud.SetPlayerCondition(condition);
            damageHud.SetPlayerHealth(health);
            int directHitEvents = 0;
            float resolvedBleedingDamage = 0f;
            health.Damaged += (_, __) => directHitEvents++;
            health.DamageResolved += damage =>
            {
                if (damage.Source == DamageSource.Bleeding) resolvedBleedingDamage += damage.Amount;
            };
            yield return null;
            Assert.That(borderObject.activeSelf, Is.False);

            health.TakeDamage(new DamageInfo(20f, player.transform.position,
                BodyPart.Torso, 0, 1f, 1f, false, DamageSource.ZombieAttack));
            float healthAfterDirectHit = health.CurrentHealth;
            float armorAfterDirectHit = armor.CurrentDurability;
            Assert.That(condition.IsBleeding, Is.True);
            Assert.That(condition.HasFracture, Is.True);
            Assert.That(condition.MovementMultiplier, Is.LessThan(1f));
            Assert.That(condition.AimSpreadMultiplier, Is.GreaterThan(1f));
            Assert.That(condition.ReloadDurationMultiplier, Is.GreaterThan(1f));

            // 원래 피격 오버레이가 사라질 만큼 기다려도 출혈만으로 다시 화면이 번쩍이거나 피격 이벤트가 발생하면 안 됩니다.
            yield return new WaitForSeconds(1.1f);
            Assert.That(health.CurrentHealth, Is.LessThan(healthAfterDirectHit));
            Assert.That(resolvedBleedingDamage, Is.GreaterThan(0f));
            Assert.That(directHitEvents, Is.EqualTo(1));
            Assert.That(damageHud.CurrentAlpha, Is.Zero);
            Assert.That(armor.CurrentDurability, Is.EqualTo(armorAfterDirectHit));
            Assert.That(borderObject.activeSelf, Is.True);
            Assert.That(border.alpha, Is.GreaterThan(0f));

            Assert.That(inventory.TryStartUse(inventory.Root, bandageEntry), Is.True);
            Assert.That(bandageEntry.Quantity, Is.EqualTo(2));
            yield return new WaitForSeconds(0.4f);
            Assert.That(inventory.IsUsingItem, Is.False);
            Assert.That(condition.IsBleeding, Is.False);
            Assert.That(condition.HasFracture, Is.True);
            Assert.That(bandageEntry.Quantity, Is.EqualTo(1));
            Assert.That(borderObject.activeSelf, Is.False);
            Assert.That(inventory.TryUse(inventory.Root, bandageEntry), Is.False);
            Assert.That(bandageEntry.Quantity, Is.EqualTo(1));

            // 체력 회복이 골절까지 없애지 않고, 부목을 사용했을 때에만 모든 골절 배율이 1로 복귀해야 합니다.
            Assert.That(inventory.TryUse(inventory.Root, medkitEntry), Is.True);
            Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
            Assert.That(condition.HasFracture, Is.True);
            Assert.That(inventory.TryUse(inventory.Root, medkitEntry), Is.False);
            Assert.That(medkitEntry.Quantity, Is.EqualTo(1));
            Assert.That(inventory.TryUse(inventory.Root, splintEntry), Is.True);
            Assert.That(condition.MovementMultiplier, Is.EqualTo(1f));
            Assert.That(condition.AimSpreadMultiplier, Is.EqualTo(1f));
            Assert.That(condition.ReloadDurationMultiplier, Is.EqualTo(1f));
            Assert.That(inventory.TryUse(inventory.Root, splintEntry), Is.False);
            Assert.That(splintEntry.Quantity, Is.EqualTo(1));
            Assert.That(condition.BuildStatusSummary(), Is.EqualTo("상태 정상"));
        }
        finally
        {
            Time.timeScale = previousTimeScale;
            Object.Destroy(hudObject);
            Object.Destroy(player);
            Object.Destroy(armorItem);
            Object.Destroy(bandage);
            Object.Destroy(splint);
            Object.Destroy(medkit);
        }
        yield return null;
    }

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

    // 실제 Build Settings의 네 씬을 차례로 열어 타이틀→로딩→거점→학교 레이드의 핵심 구성 요소가 모두 연결되어 있는지 확인합니다.
    // 마지막에는 Title로 돌아가 다음 테스트가 레이드 씬 상태를 이어받지 않게 합니다.
    [UnityTest]
    public IEnumerator GameLoopScenes_LoadWithRoadmapSystemsConnected()
    {
        yield return SceneManager.LoadSceneAsync(GameSceneNavigator.TitleSceneName);
        TitleMenuHud titleHud = Object.FindObjectOfType<TitleMenuHud>();
        Assert.That(titleHud, Is.Not.Null);
        Assert.That(titleHud.transform.Find("MainMenuPanel/StartGameButton")?.GetComponent<Button>(), Is.Not.Null);
        Assert.That(titleHud.transform.Find("MainMenuPanel/LoadGameButton")?.GetComponent<Button>(), Is.Not.Null);
        Assert.That(titleHud.transform.Find("MainMenuPanel/SettingsButton")?.GetComponent<Button>(), Is.Not.Null);
        Assert.That(titleHud.transform.Find("MainMenuPanel/QuitButton")?.GetComponent<Button>(), Is.Not.Null);

        Assert.That(GameSceneNavigator.TryLoadSceneThroughLoading(GameSceneNavigator.HideoutSceneName), Is.True);
        // LoadScene 호출은 코루틴을 실행 중인 프레임이 끝난 뒤 활성 씬에 반영될 수 있으므로 Loading 확인 전에 한 프레임 기다립니다.
        yield return null;
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameSceneNavigator.LoadingSceneName));
        LoadingSceneController loadingController = Object.FindObjectOfType<LoadingSceneController>();
        LoadingScreenHud loadingHud = Object.FindObjectOfType<LoadingScreenHud>();
        Assert.That(loadingController, Is.Not.Null);
        Assert.That(loadingHud, Is.Not.Null);
        // 실제 Loading 인스턴스에는 전체 화면 원화와 화면 아래에서 띄운 진행 막대가 연결되어 있어야 합니다.
        Assert.That(loadingHud.transform.Find("LoadingArtwork")?.GetComponent<Image>(), Is.Not.Null);
        Assert.That(loadingHud.transform.Find("LoadingPanel/ProgressFrame/ProgressFill")?.GetComponent<Image>(), Is.Not.Null);

        yield return WaitForActiveScene(GameSceneNavigator.HideoutSceneName, 10f);
        Assert.That(Object.FindObjectOfType<HideoutController>(), Is.Not.Null);
        // 거점 창고 Canvas는 플레이어가 창고 시설을 열기 전까지 비활성 상태가 정상입니다.
        // includeInactive를 사용해야 화면을 억지로 켜지 않고도 창고·출격 격자 프리팹의 실제 연결 여부를 검사할 수 있습니다.
        Assert.That(Object.FindObjectsOfType<InventoryGridView>(true).Length, Is.GreaterThanOrEqualTo(2));
        // 창고 출격 버튼을 대신하는 별도 출격 Canvas와 월드 시설이 실제 거점 씬에 함께 있어야 합니다.
        Assert.That(Object.FindObjectOfType<HideoutDeploymentHud>(true), Is.Not.Null);
        Assert.That(GameObject.Find("DeploymentFacility"), Is.Not.Null);

        Assert.That(GameSceneNavigator.TryLoadSceneThroughLoading(GameSceneNavigator.RaidSceneName), Is.True);
        yield return null;
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameSceneNavigator.LoadingSceneName));
        yield return WaitForActiveScene(GameSceneNavigator.RaidSceneName, 10f);
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameSceneNavigator.SchoolRaidSceneName));
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
        PlayerConditionHud conditionHud = Object.FindObjectOfType<PlayerConditionHud>();
        Assert.That(conditionHud, Is.Not.Null);
        Assert.That(conditionHud.transform.Find("PlayerConditionPanel/Bleeding")?.GetComponent<Text>(), Is.Not.Null);
        Assert.That(conditionHud.transform.Find("PlayerConditionPanel/Fracture")?.GetComponent<Text>(), Is.Not.Null);
        Assert.That(conditionHud.transform.Find("PlayerConditionPanel/Head"), Is.Null);
        Assert.That(conditionHud.transform.Find("BleedingScreenBorder")?.GetComponent<CanvasGroup>(), Is.Not.Null);
        Assert.That(Object.FindObjectsOfType<ZombieLootDrop>().Length, Is.GreaterThanOrEqualTo(3));
        Assert.That(Object.FindObjectsOfType<FieldLootSource>().Length, Is.GreaterThanOrEqualTo(3));
        // 학교 씬의 상자·바닥 지점·좀비가 모두 같은 지역 드랍 에셋을 읽는지 실제 씬 로드 상태에서 확인합니다.
        RegionLootController regionLootController = Object.FindObjectOfType<RegionLootController>();
        Assert.That(regionLootController, Is.Not.Null);
        Assert.That(regionLootController.Profile, Is.Not.Null);
        Assert.That(regionLootController.Profile.RegionId, Is.EqualTo("school"));
        Assert.That(RegionLootController.ActiveProfile, Is.SameAs(regionLootController.Profile));
        Assert.That(Object.FindObjectsOfType<RegionGroundLootPoint>().Length, Is.EqualTo(13));
        // 이제 학교가 공용 기능 테스트 씬이므로 같은 실행에서 학교 구역·2층 테스트 건물과 NavMesh까지 함께 검사합니다.
        Assert.That(GameObject.Find("RaidMap/Zone_SchoolEntrance"), Is.Not.Null);
        Assert.That(GameObject.Find("RaidMap/Zone_Administration"), Is.Not.Null);
        Assert.That(GameObject.Find("RaidMap/Zone_ClassroomWing"), Is.Not.Null);
        Assert.That(GameObject.Find("RaidMap/Zone_Courtyard"), Is.Not.Null);
        Assert.That(GameObject.Find("RaidMap/Zone_SpecialClassWing"), Is.Not.Null);
        Assert.That(GameObject.Find("RaidMap/Zone_Cafeteria"), Is.Not.Null);
        Assert.That(GameObject.Find("RaidMap/Zone_Gymnasium"), Is.Not.Null);
        Assert.That(GameObject.Find("RaidMap/Zone_PlaygroundExtraction"), Is.Not.Null);
        Assert.That(GameObject.Find("RaidMap/Zone_ClassroomWing/Building_TwoStoreyClassroom_Test"), Is.Not.Null);
        Assert.That(RaidMapLayout.GetZoneDisplayName(new Vector3(-27f, 0f, 2f)), Is.EqualTo("일반 교실동 1층"));
        Assert.That(
            Vector3.Distance(SchoolRaidMapLayout.PlayerStartPosition, SchoolRaidMapLayout.ExtractionPosition),
            Is.GreaterThanOrEqualTo(70f));

        yield return SceneManager.LoadSceneAsync(GameSceneNavigator.TitleSceneName);
    }

    // Loading 씬의 최소 표시 시간과 비동기 활성화를 실제 프레임으로 기다리되, 회귀 시 테스트가 무한히 멈추지 않도록 제한 시간을 둡니다.
    private static IEnumerator WaitForActiveScene(string expectedSceneName, float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (SceneManager.GetActiveScene().name != expectedSceneName && Time.realtimeSinceStartup < deadline)
            yield return null;

        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(expectedSceneName));
    }

    // 실제 Coroutine 시간을 흘려 0.45초 길게 누르면 팝업이 열리고, 손을 떼도 유지되며 뒤따르는 클릭은 실행되지 않는지 검사합니다.
    [UnityTest]
    public IEnumerator InventoryItem_LongPressShowsPopupAndSuppressesReleaseClick()
    {
        ItemDefinition item = CreateItem("popup_test_item", ItemType.Material);
        var entry = new InventoryEntry(item, 1, Vector2Int.zero);
        var owner = new PopupOwnerStub();
        EventSystem eventSystem = EventSystem.current;
        GameObject createdEventSystemObject = null;
        if (eventSystem == null)
        {
            createdEventSystemObject = new GameObject("PopupTestEventSystem", typeof(EventSystem));
            eventSystem = createdEventSystemObject.GetComponent<EventSystem>();
        }
        var itemViewObject = new GameObject(
            "PopupTestItemView",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(InventoryItemView));
        InventoryItemView itemView = itemViewObject.GetComponent<InventoryItemView>();
        itemView.Bind(entry, null, owner, false, 50f);

        var pointer = new PointerEventData(eventSystem)
        {
            button = PointerEventData.InputButton.Left,
            position = new Vector2(500f, 400f)
        };

        itemView.OnPointerDown(pointer);
        yield return new WaitForSecondsRealtime(InventoryItemView.DetailHoldDuration + 0.1f);
        Assert.That(owner.PopupShowCount, Is.EqualTo(1));

        itemView.OnPointerUp(pointer);
        itemView.OnPointerClick(pointer);
        Assert.That(owner.PopupHideCount, Is.Zero, "길게 눌러 연 설명은 마우스를 떼도 유지되어야 합니다.");
        Assert.That(owner.ItemClickCount, Is.Zero, "길게 누르기 직후의 PointerClick은 아이템 사용이나 컨테이너 열기로 전달되면 안 됩니다.");

        // 실제 화면에서는 투명 바깥 클릭 영역이 이 입력을 받아 닫습니다. View를 직접 호출하는 테스트에서는 새 누름이 이전 팝업 닫기를 요청합니다.
        itemView.OnPointerDown(pointer);
        itemView.OnPointerUp(pointer);
        itemView.OnPointerClick(pointer);
        Assert.That(owner.PopupHideCount, Is.EqualTo(1));
        Assert.That(owner.ItemClickCount, Is.EqualTo(1));

        Object.Destroy(itemViewObject);
        if (createdEventSystemObject != null) Object.Destroy(createdEventSystemObject);
        Object.Destroy(item);
        yield return null;
    }

    // 팝업 입력 테스트는 실제 인벤토리 데이터를 바꾸지 않고 View가 HUD에 어떤 명령을 보냈는지만 기록합니다.
    private sealed class PopupOwnerStub : IInventoryGridViewOwner
    {
        public int PopupShowCount { get; private set; }
        public int PopupHideCount { get; private set; }
        public int ItemClickCount { get; private set; }

        public void HandleItemClick(
            InventoryEntry entry,
            PlayerInventory.GridData grid,
            PointerEventData.InputButton button,
            bool shiftPressed,
            bool controlPressed)
        {
            ItemClickCount++;
        }

        public void HandleAutoSort(PlayerInventory.GridData grid) { }
        public void BeginItemDrag(
            InventoryItemView view,
            InventoryEntry entry,
            PlayerInventory.GridData source,
            Vector2 pointerOffset,
            PointerEventData eventData) { }
        public void UpdateItemDrag(PointerEventData eventData) { }
        public void EndItemDrag(PointerEventData eventData) { }

        public void ShowItemDetailPopup(InventoryEntry entry, Vector2 screenPosition, Camera eventCamera)
        {
            PopupShowCount++;
        }

        public void HideItemDetailPopup(InventoryEntry entry)
        {
            PopupHideCount++;
        }
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
