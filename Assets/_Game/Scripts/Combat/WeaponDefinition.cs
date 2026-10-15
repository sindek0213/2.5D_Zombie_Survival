using UnityEngine;

namespace ZombieSurvival.Combat
{
    // 한 번 눌렀을 때 한 발만 나가는 반자동과 버튼을 누르는 동안 계속 발사하는 자동을 구분합니다.
    // 발사 입력 방식만 나타내며 실제 발사 가능 여부와 탄약 소비는 PlayerCombat이 최종 판단합니다.
    public enum WeaponTriggerMode
    {
        SemiAutomatic,
        Automatic
    }

    // 무기와 탄약 아이템이 서로 호환되는지 비교할 때 사용하는 탄종입니다.
    // None은 총기 데이터가 아직 설정되지 않은 잘못된 상태이며, 이 상태의 무기는 발사는 가능해도 인벤토리 탄약으로 재장전할 수 없습니다.
    public enum AmmoType
    {
        None,
        NineMillimeter,
        FiveFiveSix,
        // 기존 저장 데이터의 enum 숫자가 바뀌지 않도록 새 탄종은 항상 배열 마지막에 추가합니다.
        TwelveGauge
    }

    // 피해량, 사거리와 탄창 크기처럼 같은 종류의 총기가 공유하는 고정 전투 수치를 저장합니다.
    // ItemDefinition은 인벤토리 크기와 장착 위치를 담당하고, 이 에셋은 전투 수치만 담당하도록 분리했습니다.
    // 실제 장전 탄 수는 무기 InventoryEntry에, 예비탄은 PlayerInventory의 탄약 아이템 수량으로 저장됩니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Weapon Definition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "무기";
        [SerializeField] private WeaponTriggerMode triggerMode = WeaponTriggerMode.SemiAutomatic;
        [Header("발사 모드")]
        // triggerMode는 무기를 처음 얻었을 때 선택되는 기본 모드입니다.
        // 아래 두 값은 B키로 전환할 수 있는 모드를 뜻하며, 권총·산탄총처럼 전환이 필요 없는 무기는 기본 모드 하나만 켜 둡니다.
        [SerializeField] private bool supportsSemiAutomatic = true;
        [SerializeField] private bool supportsAutomatic;
        [SerializeField, Min(0.01f)] private float damage = 34f;
        [SerializeField, Min(0.1f)] private float range = 30f;
        [SerializeField, Min(0.01f)] private float fireInterval = 0.2f;
        [SerializeField, Min(0f)] private float reloadDuration = 1.1f;
        [SerializeField, Min(1)] private int magazineSize = 8;
        [SerializeField] private AmmoType ammoType = AmmoType.NineMillimeter;

        [Header("산탄 설정")]
        // 한 번 방아쇠를 당겼을 때 계산할 실제 피해 Ray 수입니다.
        // 일반 총은 1로 두고 산탄총마다 5, 8, 12처럼 다른 값을 지정하면 같은 발사 코드를 그대로 재사용할 수 있습니다.
        [Tooltip("한 번 발사할 때 나가는 펠릿 수입니다. 일반 총기는 1로 설정합니다.")]
        [SerializeField, Min(1)] private int pelletCount = 1;
        // 첫 펠릿부터 마지막 펠릿까지 차지하는 전체 부채꼴 각도입니다.
        // 예를 들어 5발·16도면 조준 방향을 기준으로 -8, -4, 0, 4, 8도 방향에 각각 하나씩 발사됩니다.
        [Tooltip("모든 펠릿이 퍼지는 전체 각도입니다. 값이 클수록 좌우로 넓게 퍼집니다.")]
        [SerializeField, Range(0f, 90f)] private float spreadAngle;

        [Header("명중 오차와 충격")]
        [SerializeField, Range(0f, 15f)] private float accuracySpreadAngle = 1f;
        [SerializeField, Min(0f)] private float knockbackForce = 1f;
        [SerializeField, Range(0, 5)] private int armorPenetration = 1;

