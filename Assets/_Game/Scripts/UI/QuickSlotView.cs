using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZombieSurvival.Art;
using ZombieSurvival.Items;

namespace ZombieSurvival.UI
{
    // 퀵슬롯 한 칸의 키 번호, 아이콘, 이름, 수량과 잠금 상태를 표시합니다.
    // 드롭 판정은 InventoryHud가 이 컴포넌트를 찾아 처리하고, 클릭 명령은 소유한 QuickSlotHud로 전달합니다.
    public sealed class QuickSlotView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [SerializeField] private Text keyText;
        [SerializeField] private Text nameText;
        [SerializeField] private Text countText;
        [SerializeField] private Color unlockedColor = new Color(0.11f, 0.15f, 0.16f, 0.96f);
        [SerializeField] private Color occupiedColor = new Color(0.2f, 0.31f, 0.27f, 0.98f);
        [SerializeField] private Color lockedColor = new Color(0.045f, 0.05f, 0.055f, 0.88f);

        private QuickSlotHud owner;

        public int SlotIndex { get; private set; }

        public void Configure(Image backgroundImage, Image iconImage, Text keyLabel, Text nameLabel, Text countLabel)
        {
            background = backgroundImage;
            icon = iconImage;
            keyText = keyLabel;
            nameText = nameLabel;
            countText = countLabel;
            if (icon != null) icon.raycastTarget = false;
        }

        public void Bind(
            int slotIndex,
            string keyLabel,
            string storedDisplayName,
            ItemDefinition currentItem,
            int count,
            bool unlocked,
            QuickSlotHud hud)
        {
            SlotIndex = slotIndex;
            owner = hud;
            bool assigned = !string.IsNullOrWhiteSpace(storedDisplayName);
            if (background != null) background.color = !unlocked ? lockedColor : assigned ? occupiedColor : unlockedColor;
            if (keyText != null) keyText.text = keyLabel;
            if (nameText != null) nameText.text = !unlocked ? "잠김" : assigned ? storedDisplayName : "비어 있음";
            if (countText != null) countText.text = unlocked && assigned ? $"×{Mathf.Max(0, count)}" : string.Empty;

            if (icon == null) return;
            Sprite generated = currentItem != null ? GeneratedArtResources.GetItemIcon(currentItem.ItemId) : null;
            icon.sprite = generated != null ? generated : currentItem?.Icon;
            icon.material = GeneratedArtResources.GetUiWhiteKeyMaterial();
            icon.preserveAspect = true;
            icon.gameObject.SetActive(unlocked && icon.sprite != null);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null) return;
            owner?.HandleSlotClick(SlotIndex, eventData.button);
        }
    }
}
