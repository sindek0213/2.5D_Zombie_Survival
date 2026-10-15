using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Hideout;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.UI
{
    // 거점 상인의 상품과 창고 아이템을 각각 한 항목씩 보여 주는 프로토타입 거래 Canvas입니다.
    // 가격·공간·화폐 변경은 HideoutController와 HideoutTradeService가 담당하며 이 클래스는 선택과 버튼 표시만 관리합니다.
    public sealed class HideoutTraderHud : MonoBehaviour
    {
        [SerializeField] private HideoutController controller;
        [SerializeField] private HideoutWorldController worldController;
        [SerializeField] private Text creditsText;
        [SerializeField] private Text marketItemText;
        [SerializeField] private Text stashItemText;
        [SerializeField] private Text statusText;
        [SerializeField] private Button previousMarketButton;
        [SerializeField] private Button nextMarketButton;
        [SerializeField] private Button buyOneButton;
        [SerializeField] private Button buyStackButton;
        [SerializeField] private Button previousStashButton;
        [SerializeField] private Button nextStashButton;
        [SerializeField] private Button sellOneButton;
        [SerializeField] private Button sellStackButton;
        [SerializeField] private Button closeButton;

        private readonly List<ItemDefinition> marketItems = new List<ItemDefinition>();
        private readonly List<InventoryEntry> stashItems = new List<InventoryEntry>();
        private int marketIndex;
        private int stashIndex;
        private string pendingConfirmation = string.Empty;
        private float pendingConfirmationUntil;

        public void Configure(
            HideoutController hideout,
            HideoutWorldController world,
            Text credits,
            Text marketItem,
            Text stashItem,
            Text status,
            Button previousMarket,
            Button nextMarket,
            Button buyOne,
            Button buyStack,
            Button previousStash,
            Button nextStash,
            Button sellOne,
            Button sellStack,
            Button close)
        {
            controller = hideout;
            worldController = world;
            creditsText = credits;
            marketItemText = marketItem;
            stashItemText = stashItem;
            statusText = status;
            previousMarketButton = previousMarket;
            nextMarketButton = nextMarket;
            buyOneButton = buyOne;
            buyStackButton = buyStack;
            previousStashButton = previousStash;
            nextStashButton = nextStash;
            sellOneButton = sellOne;
            sellStackButton = sellStack;
            closeButton = close;
            ConfigureButtons();
            Refresh();
        }

        // 프리팹 안의 Text와 Button 참조는 그대로 유지하고, 씬마다 달라지는 컨트롤러 참조만 연결합니다.
        public void SetContext(HideoutController hideout, HideoutWorldController world)
        {
            Unsubscribe();
            controller = hideout;
            worldController = world;
            Subscribe();
            ConfigureButtons();
            Refresh();
        }

        private void Awake() => ConfigureButtons();

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (controller != null)
            {
                controller.StateChanged -= Refresh;
                controller.StateChanged += Refresh;
            }
            HideoutEconomyStateStore.CreditsChanged -= HandleCreditsChanged;
            HideoutEconomyStateStore.CreditsChanged += HandleCreditsChanged;
        }

        private void Unsubscribe()
        {
            if (controller != null) controller.StateChanged -= Refresh;
            HideoutEconomyStateStore.CreditsChanged -= HandleCreditsChanged;
        }

        private void HandleCreditsChanged(int _) => Refresh();

        private void ConfigureButtons()
        {
            Bind(previousMarketButton, SelectPreviousMarket);
            Bind(nextMarketButton, SelectNextMarket);
            Bind(buyOneButton, BuyOne);
            Bind(buyStackButton, BuyStack);
            Bind(previousStashButton, SelectPreviousStash);
            Bind(nextStashButton, SelectNextStash);
            Bind(sellOneButton, SellOne);
            Bind(sellStackButton, SellStack);
            Bind(closeButton, Close);
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private void SelectPreviousMarket()
        {
            RebuildLists();
            if (marketItems.Count > 0) marketIndex = (marketIndex - 1 + marketItems.Count) % marketItems.Count;
            Refresh();
        }

        private void SelectNextMarket()
        {
            RebuildLists();
            if (marketItems.Count > 0) marketIndex = (marketIndex + 1) % marketItems.Count;
            Refresh();
        }

        private void SelectPreviousStash()
        {
            RebuildLists();
            if (stashItems.Count > 0) stashIndex = (stashIndex - 1 + stashItems.Count) % stashItems.Count;
            Refresh();
        }

        private void SelectNextStash()
        {
            RebuildLists();
            if (stashItems.Count > 0) stashIndex = (stashIndex + 1) % stashItems.Count;
            Refresh();
        }

        private void BuyOne() => TryBuy(1);

        private void BuyStack()
        {
            ItemDefinition item = SelectedMarketItem;
            TryBuy(item != null ? item.MaxStack : 0);
        }

        private void TryBuy(int quantity)
        {
            ItemDefinition item = SelectedMarketItem;
            if (controller == null || item == null)
            {
                SetStatus("선택된 상품이 없습니다.");
                return;
            }
            string operation = $"buy:{item.ItemId}:{quantity}";
            if (!ConfirmOperation(operation, $"{item.DisplayName} x{quantity} 구매")) return;
            controller.TryBuy(item, quantity);
            SetStatus(controller.OperationStatus);
            Refresh(false);
        }

        private void SellOne() => TrySell(1);

        private void SellStack()
        {
            InventoryEntry entry = SelectedStashEntry;
            TrySell(entry != null ? entry.Quantity : 0);
        }

        private void TrySell(int quantity)
        {
            InventoryEntry entry = SelectedStashEntry;
            if (controller == null || entry == null)
            {
                SetStatus("창고에 판매할 수 있는 아이템이 없습니다.");
                return;
            }
            string operation = $"sell:{entry.Item.ItemId}:{quantity}:{entry.GetHashCode()}";
            if (!ConfirmOperation(operation, $"{entry.Item.DisplayName} x{quantity} 판매")) return;
            controller.TrySell(controller.StashGrid, entry, quantity);
            SetStatus(controller.OperationStatus);
            Refresh(false);
        }

        private void Close() => worldController?.CloseFacility();

        private ItemDefinition SelectedMarketItem
            => marketItems.Count == 0 ? null : marketItems[Mathf.Clamp(marketIndex, 0, marketItems.Count - 1)];

        private InventoryEntry SelectedStashEntry
            => stashItems.Count == 0 ? null : stashItems[Mathf.Clamp(stashIndex, 0, stashItems.Count - 1)];

        private void RebuildLists()
        {
            marketItems.Clear();
            if (controller?.MarketItems != null)
                foreach (ItemDefinition item in controller.MarketItems)
                    if (HideoutTradeService.IsStockAvailable(
                            item,
                            HideoutEconomyStateStore.TraderLevel,
                            HideoutEconomyStateStore.StockRefreshIndex)) marketItems.Add(item);

            stashItems.Clear();
            if (controller?.StashGrid != null)
                foreach (InventoryEntry entry in controller.StashGrid.Entries)
                    if (entry != null && HideoutTradeService.IsTradable(entry.Item)) stashItems.Add(entry);

            marketIndex = marketItems.Count == 0 ? 0 : Mathf.Clamp(marketIndex, 0, marketItems.Count - 1);
            stashIndex = stashItems.Count == 0 ? 0 : Mathf.Clamp(stashIndex, 0, stashItems.Count - 1);
        }

        private void Refresh() => Refresh(true);

        private void Refresh(bool useControllerStatus)
        {
            RebuildLists();
            int discount = controller != null ? controller.GetTraderDiscountPercent() : 0;
            int sellBonus = controller != null ? controller.GetTraderSellBonusPercent() : 0;
            if (creditsText != null)
                creditsText.text = $"보유 크레딧: {HideoutEconomyStateStore.Credits:N0}  · 상인 Lv.{HideoutEconomyStateStore.TraderLevel} / 평판 {HideoutEconomyStateStore.TraderReputation}  · 구매 할인 {discount}% · 판매 보너스 {sellBonus}%";

            ItemDefinition market = SelectedMarketItem;
            if (marketItemText != null)
                marketItemText.text = market == null
                    ? "판매 중인 상품이 없습니다."
                    : $"상품 {marketIndex + 1}/{marketItems.Count}\n{market.DisplayName}\n" +
                      $"1개 {HideoutTradeService.CalculateBuyUnitPrice(market, discount):N0} 크레딧 · 묶음 최대 {market.MaxStack}\n{market.Description}";

            InventoryEntry stash = SelectedStashEntry;
            if (stashItemText != null)
                stashItemText.text = stash == null
                    ? "창고에 판매 가능한 아이템이 없습니다."
                    : $"창고 {stashIndex + 1}/{stashItems.Count}\n{stash.Item.DisplayName} x{stash.Quantity}\n" +
                      $"1개 {HideoutTradeService.CalculateSellUnitPrice(stash, sellBonus):N0} 크레딧" +
                      (stash.Contents != null && stash.Contents.Entries.Count > 0 ? "\n내용물이 있어 판매할 수 없습니다." : string.Empty);

            if (useControllerStatus && statusText != null)
                statusText.text = controller != null && !string.IsNullOrWhiteSpace(controller.OperationStatus)
                    ? controller.OperationStatus
                    : "구매품은 창고로 들어가며, 판매는 창고의 선택 묶음에서 처리됩니다.";

            bool hasMarket = market != null;
            bool hasStash = stash != null;
            if (previousMarketButton != null) previousMarketButton.interactable = marketItems.Count > 1;
            if (nextMarketButton != null) nextMarketButton.interactable = marketItems.Count > 1;
            if (buyOneButton != null) buyOneButton.interactable = hasMarket;
            if (buyStackButton != null) buyStackButton.interactable = hasMarket;
            if (previousStashButton != null) previousStashButton.interactable = stashItems.Count > 1;
            if (nextStashButton != null) nextStashButton.interactable = stashItems.Count > 1;
            if (sellOneButton != null) sellOneButton.interactable = hasStash;
            if (sellStackButton != null) sellStackButton.interactable = hasStash;
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = string.IsNullOrWhiteSpace(message) ? "거래 결과가 없습니다." : message;
        }

        // 실수로 고가 장비를 한 번 눌러 바로 팔지 않도록 같은 버튼을 3초 안에 한 번 더 눌러야 거래가 실행됩니다.
        private bool ConfirmOperation(string operationId, string description)
        {
            if (pendingConfirmation == operationId && Time.unscaledTime <= pendingConfirmationUntil)
            {
                pendingConfirmation = string.Empty;
                return true;
            }

            pendingConfirmation = operationId;
            pendingConfirmationUntil = Time.unscaledTime + 3f;
            SetStatus($"확인: {description}을(를) 실행하려면 같은 버튼을 다시 누르세요.");
            return false;
        }
    }
}