        [Header("무기 내구도")]
        // 내구도는 무기 종류가 공유하는 최대치이고, 현재 남은 값은 각 InventoryEntry에 따로 저장됩니다.
        // durabilityLossPerShot만 조절하면 빠르게 닳는 임시 무기와 오래 쓰는 고급 무기를 같은 코드로 만들 수 있습니다.
        [SerializeField, Min(1f)] private float maxDurability = 100f;
        [SerializeField, Min(0f)] private float durabilityLossPerShot = 0.2f;
        // 내구도가 0에 가까울 때 기본 명중 오차와 재장전 시간에 곱할 최대 페널티입니다.
        // 실제 배율은 현재 내구도 비율에 따라 1에서 아래 값까지 부드럽게 증가합니다.
        [SerializeField, Range(1f, 4f)] private float wornAccuracySpreadMultiplier = 2f;
        [SerializeField, Range(1f, 3f)] private float wornReloadDurationMultiplier = 1.5f;
        [SerializeField, Min(0)] private int repairCostPerPoint = 2;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "무기" : displayName;
        public WeaponTriggerMode TriggerMode => triggerMode;
        public bool IsAutomatic => triggerMode == WeaponTriggerMode.Automatic;
        public bool SupportsSemiAutomatic => supportsSemiAutomatic || triggerMode == WeaponTriggerMode.SemiAutomatic;
        public bool SupportsAutomatic => supportsAutomatic || triggerMode == WeaponTriggerMode.Automatic;
        public float Damage => Mathf.Max(0.01f, damage);
        public float Range => Mathf.Max(0.1f, range);
        public float FireInterval => Mathf.Max(0.01f, fireInterval);
        public float ReloadDuration => Mathf.Max(0f, reloadDuration);
        public int MagazineSize => Mathf.Max(1, magazineSize);
        public AmmoType Caliber => ammoType;
        // 일반 총기는 1이고 산탄총은 한 번의 발사에서 나가는 작은 탄환 수를 사용합니다.
        public int PelletCount => Mathf.Max(1, pelletCount);
        // 전체 펠릿이 왼쪽 끝부터 오른쪽 끝까지 퍼지는 XZ 평면의 총 각도입니다.
        public float SpreadAngle => Mathf.Clamp(spreadAngle, 0f, 90f);
        // 조준점 중심에서 한 발 전체가 무작위로 흔들릴 수 있는 총 각도이며, 값이 작을수록 정확한 무기입니다.
        public float AccuracySpreadAngle => Mathf.Clamp(accuracySpreadAngle, 0f, 15f);
        // 명중한 대상이 IKnockbackReceiver를 구현했다면 탄환 진행 방향으로 더해 줄 밀림 속도입니다.
        public float KnockbackForce => Mathf.Max(0f, knockbackForce);
        public int ArmorPenetration => Mathf.Clamp(armorPenetration, 0, 5);
        public float MaxDurability => Mathf.Max(1f, maxDurability);
        public float DurabilityLossPerShot => Mathf.Max(0f, durabilityLossPerShot);
        public int RepairCostPerPoint => Mathf.Max(0, repairCostPerPoint);
        // 모든 펠릿이 같은 대상에 맞았을 때 가능한 최대 피해량이며 상세 비교 화면에서 사용합니다.
        public float PotentialDamagePerShot => Damage * PelletCount;

        // 저장 데이터나 Inspector 값이 현재 무기에서 사용할 수 없는 모드를 가리키면 기본 모드로 되돌립니다.
        // 기본 모드까지 잘못 설정된 예외 상황에서는 실제로 지원되는 첫 번째 모드를 선택해 발사 불가능 상태를 막습니다.
        public WeaponTriggerMode ClampTriggerMode(WeaponTriggerMode requested)
        {
            if (SupportsTriggerMode(requested)) return requested;
            if (SupportsTriggerMode(triggerMode)) return triggerMode;
            return SupportsSemiAutomatic ? WeaponTriggerMode.SemiAutomatic : WeaponTriggerMode.Automatic;
        }

