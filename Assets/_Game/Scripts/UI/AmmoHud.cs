using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Art;
using ZombieSurvival.Combat;
using ZombieSurvival.Core;
using ZombieSurvival.Items;
using ZombieSurvival.Player;

namespace ZombieSurvival.UI
{
    // 플레이 중 항상 보이는 현재 무기, 탄약, 체력과 기본 조작 안내를 Canvas Text로 표시합니다.
    // 전투와 체력 값을 직접 바꾸지 않고 PlayerCombat과 Health의 공개 상태만 읽으므로,
    // 프리팹의 글꼴·이미지·배치를 수정해도 실제 발사나 피해 계산에는 영향을 주지 않습니다.
    public sealed class AmmoHud : MonoBehaviour
    {
        [SerializeField] private PlayerCombat combat;
        [SerializeField] private Health playerHealth;
        [SerializeField] private PlayerSurvival playerSurvival;
        private ArmorProtection armorProtection;
        [SerializeField] private Text weaponText;
        [SerializeField] private Text ammoText;
        [SerializeField] private Text healthText;
        [SerializeField] private Text guideText;
        private RawImage generatedPortrait;

        private void Awake()
        {
            EnsureGeneratedPortrait();
        }

        // 씬 생성 코드가 플레이어의 전투 컴포넌트를 명시적으로 연결합니다.
        public void SetCombat(PlayerCombat value)
        {
            combat = value;
            Refresh();
        }

        // 체력 없는 테스트 씬에서도 재사용할 수 있도록 Health는 별도 설정 메서드로 연결합니다.
        public void SetPlayerHealth(Health value)
        {
            playerHealth = value;
            // 체력과 포만감은 같은 Player 루트에 있으므로 씬 생성기가 참조를 두 번 전달하지 않아도 함께 찾을 수 있습니다.
            playerSurvival = value != null ? value.GetComponent<PlayerSurvival>() : null;
            // 방어구 참조도 플레이어가 연결될 때 한 번만 찾아 매 프레임 GetComponent를 반복하지 않습니다.
            armorProtection = value != null ? value.GetComponent<ArmorProtection>() : null;
            Refresh();
        }

        // RaidHudCanvas 프리팹 생성기가 화면의 네 Text를 연결할 때 사용합니다.
        // 공개 설정 메서드를 사용하면 private SerializeField를 이름 문자열로 찾아 쓰지 않아 프리팹 구조 변경에 더 안전합니다.
        public void ConfigureViews(Text weapon, Text ammo, Text health, Text guide)
        {
            weaponText = weapon;
            ammoText = ammo;
            healthText = health;
            guideText = guide;
            EnsureGeneratedPortrait();
            Refresh();
        }

        private void EnsureGeneratedPortrait()
        {
            Texture2D portraitSheet = GeneratedArtResources.LoadTexture("Portraits/Portraits_MaskOn_Draft");
            if (portraitSheet == null) return;

            Transform existing = transform.Find("PlayerPortrait");
            if (existing != null) generatedPortrait = existing.GetComponent<RawImage>();
            if (generatedPortrait == null)
            {
                // 6×4 표정 시트 전체를 보여 주지 않고 RawImage의 UV 영역으로 첫 번째 정상 표정 한 칸만 잘라 HUD에 추가합니다.
                // 별도 PNG를 다시 만들지 않으므로 나중에 표정 전환 기능을 추가할 때 같은 시트를 그대로 사용할 수 있습니다.
                var portraitObject = new GameObject(
                    "PlayerPortrait",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage));
                portraitObject.transform.SetParent(transform, false);
                generatedPortrait = portraitObject.GetComponent<RawImage>();
            }

            RectTransform rect = generatedPortrait.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(22f, -12f);
            rect.sizeDelta = new Vector2(78f, 78f);
            generatedPortrait.texture = portraitSheet;
            generatedPortrait.material = GeneratedArtResources.GetUiWhiteKeyMaterial();
            generatedPortrait.color = Color.white;
            generatedPortrait.raycastTarget = false;
            // 첫 번째 256×256 초상화의 안쪽으로 4픽셀만 들어와 이웃 칸의 선이 섞이지 않게 자릅니다.
            generatedPortrait.uvRect = new Rect(4f / 1536f, 772f / 1024f, 248f / 1536f, 248f / 1024f);
            generatedPortrait.transform.SetAsFirstSibling();

            // 초상화와 조작 문구가 겹치지 않도록 기존 Text만 오른쪽으로 옮기며 다른 HUD 배치는 건드리지 않습니다.
            if (guideText != null)
            {
                guideText.rectTransform.anchoredPosition = new Vector2(112f, guideText.rectTransform.anchoredPosition.y);
                guideText.rectTransform.sizeDelta = new Vector2(1012f, guideText.rectTransform.sizeDelta.y);
            }
        }

        private void Update() => Refresh();

