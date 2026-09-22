using System;

namespace ZombieSurvival.Combat
{
    // HUD와 발사 판정이 읽는 현재 탄창 크기, 장전된 탄 수와 인벤토리 예비탄 수를 한곳에 보여 주는 순수 C# 상태 객체입니다.
    // 실제 영구 탄창 수량은 InventoryEntry가 소유하고, Reserve는 PlayerInventory의 같은 탄종 수량과 동기화됩니다.
    [Serializable]
    public sealed class AmmunitionState
    {
        public int MagazineSize { get; private set; }
        public int InMagazine { get; private set; }
        public int Reserve { get; private set; }
        public int MissingRounds => Math.Max(0, MagazineSize - InMagazine);

        public AmmunitionState(int magazineSize, int loaded, int reserve)
        {
            MagazineSize = Math.Max(1, magazineSize);
            InMagazine = Math.Clamp(loaded, 0, MagazineSize);
            Reserve = Math.Max(0, reserve);
        }

        // 탄창에 총알이 있을 때 한 발을 소비합니다.
        public bool TryConsumeRound()
        {
            if (InMagazine <= 0) return false;
            InMagazine--;
            return true;
        }

        // 외부 인벤토리에서 실제로 제거한 탄약 중 탄창의 빈자리만큼만 장전하고 실제 장전량을 반환합니다.
        // PlayerCombat은 먼저 필요한 수량을 계산해 인벤토리에서 정확히 제거하므로 정상 흐름에서는 전달값 전체가 들어갑니다.
        public int LoadRounds(int availableRounds)
        {
            int transferred = Math.Min(MissingRounds, Math.Max(0, availableRounds));
            InMagazine += transferred;
            return transferred;
        }

        // 무기를 선택하거나 인벤토리 탄약 수량이 바뀌었을 때 표시 상태를 실제 데이터와 다시 맞춥니다.
        public void Synchronize(int loadedRounds, int reserveRounds)
        {
            InMagazine = Math.Clamp(loadedRounds, 0, MagazineSize);
            Reserve = Math.Max(0, reserveRounds);
        }

        // 탄창의 빈 칸 required와 현재 Reserve 중 더 작은 값만 이동합니다.
        // 예를 들어 8발 탄창에 3발이 있고 예비탄이 2발이면 2발만 이동해 5/0 상태가 됩니다.
        // 반환값은 실제 이동량이라 재장전 애니메이션이나 효과 표시에도 사용할 수 있습니다.
        public int Reload()
        {
            int transferred = LoadRounds(Reserve);
            Reserve -= transferred;
            return transferred;
        }
    }
}
