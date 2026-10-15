using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Combat;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.UI
{
    // 선택한 무기의 부착물 세 슬롯, 실제 적용 능력치와 거점 수리 명령을 한곳에 표시하는 전용 Canvas 패널입니다.
    // 인벤토리 규칙을 직접 변경하지 않고, 소유 HUD가 전달한 해제·수리 콜백만 실행하므로 레이드와 거점에서 같은 View를 재사용합니다.
    public sealed class WeaponManagementPanel : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text titleText;
        [SerializeField] private Text statsText;
        [SerializeField] private Button opticButton;
        [SerializeField] private Button muzzleButton;
        [SerializeField] private Button magazineButton;
        [SerializeField] private Button repairButton;
        [SerializeField] private Text repairButtonText;

        private InventoryEntry boundWeapon;
        private Action<WeaponAttachmentSlot> detachRequested;
        private Action repairRequested;

        // 에디터 생성기가 만든 실제 Text와 Button을 직렬화 필드에 연결합니다.
        // 모든 자식이 일반 Canvas 오브젝트이므로 이후 사용자가 프리팹을 열어 위치·색·폰트를 코드 수정 없이 바꿀 수 있습니다.
        public void ConfigureViews(
            GameObject root,
            Text title,
            Text stats,
            Button optic,
            Button muzzle,
            Button magazine,
            Button repair,
            Text repairLabel)
        {
            panelRoot = root;
            titleText = title;
            statsText = stats;
            opticButton = optic;
            muzzleButton = muzzle;
            magazineButton = magazine;
            repairButton = repair;
            repairButtonText = repairLabel;
            WireButtons();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Awake()
        {
            WireButtons();
        }

        // 선택 무기와 비교 무기를 새로 연결하고 현재 인스턴스 상태를 즉시 화면에 반영합니다.
        // allowRepair가 false인 레이드 인벤토리에서는 수리 버튼 자체를 숨겨 거점 전용 기능으로 명확하게 구분합니다.
        public void Bind(
            InventoryEntry weapon,
            InventoryEntry comparison,
            bool allowRepair,
            Action<WeaponAttachmentSlot> onDetachRequested,
            Action onRepairRequested)
        {
            boundWeapon = weapon?.Item.Type == ItemType.Weapon && weapon.Item.WeaponDefinition != null
                ? weapon
                : null;
            detachRequested = onDetachRequested;
            repairRequested = onRepairRequested;

            if (panelRoot == null) panelRoot = gameObject;
            panelRoot.SetActive(boundWeapon != null);
            if (boundWeapon == null) return;

            WeaponDefinition definition = boundWeapon.Item.WeaponDefinition;
            if (titleText != null)
                titleText.text = $"무기 관리 · {boundWeapon.Item.DisplayName} · {ItemDetailFormatter.TriggerModeName(boundWeapon.SelectedTriggerMode)}";
            if (statsText != null) statsText.text = BuildStats(boundWeapon, comparison);

            RefreshAttachmentButton(opticButton, WeaponAttachmentSlot.Optic, "조준경");
            RefreshAttachmentButton(muzzleButton, WeaponAttachmentSlot.Muzzle, "총구");
            RefreshAttachmentButton(magazineButton, WeaponAttachmentSlot.Magazine, "탄창");

            if (repairButton != null)
            {
                repairButton.gameObject.SetActive(allowRepair);
                int repairCost = definition.CalculateRepairCost(boundWeapon.WeaponDurability);
                repairButton.interactable = allowRepair && boundWeapon.WeaponDurability < definition.MaxDurability;
                if (repairButtonText != null)
                    repairButtonText.text = repairCost > 0
                        ? $"완전 수리 · {repairCost} 크레딧"
                        : boundWeapon.WeaponDurability < definition.MaxDurability ? "완전 수리 · 무료" : "내구도 최대";
            }
        }

        private void WireButtons()
        {
            // ConfigureViews와 Awake 양쪽에서 호출될 수 있으므로 자기 리스너를 먼저 제거해 클릭 한 번이 중복 실행되지 않게 합니다.
            if (opticButton != null)
            {
                opticButton.onClick.RemoveListener(HandleOpticDetach);
                opticButton.onClick.AddListener(HandleOpticDetach);
            }
            if (muzzleButton != null)
            {
                muzzleButton.onClick.RemoveListener(HandleMuzzleDetach);
                muzzleButton.onClick.AddListener(HandleMuzzleDetach);
            }
            if (magazineButton != null)
            {
                magazineButton.onClick.RemoveListener(HandleMagazineDetach);
                magazineButton.onClick.AddListener(HandleMagazineDetach);
            }
            if (repairButton != null)
            {
                repairButton.onClick.RemoveListener(HandleRepair);
                repairButton.onClick.AddListener(HandleRepair);
            }
        }

        private void RefreshAttachmentButton(Button button, WeaponAttachmentSlot slot, string slotName)
        {
            if (button == null || boundWeapon == null) return;
            ItemDefinition installed = boundWeapon.GetAttachment(slot);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
                label.text = installed != null
                    ? $"{slotName}: {installed.DisplayName}\n클릭하여 해제"
                    : $"{slotName}: 비어 있음\n부착물을 무기로 드래그";
            button.interactable = installed != null && detachRequested != null;
        }

        private static string BuildStats(InventoryEntry weapon, InventoryEntry comparison)
        {
            WeaponDefinition definition = weapon.Item.WeaponDefinition;
            float damage = definition.PotentialDamagePerShot * weapon.AttachmentDamageMultiplier;
            float baseDamage = definition.PotentialDamagePerShot;
            float accuracy = definition.AccuracySpreadAngle * weapon.AttachmentAccuracyMultiplier *
                             definition.GetDurabilityAccuracyMultiplier(weapon.WeaponDurabilityNormalized);
            float baseAccuracy = definition.AccuracySpreadAngle *
                                 definition.GetDurabilityAccuracyMultiplier(weapon.WeaponDurabilityNormalized);
            float fireInterval = definition.FireInterval * weapon.AttachmentFireIntervalMultiplier;
            float baseFireInterval = definition.FireInterval;
            float reload = definition.ReloadDuration * weapon.AttachmentReloadMultiplier *
                           definition.GetDurabilityReloadMultiplier(weapon.WeaponDurabilityNormalized);
            float baseReload = definition.ReloadDuration *
                               definition.GetDurabilityReloadMultiplier(weapon.WeaponDurabilityNormalized);

            var text = new StringBuilder();
            text.AppendLine($"내구도 {weapon.WeaponDurability:0.#}/{definition.MaxDurability:0.#}");
            // 화살표 왼쪽은 부착물이 없는 현재 무기의 값, 오른쪽은 설치된 모든 부착물을 적용한 실제 값입니다.
            // 뒤의 대괄호는 같은 장비 슬롯의 비교 무기가 있을 때만 표시해 부착 전후와 무기 교체 비교를 동시에 제공합니다.
            text.AppendLine($"최대 피해 {baseDamage:0.#} → {damage:0.#}{Compare(damage, ComparisonValue(comparison, true), true)}");
            text.AppendLine($"명중 오차 {baseAccuracy:0.##} → {accuracy:0.##}도{Compare(accuracy, ComparisonValue(comparison, false), false)}");
            text.AppendLine($"발사 간격 {baseFireInterval:0.##} → {fireInterval:0.##}초{Compare(fireInterval, ComparisonInterval(comparison), false)}");
            text.Append($"재장전 {baseReload:0.##} → {reload:0.##}초{Compare(reload, ComparisonReload(comparison), false)}");
            return text.ToString();
        }

        private static float? ComparisonValue(InventoryEntry entry, bool damage)
        {
            WeaponDefinition definition = entry?.Item.WeaponDefinition;
            if (definition == null) return null;
            return damage
                ? definition.PotentialDamagePerShot * entry.AttachmentDamageMultiplier
                : definition.AccuracySpreadAngle * entry.AttachmentAccuracyMultiplier *
                  definition.GetDurabilityAccuracyMultiplier(entry.WeaponDurabilityNormalized);
        }

        private static float? ComparisonInterval(InventoryEntry entry)
        {
            WeaponDefinition definition = entry?.Item.WeaponDefinition;
            return definition != null
                ? definition.FireInterval * entry.AttachmentFireIntervalMultiplier
                : (float?)null;
        }

        private static float? ComparisonReload(InventoryEntry entry)
        {
            WeaponDefinition definition = entry?.Item.WeaponDefinition;
            return definition != null
                ? definition.ReloadDuration * entry.AttachmentReloadMultiplier *
                  definition.GetDurabilityReloadMultiplier(entry.WeaponDurabilityNormalized)
                : (float?)null;
        }

        // 비교 대상이 있을 때만 차이를 표시하며, 피해는 큰 값이 좋고 시간·오차는 작은 값이 좋다는 방향까지 함께 계산합니다.
        private static string Compare(float current, float? other, bool higherIsBetter)
        {
            if (!other.HasValue || Mathf.Abs(current - other.Value) < 0.001f) return string.Empty;
            float delta = current - other.Value;
            bool better = higherIsBetter ? delta > 0f : delta < 0f;
            return $"  [{(better ? "+" : "-")}{Mathf.Abs(delta):0.##}]";
        }

        private void HandleOpticDetach() => detachRequested?.Invoke(WeaponAttachmentSlot.Optic);
        private void HandleMuzzleDetach() => detachRequested?.Invoke(WeaponAttachmentSlot.Muzzle);
        private void HandleMagazineDetach() => detachRequested?.Invoke(WeaponAttachmentSlot.Magazine);
        private void HandleRepair() => repairRequested?.Invoke();
    }
}
