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

    // Health가 체력을 줄이기 직전에 방어구나 보호막이 들어오는 피해를 조정할 수 있게 하는 선택형 인터페이스입니다.
    // Health는 구체적인 방탄복 클래스를 알 필요가 없고, 이 약속을 구현한 컴포넌트가 없으면 원래 피해를 그대로 적용합니다.
    public interface IIncomingDamageModifier
    {
        // incomingDamage를 검사하고 실제 체력에 적용할 최종 피해를 반환합니다.
        float ModifyIncomingDamage(float incomingDamage, Vector3 hitPoint);
    }
}
