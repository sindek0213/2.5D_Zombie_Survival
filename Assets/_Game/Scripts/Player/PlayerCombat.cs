using System;
using System.Collections;
using UnityEngine;
using ZombieSurvival.AI;
using ZombieSurvival.Combat;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.Player
{
    // 플레이어의 현재 장착 무기 선택, 입력, 탄약 소비, 발사 간격, Raycast 명중과 재장전을 한곳에서 조정합니다.
    // Hitscan 방식은 실제 탄환 오브젝트를 날리지 않고 발사 순간 Ray로 명중 결과를 즉시 계산합니다.
    // PlayerAim이 계산한 수평 방향을 사용하며, 피해 대상은 IDamageable 인터페이스로 호출합니다.
    [RequireComponent(typeof(PlayerAim))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [SerializeField] private Transform weaponSocket;
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("정밀 조준")]
        // 우클릭 조준 중에는 무기 자체의 기본 명중 오차에 이 비율을 곱합니다.
        // 예를 들어 무기 오차가 4도이고 값이 0.35라면 조준 중 실제 오차 범위는 1.4도로 줄어듭니다.
        // 0에 너무 가까우면 모든 총이 완전히 같은 곳에 맞기 때문에 약간의 흔들림이 남도록 최솟값을 0.1로 제한합니다.
        [SerializeField, Range(0.1f, 1f)] private float aimingAccuracyMultiplier = 0.35f;

        [Header("맨손 근접 공격")]
        // 근접무기를 장착하지 않았을 때만 사용하는 맨손 기본값입니다.
        // 단검과 도끼를 장착하면 아래 값 대신 각 MeleeWeaponDefinition 에셋의 수치를 읽습니다.
        [SerializeField, Min(0.1f)] private float meleeRange = 1.45f;
        [SerializeField, Min(0.05f)] private float meleeRadius = 0.45f;
        [SerializeField, Min(0f)] private float meleeDamage = 18f;
        [SerializeField, Min(0.05f)] private float meleeCooldown = 0.65f;
        [SerializeField, Min(0f)] private float meleeKnockbackForce = 2.2f;

        // 물리 후보 수집과 자기 몸 제외 규칙은 별도 일반 C# 객체에 맡겨 이 클래스가 무기 상태와 발사 흐름에 집중하게 합니다.
        // MonoBehaviour가 아니므로 Inspector나 Player 프리팹에 새 컴포넌트를 추가할 필요가 없습니다.
        private readonly HitscanRaycaster hitscanRaycaster = new HitscanRaycaster();
        private readonly MeleeAttackResolver meleeAttackResolver = new MeleeAttackResolver();
        private PlayerAim playerAim;
        private PlayerInventory playerInventory;
        private PlayerMovement playerMovement;
        private PlayerCondition playerCondition;
        private PlayerStatusEffects statusEffects;
        private float nextFireTime;
        private float nextMeleeTime;
        private Coroutine reloadCoroutine;
        private InventoryEntry activeWeaponEntry;
        private EquipmentSlotType activeWeaponSlot = EquipmentSlotType.None;
        private bool inventorySubscribed;
        private bool actionInterruptsSubscribed;
        private string combatStatusMessage = string.Empty;
        private float combatStatusExpiresAt;

        public AmmunitionState Ammo { get; private set; }
        public bool IsReloading { get; private set; }
        // 총기가 실제로 선택된 상태에서 마우스 오른쪽 버튼을 누르고 있을 때만 true입니다.
        // CameraFollow는 이 값을 읽어 화면을 확대하고, 발사 코드는 같은 값을 읽어 명중 오차를 줄입니다.
        public bool IsAiming { get; private set; }
        public InventoryEntry ActiveWeaponEntry => activeWeaponEntry;
        public EquipmentSlotType ActiveWeaponSlot => activeWeaponSlot;
        public WeaponDefinition CurrentWeaponDefinition => activeWeaponEntry?.Item.WeaponDefinition;
        public MeleeWeaponDefinition CurrentMeleeWeaponDefinition => activeWeaponEntry?.Item.MeleeWeaponDefinition;
        public string CurrentWeaponName => activeWeaponEntry != null
            ? activeWeaponEntry.Item.DisplayName
            : "무기 없음";
        // 발사 방식은 무기 종류의 고정 기본값이 아니라 현재 들고 있는 무기 인스턴스에서 읽습니다.
        // 따라서 같은 소총 두 자루도 플레이어가 마지막으로 선택한 단발·연사 상태를 각각 기억합니다.
        public WeaponTriggerMode CurrentTriggerMode => activeWeaponEntry?.SelectedTriggerMode ?? WeaponTriggerMode.SemiAutomatic;
        public bool IsAutomatic => CurrentWeaponDefinition != null && CurrentTriggerMode == WeaponTriggerMode.Automatic;
        public string CurrentTriggerModeLabel => CurrentWeaponDefinition != null
            ? IsAutomatic ? "연사" : "단발"
            : CurrentMeleeWeaponDefinition != null ? "근접" : "맨손";
        public float CurrentWeaponDurability => activeWeaponEntry?.WeaponDurability ?? 0f;
        public float CurrentWeaponMaxDurability => CurrentWeaponDefinition?.MaxDurability ?? 0f;
        public float CurrentWeaponDurabilityNormalized => activeWeaponEntry?.WeaponDurabilityNormalized ?? 0f;
        public bool IsCurrentWeaponBroken => activeWeaponEntry?.IsWeaponBroken == true;
        // 짧은 전투 안내는 HUD가 매 프레임 읽으며, 유효 시간이 지나면 빈 문자열을 반환해 화면을 자동으로 정리합니다.
        public string CombatStatusMessage => Time.unscaledTime < combatStatusExpiresAt ? combatStatusMessage : string.Empty;
        public float CurrentDamage => CurrentWeaponDefinition != null
            ? CurrentWeaponDefinition.Damage * (activeWeaponEntry?.AttachmentDamageMultiplier ?? 1f) * OutgoingDamageMultiplier
            : (CurrentMeleeWeaponDefinition != null ? CurrentMeleeWeaponDefinition.Damage : meleeDamage) * OutgoingDamageMultiplier;
        private float OutgoingDamageMultiplier => statusEffects != null ? statusEffects.OutgoingDamageMultiplier : 1f;
        public float CurrentRange => CurrentWeaponDefinition != null
            ? CurrentWeaponDefinition.Range
            : CurrentMeleeWeaponDefinition != null ? CurrentMeleeWeaponDefinition.Range : meleeRange;
        public float CurrentFireInterval => CurrentWeaponDefinition != null
            ? CurrentWeaponDefinition.FireInterval * (activeWeaponEntry?.AttachmentFireIntervalMultiplier ?? 1f)
            : 0f;
        public float CurrentReloadDuration => CurrentWeaponDefinition != null
            ? CurrentWeaponDefinition.ReloadDuration *
              (activeWeaponEntry?.AttachmentReloadMultiplier ?? 1f) *
              CurrentWeaponDefinition.GetDurabilityReloadMultiplier(CurrentWeaponDurabilityNormalized) *
              (playerCondition != null ? playerCondition.ReloadDurationMultiplier : 1f)
            : 0f;
        public int CurrentPelletCount => CurrentWeaponDefinition != null ? CurrentWeaponDefinition.PelletCount : 0;
        public float CurrentSpreadAngle => CurrentWeaponDefinition != null ? CurrentWeaponDefinition.SpreadAngle : 0f;
        public float CurrentAccuracySpreadAngle => CurrentWeaponDefinition != null
            ? CurrentWeaponDefinition.AccuracySpreadAngle *
              (activeWeaponEntry?.AttachmentAccuracyMultiplier ?? 1f) *
              CurrentWeaponDefinition.GetDurabilityAccuracyMultiplier(CurrentWeaponDurabilityNormalized) *
              (playerCondition != null ? playerCondition.AimSpreadMultiplier : 1f)
            : 0f;
        public int CurrentMagazineSize => activeWeaponEntry?.EffectiveMagazineSize ?? 0;
        // CurrentAccuracySpreadAngle은 아이템에 저장된 원래 수치이고, 이 값은 현재 조준 상태까지 반영한 실제 발사 오차입니다.
        // 상세 정보 화면에는 원래 무기 성능을 보여 주고 실제 발사에만 조준 보너스를 적용하기 위해 두 값을 분리했습니다.
        public float EffectiveAccuracySpreadAngle => CalculateEffectiveAccuracySpread(
            CurrentAccuracySpreadAngle,
            IsAiming,
            aimingAccuracyMultiplier);
        public float CurrentKnockbackForce => CurrentWeaponDefinition != null ? CurrentWeaponDefinition.KnockbackForce : 0f;
        public int CurrentArmorPenetration => CurrentWeaponDefinition != null ? CurrentWeaponDefinition.ArmorPenetration : 0;
        // 결과 화면의 명중률은 펠릿 수가 아니라 방아쇠를 당긴 횟수를 기준으로 계산합니다.
        public int ShotsFired { get; private set; }
        public int SuccessfulShots { get; private set; }

        // 탄약 UI가 발사와 재장전 결과를 즉시 반영할 때 사용하는 이벤트입니다.
        public event Action AmmoChanged;

        // 장착 또는 숫자 키 선택으로 현재 무기가 바뀌면 HUD가 무기 이름과 선택 표시를 즉시 갱신할 수 있게 알립니다.
        public event Action WeaponChanged;

        // 발사에 성공할 때마다 총구 위치, 대표 탄도 끝점과 충돌 여부를 한 번만 전달합니다.
        // 총성·총구 섬광·캐릭터 반동처럼 방아쇠당 한 번 필요한 기능은 이 이벤트를 사용하고, 개별 탄도선은 아래 PelletResolved를 사용합니다.
        public event Action<Vector3, Vector3, bool> ShotResolved;

        // 한 번의 Shot 안에서 계산된 각 펠릿의 탄도 결과를 따로 전달합니다.
        // 산탄총은 이 이벤트가 설정된 펠릿 수만큼 발생하고 일반 총은 한 번만 발생하므로 화면에는 실제 판정 개수와 같은 탄도선이 보입니다.
        public event Action<Vector3, Vector3, bool> PelletResolved;

        // 근접 공격 전용 피드백 이벤트입니다. 총기 ShotResolved와 분리해 맨손·단검·도끼 공격이 총성·총구 섬광을 재생하지 않게 합니다.
        public event Action<Vector3, Vector3, bool> MeleeResolved;

        private void Awake()
        {
            playerAim = GetComponent<PlayerAim>();
            playerInventory = GetComponent<PlayerInventory>();
            playerMovement = GetComponent<PlayerMovement>();
            // 같은 플레이어의 상태 이상을 한 번만 찾아 조준과 재장전 수치 계산에서 매 프레임 GetComponent를 반복하지 않습니다.
            playerCondition = GetComponent<PlayerCondition>();
            statusEffects = GetComponent<PlayerStatusEffects>();
            // 실제 장비 슬롯에서 유효한 무기를 찾기 전까지는 탄약 상태를 만들지 않습니다.
            // 따라서 인벤토리에 무기가 없는 플레이어는 이름만 기본 권총으로 보이는 우회 없이 발사와 재장전을 모두 할 수 없습니다.
            Ammo = null;
        }

        private void OnEnable()
        {
            SubscribeToActionInterrupts();
            RefreshEquippedWeapon();
        }

        private void OnDisable()
        {
            // 인벤토리 화면은 PlayerControlGate를 통해 전투 컴포넌트를 잠시 비활성화합니다.
            // 단순히 창을 열었다는 이유로 재장전을 취소하지 않고, 장비 변경·아이템 사용 이벤트는 계속 구독합니다.
            // 조준 확대만 즉시 해제해 UI 뒤에서 카메라 확대가 남는 현상은 막습니다.
            SetAiming(false);
            // GameObject 자체가 꺼지면 Unity가 Coroutine을 자동 중단하므로 IsReloading만 true로 남을 수 있습니다.
            // 이는 플레이 행동에 의한 취소가 아니라 오브젝트 생명주기 정리이며, 다시 활성화됐을 때 영구 재장전 상태가 되는 것을 막습니다.
            if (!gameObject.activeInHierarchy) CancelReload();
        }

        private void OnDestroy()
        {
            // 오브젝트가 실제로 제거될 때에는 남은 코루틴과 C# 이벤트 구독을 정리해 파괴된 컴포넌트가 호출되지 않게 합니다.
            CancelReload();
            UnsubscribeFromActionInterrupts();
        }

        private void Update()
        {
            // 숫자 1~0은 모두 퀵슬롯이 사용하므로 무기 교체는 마우스 휠로 처리합니다.
            // 한 프레임의 휠 변화량 부호만 사용해 고해상도 휠이 큰 값을 보내도 무기를 여러 번 왕복하지 않게 합니다.
            float weaponScroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(weaponScroll) > 0.01f) TryCycleEquippedWeapon(weaponScroll);

            // B는 현재 무기가 단발과 연사를 모두 지원할 때만 발사 모드를 전환합니다.
            // 권총과 펌프 산탄총처럼 한 모드 전용 무기는 값을 바꾸지 않고 HUD에 이유를 표시합니다.
            if (Input.GetKeyDown(KeyCode.B)) TryCycleFireMode();

            // 총기가 있을 때 마우스 오른쪽 버튼을 누르고 있는 동안만 정밀 조준 상태를 유지합니다.
            // GetMouseButtonDown이 아니라 GetMouseButton을 사용하므로 버튼을 놓는 프레임에 곧바로 일반 시야와 정확도로 돌아갑니다.
            SetAiming(Input.GetMouseButton(1));

            // 반자동은 클릭한 프레임에만, 자동은 누르고 있는 모든 프레임에 발사를 요청합니다.
            // 실제 연사 속도는 TryFire의 nextFireTime 검사가 제한하므로 프레임 속도가 높아져도 설정값보다 빨리 발사되지 않습니다.
            if (CurrentWeaponDefinition == null)
            {
                if (Input.GetMouseButtonDown(0)) TryMelee();
            }
            else
            {
                bool fireRequested = IsAutomatic ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
                if (fireRequested) TryFire();
            }
            if (Input.GetKeyDown(KeyCode.R)) TryStartReload();
        }

        // 인벤토리 장비 슬롯에 있는 무기를 마우스 휠 순환이나 다른 시스템의 직접 요청으로 선택합니다.
        // 숫자 1~0은 퀵슬롯 전용이므로 이 메서드 안에서도 숫자 입력을 직접 검사하지 않습니다.
        // 총기 또는 근접무기 데이터가 없는 잘못된 Weapon 아이템은 선택하지 않아 현재 유효한 무기를 그대로 보존합니다.
        public bool TrySelectWeaponSlot(EquipmentSlotType slot)
        {
            if (slot != EquipmentSlotType.PrimaryWeapon && slot != EquipmentSlotType.SecondaryWeapon) return false;
            EnsureInventoryReference();
            InventoryEntry entry = FindEquippedWeapon(slot);
            if (entry == null) return false;

            SetActiveWeapon(slot, entry);
            return true;
        }

        // 장착된 주무기와 보조무기 사이를 마우스 휠 방향에 맞춰 순환합니다.
        // 슬롯이 하나만 채워져 있으면 그 무기를 선택하고, 둘 다 비어 있으면 현재 맨손 상태를 유지합니다.
        public bool TryCycleEquippedWeapon(float scrollDirection)
        {
            if (Mathf.Abs(scrollDirection) <= 0.01f) return false;
            EnsureInventoryReference();
            InventoryEntry primary = FindEquippedWeapon(EquipmentSlotType.PrimaryWeapon);
            InventoryEntry secondary = FindEquippedWeapon(EquipmentSlotType.SecondaryWeapon);
            if (primary == null && secondary == null) return false;

            if (primary != null && secondary != null)
            {
                EquipmentSlotType next = activeWeaponSlot == EquipmentSlotType.PrimaryWeapon
                    ? EquipmentSlotType.SecondaryWeapon
                    : activeWeaponSlot == EquipmentSlotType.SecondaryWeapon
                        ? EquipmentSlotType.PrimaryWeapon
                        : scrollDirection > 0f
                            ? EquipmentSlotType.PrimaryWeapon
                            : EquipmentSlotType.SecondaryWeapon;
                return TrySelectWeaponSlot(next);
            }

            return TrySelectWeaponSlot(primary != null
                ? EquipmentSlotType.PrimaryWeapon
                : EquipmentSlotType.SecondaryWeapon);
        }

        // PlayerInventory가 장착 또는 해제된 뒤 현재 선택이 여전히 유효한지 확인합니다.
        // 선택한 무기가 사라지면 주무기, 보조무기 순으로 대체하고 둘 다 없을 때는 무장 해제 상태로 돌아갑니다.
        private void RefreshEquippedWeapon()
        {
            EnsureInventoryReference();
            if (playerInventory == null) return;

            InventoryEntry current = FindEquippedWeapon(activeWeaponSlot);
            if (current == activeWeaponEntry && current != null)
            {
                // 탄약을 줍거나 다른 시스템이 수량을 바꾸면 무기는 그대로여도 HUD 예비탄 숫자는 즉시 다시 계산해야 합니다.
                SynchronizeAmmoState();
                AmmoChanged?.Invoke();
                return;
            }

            InventoryEntry primary = FindEquippedWeapon(EquipmentSlotType.PrimaryWeapon);
            if (primary != null)
            {
                SetActiveWeapon(EquipmentSlotType.PrimaryWeapon, primary);
                return;
            }

            InventoryEntry secondary = FindEquippedWeapon(EquipmentSlotType.SecondaryWeapon);
            if (secondary != null)
            {
                SetActiveWeapon(EquipmentSlotType.SecondaryWeapon, secondary);
                return;
            }

            SetActiveWeapon(EquipmentSlotType.None, null);
        }

        private InventoryEntry FindEquippedWeapon(EquipmentSlotType slot)
        {
            if (playerInventory == null || slot == EquipmentSlotType.None ||
                !playerInventory.Equipment.TryGetValue(slot, out InventoryEntry entry)) return null;

            return entry.Item.IsCombatWeapon ? entry : null;
        }

        // 선택한 무기의 InventoryEntry에 저장된 실제 탄창 수량과 인벤토리의 호환 탄약 수량으로 HUD용 상태를 만듭니다.
        // 탄창이 PlayerCombat의 임시 Dictionary가 아니라 아이템 인스턴스에 있으므로 씬 이동과 저장 뒤에도 같은 수량을 복원할 수 있습니다.
        private void SetActiveWeapon(EquipmentSlotType slot, InventoryEntry entry)
        {
            if (activeWeaponSlot == slot && activeWeaponEntry == entry) return;
            CancelReload("무기 교체로 재장전이 취소되었습니다.");

            activeWeaponSlot = slot;
            activeWeaponEntry = entry;
            if (entry == null)
            {
                Ammo = null;
                // 장착한 총이 사라졌다면 우클릭을 계속 누르고 있어도 조준과 카메라 확대가 유지되어서는 안 됩니다.
                SetAiming(false);
            }
            else if (entry.Item.WeaponDefinition != null)
            {
                WeaponDefinition definition = entry.Item.WeaponDefinition;
                Ammo = new AmmunitionState(
                    entry.EffectiveMagazineSize,
                    entry.MagazineRounds,
                    playerInventory != null ? playerInventory.CountAmmo(definition.Caliber) : 0);
            }
            else
            {
                // 근접무기는 탄창과 예비탄 상태가 없으므로 Ammo를 만들지 않습니다.
                // 이전에 총기를 사용하다 근접무기로 바꿨을 때 이전 총의 탄 수가 HUD에 남는 것도 여기서 막습니다.
                Ammo = null;
                SetAiming(false);
            }

            // 무기를 바꾸자마자 이전 무기의 발사 간격이 새 무기를 불필요하게 막지 않도록 현재 시점부터 발사를 허용합니다.
            nextFireTime = Time.time;
            WeaponChanged?.Invoke();
            AmmoChanged?.Invoke();
        }

        // 현재 선택 무기의 저장된 탄창과 실제 인벤토리 탄약 수량을 HUD용 AmmunitionState에 복사합니다.
        private void SynchronizeAmmoState()
        {
            if (Ammo == null || activeWeaponEntry == null || CurrentWeaponDefinition == null) return;
            int reserve = playerInventory != null
                ? playerInventory.CountAmmo(CurrentWeaponDefinition.Caliber)
                : 0;
            Ammo.ResizeAndSynchronize(activeWeaponEntry.EffectiveMagazineSize, activeWeaponEntry.MagazineRounds, reserve);
        }

        private void EnsureInventoryReference()
        {
            if (playerInventory == null) playerInventory = GetComponent<PlayerInventory>();
        }

        private void SubscribeToActionInterrupts()
        {
            EnsureInventoryReference();
            if (playerInventory != null && !inventorySubscribed)
            {
                playerInventory.InventoryChanged += RefreshEquippedWeapon;
                inventorySubscribed = true;
            }
            if (actionInterruptsSubscribed) return;

            // PlayerCombat이 인벤토리 창 때문에 disabled 상태여도 C# 이벤트는 받을 수 있습니다.
            // 따라서 장비 변경과 아이템 사용은 UI 안에서 발생해도 재장전을 즉시 취소합니다.
            if (playerInventory != null)
            {
                playerInventory.EquipmentChanged += HandleEquipmentChanged;
                playerInventory.ItemUseStarted += HandleItemUseStarted;
            }
            if (playerMovement != null) playerMovement.RollStarted += HandleRollStarted;
            actionInterruptsSubscribed = true;
        }

        private void UnsubscribeFromActionInterrupts()
        {
            if (playerInventory != null && inventorySubscribed)
                playerInventory.InventoryChanged -= RefreshEquippedWeapon;
            inventorySubscribed = false;
            if (playerInventory != null)
            {
                playerInventory.EquipmentChanged -= HandleEquipmentChanged;
                playerInventory.ItemUseStarted -= HandleItemUseStarted;
            }
            if (playerMovement != null) playerMovement.RollStarted -= HandleRollStarted;
            actionInterruptsSubscribed = false;
        }

        private void HandleRollStarted()
            => CancelReload("구르기로 재장전이 취소되었습니다.");

        private void HandleEquipmentChanged()
            => CancelReload("장비 변경으로 재장전이 취소되었습니다.");

        private void HandleItemUseStarted()
            => CancelReload("아이템 사용으로 재장전이 취소되었습니다.");

        // 외부 코드나 입력에서 발사를 요청할 때 호출합니다.
        // 재장전 중이면 장전만 취소하고, 그 외에는 탄약과 연사 쿨다운 검사를 통과한 경우에만 실제 탄환을 발사합니다.
        // 발사 가능하면 탄약을 먼저 한 발 소비한 뒤 총구 위치에서 조준 방향으로 Raycast를 수행합니다.
        public bool TryFire()
        {
            // 발사 입력은 재장전만 취소하고 같은 입력으로 즉시 총알까지 발사하지 않습니다.
            // Update뿐 아니라 테스트·AI 보조 입력처럼 TryFire를 직접 호출하는 경로에도 같은 규칙을 적용합니다.
            if (IsReloading)
            {
                CancelReload("발사 입력으로 재장전이 취소되었습니다.");
                return false;
            }
            if (activeWeaponEntry?.IsWeaponBroken == true)
            {
                SetCombatStatus("무기가 파손되어 발사할 수 없습니다. 거점에서 수리하세요.");
                return false;
            }
            if (Ammo == null || !CanFire(Ammo.InMagazine, IsReloading, Time.time, nextFireTime)) return false;

            // 탄창의 실제 원본은 InventoryEntry이므로 발사 성공 시 해당 무기 인스턴스에서 직접 한 발을 제거합니다.
            if (activeWeaponEntry == null || !activeWeaponEntry.TryConsumeMagazineRound()) return false;
            SynchronizeAmmoState();
            nextFireTime = Time.time + CurrentFireInterval;
            // 실제 탄이 나간 발사만 내구도를 감소시킵니다. 이번 발사로 0이 되어도 이미 소비한 한 발의 판정은 끝까지 처리합니다.
            activeWeaponEntry.ConsumeWeaponDurability(CurrentWeaponDefinition.DurabilityLossPerShot);
            AmmoChanged?.Invoke();

            Vector3 origin = weaponSocket != null
                ? weaponSocket.position
                : transform.position + Vector3.up;
            // 조준 방향 전체에 무기 정확도만큼 작은 무작위 오차를 먼저 적용합니다.
            // 산탄총은 이 기준 방향을 중심으로 각 펠릿 확산을 추가하므로 한 발의 산탄 무늬가 함께 조금씩 흔들립니다.
            // 일반 사격은 무기의 원래 오차를 사용하고, 우클릭 정밀 조준 중에는 감소된 실제 오차를 사용합니다.
            // 탄환 방향 계산만 바뀌므로 피해량, 사거리, 연사 속도와 탄약 소비량은 조준 전후에 동일합니다.
            float accuracyAngle = CalculateAccuracyAngle(UnityEngine.Random.value, EffectiveAccuracySpreadAngle);
            Vector3 baseDirection = Quaternion.AngleAxis(accuracyAngle, Vector3.up) * playerAim.AimDirection;
            float shotRange = CurrentRange;
            int pelletCount = CurrentPelletCount;
            float spreadAngle = CurrentSpreadAngle;

            // ShotResolved는 총성·총구 섬광이 한 발당 한 번만 재생되도록 가운데 펠릿의 결과만 대표값으로 전달합니다.
            // 실제 피해 판정은 아래 반복문에서 모든 펠릿을 독립적으로 처리하므로 산탄총은 가까울수록 여러 펠릿이 맞아 강해집니다.
            int representativePellet = pelletCount / 2;
            Vector3 shotEnd = origin + baseDirection * shotRange;
            bool hitSomething = false;
            bool shotDamagedTarget = false;
            ShotsFired++;

            for (int pelletIndex = 0; pelletIndex < pelletCount; pelletIndex++)
            {
                float angle = CalculatePelletAngle(pelletIndex, pelletCount, spreadAngle);
                Vector3 pelletDirection = Quaternion.AngleAxis(angle, Vector3.up) * baseDirection;
                Vector3 pelletEnd = origin + pelletDirection * shotRange;
                bool pelletHitSomething = false;

                if (hitscanRaycaster.TryGetFirstNonSelfHit(
                        transform,
                        origin,
                        pelletDirection,
                        shotRange,
                        hitMask,
                        out RaycastHit hit))
                {
                    pelletHitSomething = true;
                    pelletEnd = hit.point;
                    // Collider의 부모까지 검색해 자식 충돌체를 가진 적도 피해를 받게 합니다.
                    IDetailedDamageable detailedDamageable = hit.collider.GetComponentInParent<IDetailedDamageable>();
                    if (detailedDamageable != null)
                    {
                        Transform hitRoot = hit.collider.GetComponentInParent<Health>()?.transform;
                        // 명중 위치는 적에게 적용할 피해 배율만 결정하며 플레이어 상태 이상 시스템과는 분리합니다.
                        BodyPart bodyPart = Health.ResolveBodyPart(hitRoot, hit.point);
                        detailedDamageable.TakeDamage(new DamageInfo(
                            CurrentDamage,
                            hit.point,
                            bodyPart,
                            CurrentArmorPenetration,
                            Mathf.Clamp01(CurrentDamage / 180f),
                            Mathf.Clamp01(CurrentDamage / 260f),
                            false,
                            DamageSource.Gunfire,
                            activeWeaponEntry.Item.ItemId));
                        shotDamagedTarget = true;
                    }
                    else
                    {
                        IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
                        if (damageable != null)
                        {
                            damageable.TakeDamage(CurrentDamage, hit.point);
                            shotDamagedTarget = true;
                        }
                    }
                    // 대상이 밀림 인터페이스를 지원할 때만 힘을 전달하므로 벽·상자 같은 고정 오브젝트에는 추가 처리가 없습니다.
                    IKnockbackReceiver knockbackReceiver = hit.collider.GetComponentInParent<IKnockbackReceiver>();
                    knockbackReceiver?.ApplyKnockback(pelletDirection * CurrentKnockbackForce);
                    Debug.DrawLine(origin, hit.point, Color.red, 0.35f);
                }
                else
                {
                    Debug.DrawRay(origin, pelletDirection * shotRange, Color.yellow, 0.35f);
                }

                // 아래 대표 Shot 이벤트와 달리 펠릿 이벤트는 반복마다 한 번씩 보내야 산탄총의 모든 탄도선을 그릴 수 있습니다.
                // 탄약 소비·총성·총구 섬광은 여전히 반복문 밖의 ShotResolved 한 번만 사용하므로 5발의 산탄이 쉘 5개를 소모하지 않습니다.
                PelletResolved?.Invoke(origin, pelletEnd, pelletHitSomething);

                if (pelletIndex != representativePellet) continue;
                shotEnd = pelletEnd;
                hitSomething = pelletHitSomething;
            }

            // 모든 판정이 끝난 뒤 한 번만 알려 탄도선이 실제 벽 또는 적과 멈춘 위치에 정확히 맞도록 합니다.
            ShotResolved?.Invoke(origin, shotEnd, hitSomething);
            if (shotDamagedTarget) SuccessfulShots++;
            // 총성은 무기 사거리의 일부만큼 주변 좀비에게 전달됩니다.
            // 소음 이벤트는 명중 여부와 관계없이 방아쇠를 당겨 실제 한 발이 소비된 경우에만 한 번 발생합니다.
            ZombieNoiseSystem.Emit(
                origin,
                Mathf.Clamp(CurrentRange * 0.75f, 12f, 32f),
                ZombieNoiseSystem.NoiseKind.Gunshot);

            // 내구도 표시는 탄약 UI와 아이템 상세창 양쪽에서 읽으므로 인벤토리 상태 변경 이벤트도 한 번 전달합니다.
            playerInventory?.NotifyItemStateChanged();

            return true;
        }

        // 현재 근접무기 에셋의 수치로 공격하며, 근접무기가 없으면 Inspector의 맨손 기본값을 사용합니다.
        // SphereCast 결과를 거리순으로 확인하고 자기 Collider를 제외합니다. 벽에서는 멈추고 도끼 같은 다중 대상 무기는 설정된 수만큼 계속 처리합니다.
        public bool TryMelee()
        {
            if (CurrentWeaponDefinition != null || Time.time < nextMeleeTime) return false;

            MeleeWeaponDefinition melee = CurrentMeleeWeaponDefinition;
            float attackRange = melee != null ? melee.Range : meleeRange;
            float attackRadius = melee != null ? melee.HitRadius : meleeRadius;
            // 총기와 같은 규칙으로 주는 피해 버프를 적용합니다. CurrentDamage를 사용하면 장착 근접무기와 맨손 모두 한 계산 경로를 공유합니다.
            float attackDamage = CurrentDamage;
            float attackInterval = melee != null ? melee.AttackInterval : meleeCooldown;
            float attackKnockback = melee != null ? melee.KnockbackForce : meleeKnockbackForce;
            int armorPenetration = melee != null ? melee.ArmorPenetration : 0;
            int maximumTargets = melee != null ? melee.MaximumTargets : 1;
            float noiseRadius = melee != null ? melee.NoiseRadius : 4f;
            string sourceId = activeWeaponEntry?.Item.ItemId ?? "unarmed";

            nextMeleeTime = Time.time + attackInterval;
            Vector3 direction = playerAim != null ? playerAim.AimDirection : transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f) direction = transform.forward;
            direction.Normalize();

            Vector3 origin = transform.position + Vector3.up;
            MeleeAttackResult result = meleeAttackResolver.Resolve(
                transform,
                origin,
                direction,
                attackRange,
                attackRadius,
                maximumTargets,
                attackDamage,
                attackKnockback,
                armorPenetration,
                sourceId,
                hitMask);

            MeleeResolved?.Invoke(origin, result.EndPoint, result.DamagedTarget);
            if (noiseRadius > 0f)
                ZombieNoiseSystem.Emit(origin, noiseRadius, ZombieNoiseSystem.NoiseKind.Melee);
            string attackName = melee != null ? CurrentWeaponName : "맨손 공격";
            SetCombatStatus(
                result.DamagedTarget ? $"{attackName}이(가) 적중했습니다." : $"{attackName}이(가) 빗나갔습니다.",
                0.8f);
            return true;
        }

        // 이미 재장전 중이거나 같은 탄종의 인벤토리 탄약이 없거나 탄창이 가득 찼다면 false를 반환합니다.
        // 조건을 통과하면 Coroutine을 시작하며, 재장전 시간이 끝난 순간 필요한 수량만 실제 InventoryEntry에서 제거합니다.
        public bool TryStartReload()
        {
            SynchronizeAmmoState();
            if (Ammo == null || activeWeaponEntry == null || CurrentWeaponDefinition == null ||
                CurrentWeaponDefinition.Caliber == AmmoType.None || IsReloading ||
                activeWeaponEntry.IsWeaponBroken || Ammo.Reserve <= 0 || Ammo.InMagazine >= Ammo.MagazineSize) return false;

            // 일반 이동과 조준은 재장전과 함께 할 수 있습니다. 구르기는 시작 성공 이벤트가 발생하는 순간에만 재장전을 취소합니다.
            reloadCoroutine = StartCoroutine(ReloadRoutine(activeWeaponEntry, CurrentReloadDuration));
            return true;
        }

        private IEnumerator ReloadRoutine(InventoryEntry reloadingWeapon, float duration)
        {
            IsReloading = true;
            AmmoChanged?.Invoke();
            yield return new WaitForSeconds(duration);

            // 구르기·발사·장비/무기 변경·아이템 사용으로 취소되지 않고 같은 무기를 계속 들고 있을 때만 실제 탄약을 옮깁니다.
            if (activeWeaponEntry == reloadingWeapon) TransferAmmoFromInventory(reloadingWeapon);
            IsReloading = false;
            reloadCoroutine = null;
            SetCombatStatus("재장전 완료", 1.2f);
            AmmoChanged?.Invoke();
        }

        private int TransferAmmoFromInventory(InventoryEntry weaponEntry)
        {
            if (weaponEntry == null || playerInventory == null || weaponEntry.Item.WeaponDefinition == null) return 0;

            WeaponDefinition definition = weaponEntry.Item.WeaponDefinition;
            int available = playerInventory.CountAmmo(definition.Caliber);
            int requested = CalculateReloadRequest(
                weaponEntry.MagazineRounds,
                weaponEntry.EffectiveMagazineSize,
                available);
            if (requested <= 0 || !playerInventory.TryConsumeAmmo(definition.Caliber, requested, out int consumed))
                return 0;

            int loaded = weaponEntry.AddMagazineRounds(consumed);
            SynchronizeAmmoState();
            return loaded;
        }

        // 허용된 취소 행동이 재장전 중간에 발생하면 Coroutine과 표시 상태를 함께 정리합니다.
        // notify가 필요한 경우에만 이벤트를 보내 불필요한 HUD 갱신을 만들지 않습니다.
        private bool CancelReload(string reason = null)
        {
            if (reloadCoroutine != null)
            {
                StopCoroutine(reloadCoroutine);
                reloadCoroutine = null;
            }

            if (!IsReloading) return false;
            IsReloading = false;
            if (!string.IsNullOrWhiteSpace(reason)) SetCombatStatus(reason);
            AmmoChanged?.Invoke();
            return true;
        }

        // 현재 무기 인스턴스의 발사 모드를 바꾸고 인벤토리 변경 이벤트를 보내 저장·상세 UI도 같은 값을 보게 합니다.
        public bool TryCycleFireMode()
        {
            if (activeWeaponEntry == null || CurrentWeaponDefinition == null) return false;
            if (!activeWeaponEntry.TryCycleTriggerMode())
            {
                SetCombatStatus($"{CurrentWeaponName}은(는) {CurrentTriggerModeLabel} 전용 무기입니다.");
                return false;
            }

            SetCombatStatus($"발사 모드: {CurrentTriggerModeLabel}");
            playerInventory?.NotifyItemStateChanged();
            WeaponChanged?.Invoke();
            return true;
        }

        private void SetCombatStatus(string message, float duration = 2f)
        {
            combatStatusMessage = message ?? string.Empty;
            combatStatusExpiresAt = Time.unscaledTime + Mathf.Max(0.1f, duration);
        }

        // loadedRounds는 탄창 수량, isReloading은 재장전 상태입니다.
        // currentTime이 nextAllowedTime 이상이어야 이전 발사의 쿨다운이 끝난 것입니다.
        // Unity 오브젝트 없이도 테스트할 수 있도록 상태 판정을 정적 순수 함수로 분리했습니다.
        public static bool CanFire(int loadedRounds, bool isReloading, float currentTime, float nextAllowedTime)
        {
            return loadedRounds > 0 && !isReloading && currentTime >= nextAllowedTime;
        }

        // 탄창의 빈자리와 가진 호환 탄약 중 더 작은 수량만 재장전 요청으로 반환합니다.
        // GameObject 없이 경계값을 검사할 수 있어 인벤토리 소비 전에 과도한 수량을 요청하는 오류를 테스트로 막습니다.
        public static int CalculateReloadRequest(int loadedRounds, int magazineSize, int availableRounds)
        {
            int safeMagazineSize = Mathf.Max(1, magazineSize);
            int safeLoaded = Mathf.Clamp(loadedRounds, 0, safeMagazineSize);
            return Mathf.Min(safeMagazineSize - safeLoaded, Mathf.Max(0, availableRounds));
        }

        // 펠릿을 전체 확산 각도의 왼쪽 끝부터 오른쪽 끝까지 같은 간격으로 배치합니다.
        // 한 발짜리 일반 총기는 항상 0도를 반환하며, 예를 들어 5발·20도라면 -10, -5, 0, 5, 10도가 됩니다.
        public static float CalculatePelletAngle(int pelletIndex, int pelletCount, float totalSpreadAngle)
        {
            int safeCount = Mathf.Max(1, pelletCount);
            if (safeCount == 1) return 0f;

            int safeIndex = Mathf.Clamp(pelletIndex, 0, safeCount - 1);
            float safeSpread = Mathf.Clamp(totalSpreadAngle, 0f, 90f);
            float normalizedPosition = safeIndex / (float)(safeCount - 1);
            return Mathf.Lerp(-safeSpread * 0.5f, safeSpread * 0.5f, normalizedPosition);
        }

        // 0~1 난수 값을 무기 정확도 각도의 왼쪽 끝부터 오른쪽 끝 사이 값으로 변환합니다.
        // 난수 생성을 함수 밖에서 전달하는 형태라 경계값을 EditMode 테스트에서 항상 같은 결과로 검사할 수 있습니다.
        public static float CalculateAccuracyAngle(float normalizedRoll, float totalAccuracySpreadAngle)
        {
            float safeSpread = Mathf.Clamp(totalAccuracySpreadAngle, 0f, 15f);
            return Mathf.Lerp(-safeSpread * 0.5f, safeSpread * 0.5f, Mathf.Clamp01(normalizedRoll));
        }

        // 외부 입력 장치나 테스트에서도 같은 조준 규칙을 사용할 수 있도록 상태 변경을 한 메서드로 모았습니다.
        // requested가 true여도 현재 총기가 없거나 전투 컴포넌트가 비활성화되어 있으면 조준 상태가 될 수 없습니다.
        public void SetAiming(bool requested)
        {
            IsAiming = requested && isActiveAndEnabled && CurrentWeaponDefinition != null;
        }

        // baseSpread는 무기 에셋의 원래 명중 오차이고 multiplier는 조준 중 남길 오차 비율입니다.
        // 조준하지 않을 때는 원래 값을 그대로 돌려주며, 잘못된 음수나 과도한 배율이 들어와도 안전한 범위로 보정합니다.
        // 순수 계산 함수로 분리했기 때문에 마우스 입력 없이도 EditMode 테스트에서 정확한 조준 보너스를 검사할 수 있습니다.
        public static float CalculateEffectiveAccuracySpread(float baseSpread, bool isAiming, float multiplier)
        {
            float safeBaseSpread = Mathf.Clamp(baseSpread, 0f, 15f);
            if (!isAiming) return safeBaseSpread;
            return safeBaseSpread * Mathf.Clamp(multiplier, 0.1f, 1f);
        }
    }
}
