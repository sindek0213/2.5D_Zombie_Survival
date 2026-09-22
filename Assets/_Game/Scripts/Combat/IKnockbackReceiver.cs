using UnityEngine;

namespace ZombieSurvival.Combat
{
    // 총알이나 폭발에 밀려날 수 있는 대상이 구현하는 공통 약속입니다.
    // PlayerCombat은 대상이 좀비인지 다른 적인지 몰라도 이 인터페이스만 확인해 밀림 속도를 전달할 수 있습니다.
    public interface IKnockbackReceiver
    {
        // velocity는 한 프레임 이동 거리가 아니라 즉시 추가할 월드 기준 속도이며, 대상이 이후 서서히 감속시킵니다.
        void ApplyKnockback(Vector3 velocity);
    }
}
