using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZombieSurvival.Combat;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;
using ZombieSurvival.Player;

namespace ZombieSurvival.UI
{
    // 인벤토리와 필드가 같은 열 칸짜리 퀵슬롯 상태를 서로 다른 Canvas에서 표시하도록 만든 공용 HUD입니다.
    // 실제 등록·사용·잠금 계산은 PlayerQuickSlots가 담당하고 이 컴포넌트는 자식 QuickSlotView를 갱신합니다.
    public sealed class QuickSlotHud : MonoBehaviour
    {
        [SerializeField] private QuickSlotView[] slotViews = new QuickSlotView[0];
        // 이전 네 칸 텍스트 프리팹을 읽을 수 있도록 남겨 둔 호환 필드이며 새 프리팹에서는 개별 SlotView를 우선 사용합니다.
        [SerializeField] private Text slotsText;
        [SerializeField] private Text effectsText;
        [SerializeField] private Text messageText;
        [SerializeField] private bool allowUseClick;
        [SerializeField] private bool allowClearClick;

        private PlayerQuickSlots quickSlots;
        private PlayerStatusEffects statusEffects;

        // 새 퀵슬롯 바는 열 개의 실제 드롭 대상과 클릭 허용 범위를 함께 연결합니다.
        public void ConfigureBar(
            QuickSlotView[] views,
            Text effectLabels,
            Text messageLabel,
            bool useOnLeftClick,
            bool clearOnRightClick)
        {
            slotViews = views ?? new QuickSlotView[0];
            effectsText = effectLabels;
            messageText = messageLabel;
            allowUseClick = useOnLeftClick;
            allowClearClick = clearOnRightClick;
            if (slotsText != null) slotsText.gameObject.SetActive(slotViews.Length == 0);
            RefreshImmediate();
        }

        // 이전 생성기와 사용자 프리팹이 컴파일·표시를 유지하도록 제공하는 텍스트 전용 호환 설정입니다.
        public void ConfigureViews(Text slotLabels, Text effectLabels, Text messageLabel)
        {
            slotsText = slotLabels;
            effectsText = effectLabels;
            messageText = messageLabel;
            RefreshImmediate();
        }

        private void OnEnable()
        {
            ResolvePlayer();
            Subscribe();
            RefreshImmediate();
        }

        private void OnDisable() => Unsubscribe();

        private void Update()
        {
            if (quickSlots == null || statusEffects == null)
            {
                ResolvePlayer();
                Subscribe();
                RefreshSlots();
            }
            // 효과 남은 시간과 메시지 만료는 매 프레임 달라지지만 슬롯 전체 오브젝트를 다시 만들지는 않습니다.
            RefreshEffects();
            RefreshMessage();
        }

        private void ResolvePlayer()
        {
            if (quickSlots == null) quickSlots = FindObjectOfType<PlayerQuickSlots>();
            if (statusEffects == null && quickSlots != null)
                statusEffects = quickSlots.GetComponent<PlayerStatusEffects>();
        }

        private void Subscribe()
        {
            if (quickSlots != null)
            {
                quickSlots.SlotsChanged -= RefreshSlots;
                quickSlots.SlotsChanged += RefreshSlots;
                quickSlots.MessageChanged -= RefreshMessage;
                quickSlots.MessageChanged += RefreshMessage;
            }
            if (statusEffects != null)
            {
                statusEffects.EffectsChanged -= RefreshEffects;
                statusEffects.EffectsChanged += RefreshEffects;
            }
        }

        private void Unsubscribe()
        {
            if (quickSlots != null)
            {
                quickSlots.SlotsChanged -= RefreshSlots;
                quickSlots.MessageChanged -= RefreshMessage;
            }
            if (statusEffects != null) statusEffects.EffectsChanged -= RefreshEffects;
        }

        public void HandleSlotClick(int slotIndex, PointerEventData.InputButton button)
        {
            if (quickSlots == null) ResolvePlayer();
            if (quickSlots == null) return;
            if (button == PointerEventData.InputButton.Left && allowUseClick)
                quickSlots.TryUseSlotAndReport(slotIndex);
            else if (button == PointerEventData.InputButton.Right && allowClearClick)
                quickSlots.TryClearSlot(slotIndex, out _);
        }

        private void RefreshImmediate()
        {
            RefreshSlots();
            RefreshEffects();
            RefreshMessage();
        }

        private void RefreshSlots()
        {
            if (slotViews != null && slotViews.Length > 0)
            {
                for (int index = 0; index < slotViews.Length; index++)
                {
                    QuickSlotView view = slotViews[index];
                    if (view == null) continue;
                    string name = QuickSlotAssignmentStore.GetDisplayName(index);
                    ItemDefinition item = null;
                    int count = 0;
                    if (quickSlots != null) quickSlots.TryGetAssignedItem(index, out item, out count);
                    view.Bind(
                        index,
                        PlayerQuickSlots.GetSlotKeyLabel(index),
                        name,
                        item,
                        count,
                        quickSlots != null && quickSlots.IsSlotUnlocked(index),
                        this);
                }
                if (slotsText != null) slotsText.gameObject.SetActive(false);
                return;
            }

            if (slotsText == null) return;
            if (quickSlots == null)
            {
                slotsText.text = "[1] -   [2] -";
                return;
            }
            var text = new StringBuilder();
            for (int index = 0; index < QuickSlotAssignmentStore.SlotCount; index++)
            {
                if (index > 0) text.Append("   ");
                text.Append(quickSlots.BuildSlotLabel(index));
            }
            slotsText.text = text.ToString();
        }

        private void RefreshEffects()
        {
            if (effectsText == null) return;
            if (statusEffects == null || statusEffects.ActiveEffects.Count == 0)
            {
                effectsText.text = string.Empty;
                return;
            }

            var text = new StringBuilder();
            foreach (ActiveStatusEffect active in statusEffects.ActiveEffects)
            {
                if (active?.Definition == null) continue;
                if (text.Length > 0) text.Append("   ");
                string marker = active.Definition.Disposition == StatusEffectDisposition.Buff ? "▲" : "▼";
                text.Append(marker)
                    .Append(' ')
                    .Append(active.Definition.DisplayName)
                    .Append(' ')
                    .Append(Mathf.CeilToInt(active.RemainingSeconds(Time.time)))
                    .Append('초');
            }
            effectsText.text = text.ToString();
        }

        private void RefreshMessage()
        {
            if (messageText == null) return;
            PlayerInventory inventory = quickSlots != null ? quickSlots.GetComponent<PlayerInventory>() : null;
            string inventoryMessage = inventory != null && inventory.HasVisibleItemUseMessage
                ? inventory.LastItemUseMessage
                : string.Empty;
            if (quickSlots != null && quickSlots.HasVisibleMessage)
                messageText.text = quickSlots.LastMessage;
            else if (!string.IsNullOrEmpty(inventoryMessage))
                messageText.text = inventoryMessage;
            else
                messageText.text = string.Empty;
        }
    }
}
