using UnityEngine;

namespace ZombieSurvival.Combat
{
    // 총기와 좀비 공격이 구체적인 Health 클래스에 의존하지 않도록 만든 피해 인터페이스입니다.
    // Health 외에도 파괴 가능한 상자나 방어막이 이 인터페이스를 구현하면 같은 공격 코드로 피해를 받을 수 있습니다.
    public interface IDamageable
    {
        // 공격 피해량과 명중 위치를 전달합니다.
        void TakeDamage(float amount, Vector3 hitPoint);
    }
}
