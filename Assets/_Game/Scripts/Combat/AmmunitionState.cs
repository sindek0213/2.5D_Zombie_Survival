using System;

namespace ZombieSurvival.Combat
{
    // 총기의 탄창 크기, 장전된 탄 수와 예비탄 수를 보관하는 순수 C# 상태 객체입니다.
    // MonoBehaviour가 아니므로 씬이나 GameObject 없이 생성해 테스트할 수 있고, 이후 저장 데이터로 직렬화하기 쉽습니다.
    [Serializable]
    public sealed class AmmunitionState
    {
        public int MagazineSize { get; private set; }
        public int InMagazine { get; private set; }
        public int Reserve { get; private set; }

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

        // 탄창의 빈 칸 required와 현재 Reserve 중 더 작은 값만 이동합니다.
        // 예를 들어 8발 탄창에 3발이 있고 예비탄이 2발이면 2발만 이동해 5/0 상태가 됩니다.
        // 반환값은 실제 이동량이라 재장전 애니메이션이나 효과 표시에도 사용할 수 있습니다.
        public int Reload()
        {
            int required = MagazineSize - InMagazine;
            int transferred = Math.Min(required, Reserve);
            InMagazine += transferred;
            Reserve -= transferred;
            return transferred;
        }

        // 루팅한 탄약을 예비탄에 추가합니다.
        public void AddReserve(int quantity) => Reserve += Math.Max(0, quantity);
    }
}
