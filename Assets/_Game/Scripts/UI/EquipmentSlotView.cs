using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZombieSurvival.Art;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.UI
{
    // 장비 슬롯 View가 레이드 인벤토리와 거점 인벤토리 중 어느 화면에 놓여도 같은 입력을 전달하기 위한 공용 계약입니다.
    // View가 특정 HUD 클래스에 직접 의존하지 않으므로 장비 슬롯 프리팹 하나를 두 화면에서 그대로 재사용할 수 있습니다.
    public interface IEquipmentSlotViewOwner
    {
        void HandleEquipmentClick(EquipmentSlotType slot, PointerEventData.InputButton button);
        void BeginEquipmentDrag(EquipmentSlotView view, EquipmentSlotType slot, PointerEventData eventData);
        void UpdateEquipmentDrag(PointerEventData eventData);
        void EndEquipmentDrag(PointerEventData eventData);
        void ShowItemDetailPopup(InventoryEntry entry, Vector2 screenPosition, Camera eventCamera);
        void HideItemDetailPopup(InventoryEntry entry);
    }

    // 주무기, 보조무기, 방탄복, 조끼와 가방 중 한 장비 슬롯을 표현하는 재사용 UI 프리팹입니다.
    // 슬롯 종류만 다르게 지정하면 같은 배경 이미지와 글자 디자인을 모든 장비 칸에 일관되게 사용할 수 있습니다.
    public sealed class EquipmentSlotView : MonoBehaviour,
        IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private EquipmentSlotType slot;
        [SerializeField] private Image background;
        // 장착된 아이템의 원화는 배경과 분리된 Image에 표시합니다.
        // 배경 색으로 현재 사용 무기를 강조해도 아이콘 고유 색상은 바뀌지 않습니다.
        [SerializeField] private Image iconImage;
        [SerializeField] private Text labelText;
        // 장비 상태색을 공용 팔레트에서 가져와 인벤토리와 거점 장비창이 같은 시각 규칙을 사용하게 합니다.
        [SerializeField] private Color normalColor = SurvivalUiTheme.EquipmentEmpty;
        [SerializeField] private Color occupiedColor = SurvivalUiTheme.EquipmentOccupied;
        [SerializeField] private Color activeWeaponColor = SurvivalUiTheme.EquipmentActive;

        private InventoryEntry entry;
        private IEquipmentSlotViewOwner owner;
        private Coroutine detailHoldRoutine;
        private bool detailPopupShown;
        private bool suppressNextClick;

        public EquipmentSlotType Slot => slot;

        private void Awake()
        {
            EnsureIconView();
        }

        // 생성기가 이 프리팹 인스턴스의 슬롯 종류와 Text 참조를 지정합니다.
        public void Configure(EquipmentSlotType slotType, Image backgroundImage, Text label)
        {
            slot = slotType;
            background = backgroundImage;
            labelText = label;
        }

        // 리소스 적용 도구가 기존 장비 슬롯 프리팹에 아이콘 Image를 추가한 뒤 참조를 연결합니다.
        public void SetIconView(Image image)
        {
            iconImage = image;
            if (iconImage != null) iconImage.raycastTarget = false;
        }

        public void Bind(InventoryEntry value, bool isActiveWeapon, IEquipmentSlotViewOwner hud)
        {
            entry = value;
            owner = hud;

            if (background != null)
                background.color = isActiveWeapon ? activeWeaponColor : entry != null ? occupiedColor : normalColor;

            if (iconImage != null)
            {
                // 인벤토리 칸과 같은 잘린 Sprite를 사용해야 장비 슬롯에서도 그림 크기가 동일하게 보입니다.
                // 생성 원화가 없는 아이템은 ScriptableObject에 직접 연결된 기존 아이콘을 그대로 사용합니다.
                Sprite croppedGeneratedIcon = entry != null
                    ? GeneratedArtResources.GetItemIcon(entry.Item.ItemId)
                    : null;
                iconImage.sprite = entry != null
                    ? croppedGeneratedIcon != null ? croppedGeneratedIcon : entry.Item.Icon
                    : null;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                iconImage.gameObject.SetActive(iconImage.sprite != null);
            }

            if (labelText == null) return;
            string activePrefix = isActiveWeapon ? "[사용 중] " : string.Empty;
            string providedSuffix = entry != null && entry.IsRaidProvided ? " (테스트 지급)" : string.Empty;
            string stateSuffix = entry != null && entry.HasArmorState
                ? $"  {entry.ArmorDurability:0}/{entry.Item.ArmorMaxDurability:0}"
                : entry != null && entry.HasMagazineState
                    ? $"  {entry.MagazineRounds}/{entry.EffectiveMagazineSize}"
                    : string.Empty;
            labelText.text = entry == null
                ? SlotName(slot) + "\n-"
                : activePrefix + SlotName(slot) + "\n" + entry.Item.DisplayName + stateSuffix + providedSuffix;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (suppressNextClick)
            {
                suppressNextClick = false;
                return;
            }
            if (entry != null) owner?.HandleEquipmentClick(slot, eventData.button);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || entry == null || owner == null) return;
            CancelDetailHold(true);
            suppressNextClick = false;
            detailHoldRoutine = StartCoroutine(WaitForDetailHold(eventData.position, eventData.pressEventCamera));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            // 장비 정보창도 일반 격자 아이템과 같이 손을 뗀 뒤 유지합니다.
            if (eventData.button == PointerEventData.InputButton.Left) CancelDetailHold(false);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // 열기 전 대기만 취소하고 이미 표시된 팝업은 바깥 클릭을 기다립니다.
            CancelDetailHold(false);
        }

        // 장비 슬롯에서 격자로 직접 끌어 해제할 수 있도록 왼쪽 버튼 드래그 시작을 InventoryHud에 전달합니다.
        // 실제 장비 Dictionary는 마우스를 놓고 목표 격자 검사가 성공할 때까지 변경하지 않습니다.
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || entry == null || owner == null) return;
            CancelDetailHold(true);
            owner.BeginEquipmentDrag(this, slot, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) owner?.UpdateEquipmentDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) owner?.EndEquipmentDrag(eventData);
        }

        private static string SlotName(EquipmentSlotType value)
        {
            switch (value)
            {
                case EquipmentSlotType.PrimaryWeapon: return "주무기";
                case EquipmentSlotType.SecondaryWeapon: return "보조무기";
                case EquipmentSlotType.Armor: return "방탄복";
                case EquipmentSlotType.Rig: return "전술 조끼";
                case EquipmentSlotType.Backpack: return "가방";
                default: return "장비";
            }
        }

        private IEnumerator WaitForDetailHold(Vector2 screenPosition, Camera eventCamera)
        {
            float elapsed = 0f;
            while (elapsed < InventoryItemView.DetailHoldDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            detailHoldRoutine = null;
            detailPopupShown = true;
            suppressNextClick = true;
            owner?.ShowItemDetailPopup(entry, screenPosition, eventCamera);
        }

        private void CancelDetailHold(bool hidePopup)
        {
            if (detailHoldRoutine != null)
            {
                StopCoroutine(detailHoldRoutine);
                detailHoldRoutine = null;
            }

            if (hidePopup && detailPopupShown)
            {
                owner?.HideItemDetailPopup(entry);
                detailPopupShown = false;
            }
        }

        private void EnsureIconView()
        {
            if (iconImage == null)
            {
                Transform existing = transform.Find("ItemIcon");
                if (existing != null) iconImage = existing.GetComponent<Image>();
            }

            if (iconImage == null)
            {
                // 기존 장비 슬롯 프리팹에도 원화가 보이도록 왼쪽에 62×56 크기의 Image를 자동으로 추가합니다.
                // 슬롯 루트의 배경 Image가 클릭을 받고 이 자식은 Raycast를 끄므로 장비 선택 조작은 이전과 같습니다.
                var iconObject = new GameObject("ItemIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconObject.transform.SetParent(transform, false);
                iconImage = iconObject.GetComponent<Image>();
                RectTransform iconRect = iconImage.rectTransform;
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(5f, 0f);
                iconRect.sizeDelta = new Vector2(62f, 56f);
            }

            iconImage.material = GeneratedArtResources.GetUiWhiteKeyMaterial();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            if (labelText != null)
            {
                RectTransform labelRect = labelText.rectTransform;
                labelRect.offsetMin = new Vector2(70f, 4f);
                labelRect.offsetMax = new Vector2(-6f, -4f);
                labelText.alignment = TextAnchor.MiddleLeft;
                labelText.transform.SetAsLastSibling();
            }
        }

        private void OnDisable()
        {
            CancelDetailHold(true);
        }
    }
}
