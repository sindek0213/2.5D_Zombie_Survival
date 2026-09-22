using System.Text;
using ZombieSurvival.Combat;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.UI
{
    // 인벤토리와 거점이 같은 아이템 상세 문구와 무기 비교 규칙을 사용하도록 문자열 생성을 한곳에 모았습니다.
    // UI는 완성된 문자열만 표시하므로 수치 항목을 추가해도 두 화면을 따로 수정할 필요가 없습니다.
    public static class ItemDetailFormatter
    {
        public static string Build(InventoryEntry entry, InventoryEntry comparisonWeapon = null)
        {
            if (entry == null) return "아이템을 선택하면 상세 정보가 표시됩니다.";

            ItemDefinition item = entry.Item;
            var text = new StringBuilder();
            text.AppendLine(item.DisplayName);
            text.AppendLine("등급: " + RarityName(item.Rarity));
            text.AppendLine($"수량 {entry.Quantity} · 크기 {entry.Width}×{entry.Height}");
            // 한 개 가치와 현재 묶음 전체 가치를 같이 보여 주면 파밍 중 무엇을 가져갈지 빠르게 비교할 수 있습니다.
            text.AppendLine($"기본 가치: {item.BaseValue} · 묶음 가치: {item.BaseValue * entry.Quantity}");

            switch (item.Type)
            {
                case ItemType.Ammo:
                    text.AppendLine("탄종: " + AmmoName(item.Caliber));
                    break;

                case ItemType.Healing:
                    text.AppendLine($"체력 회복: {item.HealthRestoreAmount:0.#}");
                    break;

                case ItemType.Food:
                    text.AppendLine($"포만감 회복: {item.SatietyRestoreAmount:0.#}");
                    break;

                case ItemType.Weapon:
                    AppendWeapon(text, entry, comparisonWeapon);
                    break;

                case ItemType.Armor:
                    text.AppendLine($"피해 감소: {item.ArmorDamageReduction * 100f:0}%");
                    text.AppendLine($"내구도: {entry.ArmorDurability:0.#} / {item.ArmorMaxDurability:0.#}");
                    if (entry.ArmorDurability <= 0f) text.AppendLine("파손됨: 피해 감소 효과 없음");
                    break;

                case ItemType.Container:
                    if (item.IsContainer) text.AppendLine($"내부 공간: {item.ContainerWidth}×{item.ContainerHeight}");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                text.AppendLine();
                text.Append(item.Description);
            }
            return text.ToString();
        }

        private static void AppendWeapon(StringBuilder text, InventoryEntry entry, InventoryEntry comparison)
        {
            WeaponDefinition weapon = entry.Item.WeaponDefinition;
            if (weapon == null)
            {
                text.AppendLine("무기 전투 데이터가 연결되지 않았습니다.");
                return;
            }

            text.AppendLine($"탄종: {AmmoName(weapon.Caliber)}");
            text.AppendLine($"탄창: {entry.MagazineRounds} / {weapon.MagazineSize}");
            text.AppendLine("발사 방식: " + (weapon.IsAutomatic ? "자동" : "반자동"));
            if (weapon.PelletCount > 1)
            {
                text.AppendLine($"산탄: {weapon.PelletCount}발 · 확산 {weapon.SpreadAngle:0.#}도");
                text.AppendLine($"펠릿당 피해: {weapon.Damage:0.#}");
            }
            // 산탄총과 일반 총기를 비교할 때 같은 기준을 사용하도록 한 번의 방아쇠 입력으로 가능한 최대 피해를 표시합니다.
            // 일반 총기는 펠릿이 하나라 기존 피해량과 같고, 산탄총은 모든 펠릿이 맞는 가까운 거리의 이론값입니다.
            text.AppendLine(
                $"발사당 최대 피해: {weapon.PotentialDamagePerShot:0.#}" +
                Difference(
                    weapon.PotentialDamagePerShot,
                    ComparisonValue(comparison, w => w.PotentialDamagePerShot),
                    true));
            text.AppendLine($"사거리: {weapon.Range:0.#}{Difference(weapon.Range, ComparisonValue(comparison, w => w.Range), true)}");
            text.AppendLine($"명중 오차: {weapon.AccuracySpreadAngle:0.#}도{Difference(weapon.AccuracySpreadAngle, ComparisonValue(comparison, w => w.AccuracySpreadAngle), false)}");
            text.AppendLine($"밀림: {weapon.KnockbackForce:0.##}{Difference(weapon.KnockbackForce, ComparisonValue(comparison, w => w.KnockbackForce), true)}");
            text.AppendLine($"발사 간격: {weapon.FireInterval:0.##}초{Difference(weapon.FireInterval, ComparisonValue(comparison, w => w.FireInterval), false)}");
            text.AppendLine($"재장전: {weapon.ReloadDuration:0.##}초{Difference(weapon.ReloadDuration, ComparisonValue(comparison, w => w.ReloadDuration), false)}");
        }

        private static float? ComparisonValue(InventoryEntry comparison, System.Func<WeaponDefinition, float> selector)
        {
            WeaponDefinition definition = comparison?.Item.WeaponDefinition;
            return definition != null ? selector(definition) : (float?)null;
        }

        // 피해량·사거리는 큰 값이 좋고 발사·재장전 간격은 작은 값이 좋으므로 부호 색인 대신 좋음/나쁨 문구를 붙입니다.
        private static string Difference(float current, float? compared, bool higherIsBetter)
        {
            if (!compared.HasValue || System.Math.Abs(current - compared.Value) < 0.001f) return string.Empty;
            float delta = current - compared.Value;
            bool better = higherIsBetter ? delta > 0f : delta < 0f;
            return $"  ({(better ? "좋음" : "나쁨")} {System.Math.Abs(delta):0.##})";
        }

        public static string AmmoName(AmmoType ammoType)
        {
            switch (ammoType)
            {
                case AmmoType.NineMillimeter: return "9mm";
                case AmmoType.FiveFiveSix: return "5.56mm";
                case AmmoType.TwelveGauge: return "12게이지";
                default: return "없음";
            }
        }

        // ItemRarity enum을 플레이어가 바로 이해할 수 있는 한글 표시 이름으로 변환합니다.
        public static string RarityName(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Uncommon: return "고급";
                case ItemRarity.Rare: return "희귀";
                case ItemRarity.Quest: return "퀘스트";
                default: return "일반";
            }
        }
    }
}