        private void Refresh()
        {
            if (guideText != null)
            {
                // 별도 Trigger를 여러 개 배치하지 않고 플레이어 좌표를 공용 맵 규칙에 전달해 현재 구역 이름을 계산합니다.
                string zone = playerHealth != null
                    ? RaidMapLayout.GetZoneDisplayName(playerHealth.transform.position)
                    : "위치 확인 중";
                // 새 플레이어도 별도 설명서를 열지 않고 정밀 조준 입력을 알 수 있도록 항상 보이는 기본 조작 줄에 우클릭을 표시합니다.
                guideText.text = $"[{zone}]  WASD 이동  |  Shift 달리기  |  좌클릭 발사  |  우클릭 조준  |  R 재장전  |  1·2 무기  |  Tab 인벤토리";
            }

            if (combat == null)
            {
                if (weaponText != null) weaponText.text = "[없음] 무기 없음";
                if (ammoText != null) ammoText.text = "- / -";
            }
            else
            {
                if (weaponText != null) weaponText.text = BuildWeaponText(combat.ActiveWeaponSlot, combat.CurrentWeaponName);
                if (ammoText != null)
                    ammoText.text = BuildAmmoText(
                        combat.Ammo != null ? combat.Ammo.InMagazine : -1,
                        combat.Ammo != null ? combat.Ammo.Reserve : -1,
                        combat.IsReloading);
            }

            if (healthText == null) return;
            healthText.gameObject.SetActive(playerHealth != null);
            if (playerHealth != null)
            {
                string armorText = armorProtection != null && armorProtection.MaximumDurability > 0f
                    ? $"  |  방탄 {armorProtection.CurrentDurability:0}/{armorProtection.MaximumDurability:0}"
                    : string.Empty;
                string satietyText = playerSurvival != null
                    ? "  |  " + BuildSatietyText(
                        playerSurvival.CurrentSatiety,
                        playerSurvival.MaximumSatiety,
                        playerSurvival.MovementSpeedMultiplier)
                    : string.Empty;
                string staminaText = playerSurvival != null
                    ? "  |  " + BuildStaminaText(
                        playerSurvival.CurrentStamina,
                        playerSurvival.MaximumStamina,
                        playerSurvival.IsSprinting)
                    : string.Empty;
                healthText.text = $"HP {Mathf.CeilToInt(playerHealth.CurrentHealth)} / {Mathf.CeilToInt(playerHealth.MaxHealth)}{armorText}{satietyText}{staminaText}";
            }
        }

        // 무기가 없으면 -/-를, 재장전 중이면 숫자 대신 명확한 상태 문구를 반환합니다.
        // 순수 함수로 분리해 Canvas나 GameObject 없이도 EditMode 테스트에서 표시 규칙을 확인할 수 있습니다.
        public static string BuildAmmoText(int magazine, int reserve, bool isReloading)
        {
            if (magazine < 0 || reserve < 0) return "- / -";
            return isReloading ? "재장전 중..." : $"{magazine} / {reserve}";
        }

        // 장비 슬롯을 사용자가 누르는 숫자와 같은 1·2 표기로 변환해 현재 무기 이름 앞에 붙입니다.
        public static string BuildWeaponText(EquipmentSlotType slot, string weaponName)
        {
            string slotLabel = slot == EquipmentSlotType.PrimaryWeapon
                ? "1"
                : slot == EquipmentSlotType.SecondaryWeapon ? "2" : "없음";
            return $"[{slotLabel}] {weaponName}";
        }

        // 포만감이 정상 구간이면 현재/최대 수치만, 부족해 실제 이동 페널티가 생기면 적용 속도 비율도 함께 보여 줍니다.
        // 순수 문자열 함수라 실제 Canvas 없이도 경계값 표시를 자동 테스트할 수 있습니다.
        public static string BuildSatietyText(float current, float maximum, float movementMultiplier)
        {
            int safeMaximum = Mathf.Max(1, Mathf.CeilToInt(maximum));
            int safeCurrent = Mathf.Clamp(Mathf.CeilToInt(current), 0, safeMaximum);
            float safeMultiplier = Mathf.Clamp01(movementMultiplier);
            string penalty = safeMultiplier < 0.999f
                ? $" (이동 {Mathf.RoundToInt(safeMultiplier * 100f)}%)"
                : string.Empty;
            return $"포만 {safeCurrent}/{safeMaximum}{penalty}";
        }

        // 달리는 동안에는 수치 뒤에 상태를 붙여 감소 원인을 알리고, 평소에는 화면 폭을 아끼기 위해 수치만 표시합니다.
        // 값은 HUD의 다른 생존 수치와 같이 올림 처리하되 최대치를 넘거나 0 아래로 보이지 않게 제한합니다.
        public static string BuildStaminaText(float current, float maximum, bool sprinting)
        {
            int safeMaximum = Mathf.Max(1, Mathf.CeilToInt(maximum));
            int safeCurrent = Mathf.Clamp(Mathf.CeilToInt(current), 0, safeMaximum);
            return $"기력 {safeCurrent}/{safeMaximum}{(sprinting ? " (달리기)" : string.Empty)}";
        }
    }
}