        public bool SupportsTriggerMode(WeaponTriggerMode mode)
        {
            return mode == WeaponTriggerMode.Automatic ? SupportsAutomatic : SupportsSemiAutomatic;
        }

        // 단발과 연사를 모두 지원할 때만 반대 모드로 전환하며, 한 모드 전용 총기는 현재 모드를 그대로 유지합니다.
        public WeaponTriggerMode GetNextTriggerMode(WeaponTriggerMode current)
        {
            WeaponTriggerMode safeCurrent = ClampTriggerMode(current);
            if (!SupportsSemiAutomatic || !SupportsAutomatic) return safeCurrent;
            return safeCurrent == WeaponTriggerMode.Automatic
                ? WeaponTriggerMode.SemiAutomatic
                : WeaponTriggerMode.Automatic;
        }

        // 내구도가 낮아질수록 명중 오차가 커지는 배율을 계산합니다.
        // normalizedDurability가 1이면 새 총이므로 1배, 0이면 설정한 최대 페널티를 반환합니다.
        public float GetDurabilityAccuracyMultiplier(float normalizedDurability)
            => Mathf.Lerp(wornAccuracySpreadMultiplier, 1f, Mathf.Clamp01(normalizedDurability));

        // 재장전 시간도 같은 방식으로 계산하되 명중률과 별도 값으로 두어 무기별 밸런스를 독립적으로 조절할 수 있습니다.
        public float GetDurabilityReloadMultiplier(float normalizedDurability)
            => Mathf.Lerp(wornReloadDurationMultiplier, 1f, Mathf.Clamp01(normalizedDurability));

        // 현재 내구도에서 최대 내구도까지 복구하는 데 필요한 크레딧을 올림 계산합니다.
        // 한 번이라도 손상됐는데 소수점 때문에 0원이 되는 일을 막고, 수리 단가가 0이면 무료 수리를 허용합니다.
        public int CalculateRepairCost(float currentDurability)
        {
            float missing = Mathf.Max(0f, MaxDurability - Mathf.Clamp(currentDurability, 0f, MaxDurability));
            return repairCostPerPoint <= 0 ? 0 : Mathf.CeilToInt(missing * RepairCostPerPoint);
        }

        // Inspector나 에디터 도구가 잘못된 음수 값을 기록해도 에셋 자체에는 안전한 범위만 남도록 보정합니다.
        // 런타임 프로퍼티도 같은 보정을 수행하므로 오래된 에셋을 읽는 순간에도 유효한 전투 수치가 보장됩니다.
        private void OnValidate()
        {
            damage = Mathf.Max(0.01f, damage);
            range = Mathf.Max(0.1f, range);
            fireInterval = Mathf.Max(0.01f, fireInterval);
            reloadDuration = Mathf.Max(0f, reloadDuration);
            magazineSize = Mathf.Max(1, magazineSize);
            pelletCount = Mathf.Max(1, pelletCount);
            spreadAngle = Mathf.Clamp(spreadAngle, 0f, 90f);
            accuracySpreadAngle = Mathf.Clamp(accuracySpreadAngle, 0f, 15f);
            knockbackForce = Mathf.Max(0f, knockbackForce);
            armorPenetration = Mathf.Clamp(armorPenetration, 0, 5);
            maxDurability = Mathf.Max(1f, maxDurability);
            durabilityLossPerShot = Mathf.Max(0f, durabilityLossPerShot);
            wornAccuracySpreadMultiplier = Mathf.Clamp(wornAccuracySpreadMultiplier, 1f, 4f);
            wornReloadDurationMultiplier = Mathf.Clamp(wornReloadDurationMultiplier, 1f, 3f);
            repairCostPerPoint = Mathf.Max(0, repairCostPerPoint);

            // 최소한 기본 발사 모드는 항상 지원하도록 자동 보정해 잘못된 Inspector 조합에서도 무기를 사용할 수 있게 합니다.
            if (triggerMode == WeaponTriggerMode.Automatic) supportsAutomatic = true;
            else supportsSemiAutomatic = true;
        }
    }
}
