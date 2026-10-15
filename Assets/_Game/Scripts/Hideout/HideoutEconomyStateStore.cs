using System;
using UnityEngine;

namespace ZombieSurvival.Hideout
{
    // 플레이어가 거점에서 사용하는 공용 화폐를 씬 전환 사이에 보관합니다.
    // 실제 JSON 저장과 불러오기는 HideoutSaveService가 담당하며, 이 클래스는 음수 잔액이 생기지 않도록 숫자 변경 규칙만 책임집니다.
    public static class HideoutEconomyStateStore
    {
        // 새 게임에서도 권총 탄약, 음식과 치료품을 조금 살 수 있도록 프로토타입 시작 자금을 제공합니다.
        // 이전 버전 저장 파일에는 화폐 필드가 없으므로 불러올 때도 이 값을 기본값으로 사용합니다.
        public const int DefaultCredits = 1200;

        public static int Credits { get; private set; } = DefaultCredits;
        public static int TraderReputation { get; private set; }
        public static int StockRefreshIndex { get; private set; }
        public static int TraderLevel => TraderReputation >= 140 ? 3 : TraderReputation >= 50 ? 2 : 1;

        // 상인 HUD가 잔액을 즉시 다시 그릴 수 있도록 실제 값이 달라졌을 때만 알림을 보냅니다.
        public static event Action<int> CreditsChanged;
        public static event Action EconomyChanged;

        // 가격 전체를 지불할 수 있을 때만 잔액을 줄입니다.
        // 먼저 충분한지 확인하므로 돈이 부족한 구매가 일부 금액만 차감되는 상황은 생기지 않습니다.
        public static bool TrySpend(int amount)
        {
            if (amount <= 0 || Credits < amount) return false;
            Credits -= amount;
            CreditsChanged?.Invoke(Credits);
            EconomyChanged?.Invoke();
            return true;
        }

        // 판매 보상이나 임무 보상을 더할 때 int 범위를 넘으면 최대값에서 멈춰 음수로 뒤집히지 않게 합니다.
        public static void AddCredits(int amount)
        {
            if (amount <= 0) return;
            long result = (long)Credits + amount;
            Credits = (int)Math.Min(int.MaxValue, result);
            CreditsChanged?.Invoke(Credits);
            EconomyChanged?.Invoke();
        }

        // 임무 완료로만 평판을 올려 거래 반복만으로 상인 레벨을 무한히 올릴 수 없게 합니다.
        public static void AddTraderReputation(int amount)
        {
            if (amount <= 0) return;
            TraderReputation = (int)Math.Min(int.MaxValue, (long)TraderReputation + amount);
            EconomyChanged?.Invoke();
        }

        // 레이드가 끝날 때 재고 회차를 한 번만 올립니다. 같은 회차에서는 상품 목록이 화면을 닫아도 바뀌지 않습니다.
        public static void AdvanceStockRotation()
        {
            StockRefreshIndex = StockRefreshIndex == int.MaxValue ? 0 : StockRefreshIndex + 1;
            EconomyChanged?.Invoke();
        }

        // 저장 파일 검증이 끝난 뒤에만 호출됩니다. 음수는 손상 데이터이므로 저장 서비스가 이 메서드에 전달하지 않습니다.
        internal static void Restore(int savedCredits, int savedReputation = 0, int savedStockRefreshIndex = 0)
        {
            Credits = Mathf.Max(0, savedCredits);
            TraderReputation = Mathf.Max(0, savedReputation);
            StockRefreshIndex = Mathf.Max(0, savedStockRefreshIndex);
            CreditsChanged?.Invoke(Credits);
            EconomyChanged?.Invoke();
        }

        // EditMode 테스트와 새 Play 세션이 이전 실행의 잔액을 이어받지 않게 기본 시작 자금으로 되돌립니다.
        public static void ResetForTests()
        {
            Credits = DefaultCredits;
            TraderReputation = 0;
            StockRefreshIndex = 0;
            CreditsChanged?.Invoke(Credits);
            EconomyChanged?.Invoke();
        }
    }
}
