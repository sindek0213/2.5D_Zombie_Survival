using UnityEngine;

namespace ZombieSurvival.Combat
{
    // 총격 명중 위치에 따른 피해 배율을 구분합니다.
    // 플레이어 부위별 체력 시스템과는 무관하며, 현재는 적 머리 사격 같은 전투 판정에만 사용합니다.
    public enum BodyPart
    {
        Head,
        Torso,
        Arms,
        Legs
    }

    // 탄약 관통력과 비교하는 간단한 방어 등급입니다. 숫자가 높을수록 낮은 관통 탄환의 피해 감소 효과를 더 온전히 유지합니다.
    public enum ArmorClass
    {
        None = 0,
        Soft = 1,
        Tactical = 2,
        Heavy = 3
    }

    // 마지막 피해가 어떤 행동에서 시작됐는지 구분해 사망 화면에 단순히 "플레이어 사망"보다 구체적인 원인을 표시합니다.
    // 기존 코드는 생성자 마지막 인수를 생략하면 Unknown을 사용하므로 새 분류를 추가해도 이전 피해 호출은 그대로 동작합니다.
    public enum DamageSource
    {
        Unknown,
        Gunfire,
        Melee,
        ZombieAttack,
        Bleeding
    }

    // 총알 관통력, 상태 이상 확률과 신체 부위를 한 번의 피해 요청으로 전달합니다.
    // 기존 근접 공격과 테스트는 단순 TakeDamage를 계속 사용할 수 있도록 별도 상세 인터페이스로 확장했습니다.
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly Vector3 HitPoint;
        public readonly BodyPart BodyPart;
        public readonly int ArmorPenetration;
        public readonly float BleedingChance;
        public readonly float FractureChance;
        public readonly bool IsStatusDamage;
        public readonly DamageSource Source;
        // 총기 처치 임무와 결과 통계가 어떤 무기 인스턴스에서 시작된 피해인지 구분하는 안정적인 ItemId입니다.
        // 좀비 공격이나 구버전 호출처럼 출처가 필요 없는 경우에는 빈 문자열을 사용합니다.
        public readonly string SourceId;

        public DamageInfo(
            float amount,
            Vector3 hitPoint,
            BodyPart bodyPart = BodyPart.Torso,
            int armorPenetration = 0,
            float bleedingChance = 0f,
            float fractureChance = 0f,
            bool isStatusDamage = false,
            DamageSource source = DamageSource.Unknown,
            string sourceId = null)
        {
            Amount = Mathf.Max(0f, amount);
            HitPoint = hitPoint;
            BodyPart = bodyPart;
            ArmorPenetration = Mathf.Max(0, armorPenetration);
            BleedingChance = Mathf.Clamp01(bleedingChance);
            FractureChance = Mathf.Clamp01(fractureChance);
            IsStatusDamage = isStatusDamage;
            Source = source;
            SourceId = sourceId ?? string.Empty;
        }

        // 방어구와 신체 배율이 피해량만 조정하더라도 공격 종류와 상태 이상 정보는 잃지 않도록 모두 복사합니다.
        public DamageInfo WithAmount(float amount)
            => new DamageInfo(
                amount,
                HitPoint,
                BodyPart,
                ArmorPenetration,
                BleedingChance,
                FractureChance,
                IsStatusDamage,
                Source,
                SourceId);
    }

    // 총기와 좀비 공격이 구체적인 Health 클래스에 의존하지 않도록 만든 피해 인터페이스입니다.
    // Health 외에도 파괴 가능한 상자나 방어막이 이 인터페이스를 구현하면 같은 공격 코드로 피해를 받을 수 있습니다.
    public interface IDamageable
    {
        // 공격 피해량과 명중 위치를 전달합니다.
        void TakeDamage(float amount, Vector3 hitPoint);
    }

    public interface IDetailedDamageable
    {
        void TakeDamage(DamageInfo damageInfo);
    }

    // Health가 체력을 줄이기 직전에 방어구나 보호막이 들어오는 피해를 조정할 수 있게 하는 선택형 인터페이스입니다.
    // Health는 구체적인 방탄복 클래스를 알 필요가 없고, 이 약속을 구현한 컴포넌트가 없으면 원래 피해를 그대로 적용합니다.
    public interface IIncomingDamageModifier
    {
        // incomingDamage를 검사하고 실제 체력에 적용할 최종 피해를 반환합니다.
        float ModifyIncomingDamage(float incomingDamage, Vector3 hitPoint);
    }

    public interface IDetailedIncomingDamageModifier
    {
        float ModifyIncomingDamage(DamageInfo damageInfo);
    }
}
