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
                    if (item.HealthRestoreAmount > 0f) text.AppendLine($"체력 회복: {item.HealthRestoreAmount:0.#}");
                    if (item.TreatsBleeding) text.AppendLine("치료 효과: 출혈 제거");
                    if (item.TreatsFracture) text.AppendLine("치료 효과: 골절 제거");
                    break;

                case ItemType.Food:
                    text.AppendLine($"포만감 회복: {item.SatietyRestoreAmount:0.#}");
                    break;

                case ItemType.Weapon:
                    AppendWeapon(text, entry, comparisonWeapon);
                    break;

                case ItemType.Armor:
                    text.AppendLine($"피해 감소: {item.ArmorDamageReduction * 100f:0}%");
                    text.AppendLine($"방어 등급: {(int)item.ArmorClass} ({ArmorClassName(item.ArmorClass)})");
                    text.AppendLine($"내구도: {entry.ArmorDurability:0.#} / {item.ArmorMaxDurability:0.#}");
                    text.AppendLine($"추가 퀵슬롯: +{item.QuickSlotBonus}칸");
                    if (entry.ArmorDurability <= 0f) text.AppendLine("파손됨: 피해 감소 효과 없음");
                    break;

                case ItemType.Container:
                    if (item.IsContainer) text.AppendLine($"내부 공간: {item.ContainerWidth}×{item.ContainerHeight}");
                    break;

                case ItemType.Attachment:
                    AppendAttachment(text, item.WeaponAttachmentDefinition);
                    break;

                case ItemType.BuffConsumable:
                    AppendStatusEffect(text, item.StatusEffectDefinition);
                    text.AppendLine("인벤토리 아래의 열린 퀵슬롯 칸으로 드래그해 등록할 수 있습니다.");
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
            MeleeWeaponDefinition melee = entry.Item.MeleeWeaponDefinition;
            if (melee != null)
            {
                AppendMeleeWeapon(text, melee, comparison?.Item.MeleeWeaponDefinition);
                return;
            }

            if (weapon == null)
            {
                text.AppendLine("무기 전투 데이터가 연결되지 않았습니다.");
                return;
            }

            text.AppendLine($"탄종: {AmmoName(weapon.Caliber)}");
            text.AppendLine($"탄창: {entry.MagazineRounds} / {entry.EffectiveMagazineSize}");
            text.AppendLine("현재 발사 방식: " + TriggerModeName(entry.SelectedTriggerMode));
            text.AppendLine($"지원 발사 방식: {BuildSupportedTriggerModes(weapon)}");
            text.AppendLine($"내구도: {entry.WeaponDurability:0.#} / {weapon.MaxDurability:0.#}");
            if (entry.IsWeaponBroken) text.AppendLine("파손됨: 발사·재장전 불가, 거점 수리 필요");
            if (weapon.PelletCount > 1)
            {
                text.AppendLine($"산탄: {weapon.PelletCount}발 · 확산 {weapon.SpreadAngle:0.#}도");
                text.AppendLine($"펠릿당 피해: {weapon.Damage * entry.AttachmentDamageMultiplier:0.#}");
            }
            // 산탄총과 일반 총기를 비교할 때 같은 기준을 사용하도록 한 번의 방아쇠 입력으로 가능한 최대 피해를 표시합니다.
            // 일반 총기는 펠릿이 하나라 기존 피해량과 같고, 산탄총은 모든 펠릿이 맞는 가까운 거리의 이론값입니다.
            text.AppendLine(
                $"발사당 최대 피해: {weapon.PotentialDamagePerShot * entry.AttachmentDamageMultiplier:0.#}" +
                Difference(
                    weapon.PotentialDamagePerShot * entry.AttachmentDamageMultiplier,
                    ComparisonEntryValue(comparison, e => e.Item.WeaponDefinition.PotentialDamagePerShot * e.AttachmentDamageMultiplier),
                    true));
            text.AppendLine($"사거리: {weapon.Range:0.#}{Difference(weapon.Range, ComparisonValue(comparison, w => w.Range), true)}");
            // 비교 수치에는 부착물뿐 아니라 현재 내구도 페널티도 반영해 화면에 보이는 값과 실제 발사 계산을 일치시킵니다.
            float accuracy = weapon.AccuracySpreadAngle * entry.AttachmentAccuracyMultiplier *
                             weapon.GetDurabilityAccuracyMultiplier(entry.WeaponDurabilityNormalized);
            float fireInterval = weapon.FireInterval * entry.AttachmentFireIntervalMultiplier;
            float reload = weapon.ReloadDuration * entry.AttachmentReloadMultiplier *
                           weapon.GetDurabilityReloadMultiplier(entry.WeaponDurabilityNormalized);
            text.AppendLine($"명중 오차: {accuracy:0.#}도{Difference(accuracy, ComparisonEntryValue(comparison, e => e.Item.WeaponDefinition.AccuracySpreadAngle * e.AttachmentAccuracyMultiplier * e.Item.WeaponDefinition.GetDurabilityAccuracyMultiplier(e.WeaponDurabilityNormalized)), false)}");
            text.AppendLine($"밀림: {weapon.KnockbackForce:0.##}{Difference(weapon.KnockbackForce, ComparisonValue(comparison, w => w.KnockbackForce), true)}");
            text.AppendLine($"발사 간격: {fireInterval:0.##}초{Difference(fireInterval, ComparisonEntryValue(comparison, e => e.Item.WeaponDefinition.FireInterval * e.AttachmentFireIntervalMultiplier), false)}");
            text.AppendLine($"재장전: {reload:0.##}초{Difference(reload, ComparisonEntryValue(comparison, e => e.Item.WeaponDefinition.ReloadDuration * e.AttachmentReloadMultiplier * e.Item.WeaponDefinition.GetDurabilityReloadMultiplier(e.WeaponDurabilityNormalized)), false)}");
            if (entry.InstalledAttachments.Count > 0)
            {
                text.AppendLine("장착 부품:");
                foreach (ItemDefinition attachment in entry.InstalledAttachments)
                    if (attachment != null) text.AppendLine("- " + attachment.DisplayName);
                text.AppendLine("무기 관리 패널에서 원하는 슬롯의 해제 버튼을 누를 수 있습니다.");
            }
        }

        // 근접무기는 탄창·재장전·부착물이 없으므로 실제 전투에 필요한 수치만 별도 형식으로 보여 줍니다.
        // comparisonMelee가 있으면 단검과 도끼처럼 같은 근접무기끼리 피해·사거리·공격 속도를 바로 비교합니다.
        private static void AppendMeleeWeapon(
            StringBuilder text,
            MeleeWeaponDefinition melee,
            MeleeWeaponDefinition comparisonMelee)
        {
            text.AppendLine("공격 방식: " +
                            (melee.AttackStyle == MeleeAttackStyle.Stab ? "찌르기" : "휘두르기"));
            text.AppendLine($"피해: {melee.Damage:0.#}{Difference(melee.Damage, comparisonMelee?.Damage, true)}");
            text.AppendLine($"사거리: {melee.Range:0.##}{Difference(melee.Range, comparisonMelee?.Range, true)}");
            text.AppendLine($"판정 폭: {melee.HitRadius:0.##}{Difference(melee.HitRadius, comparisonMelee?.HitRadius, true)}");
            text.AppendLine($"공격 간격: {melee.AttackInterval:0.##}초{Difference(melee.AttackInterval, comparisonMelee?.AttackInterval, false)}");
            text.AppendLine($"최대 대상: {melee.MaximumTargets}명");
            text.AppendLine($"밀림: {melee.KnockbackForce:0.##}{Difference(melee.KnockbackForce, comparisonMelee?.KnockbackForce, true)}");
            text.AppendLine($"방어 관통: {melee.ArmorPenetration}");
            text.AppendLine($"소음 반경: {melee.NoiseRadius:0.#}");
        }

        public static string TriggerModeName(WeaponTriggerMode mode)
            => mode == WeaponTriggerMode.Automatic ? "연사" : "단발";

        private static string BuildSupportedTriggerModes(WeaponDefinition weapon)
        {
            if (weapon.SupportsSemiAutomatic && weapon.SupportsAutomatic) return "단발 / 연사 (B키 전환)";
            return weapon.SupportsAutomatic ? "연사 전용" : "단발 전용";
        }

        private static float? ComparisonValue(InventoryEntry comparison, System.Func<WeaponDefinition, float> selector)
        {
            WeaponDefinition definition = comparison?.Item.WeaponDefinition;
            return definition != null ? selector(definition) : (float?)null;
        }

        private static float? ComparisonEntryValue(InventoryEntry comparison, System.Func<InventoryEntry, float> selector)
        {
            return comparison?.Item.WeaponDefinition != null ? selector(comparison) : (float?)null;
        }

        private static void AppendAttachment(StringBuilder text, WeaponAttachmentDefinition attachment)
        {
            if (attachment == null)
            {
                text.AppendLine("부착물 전투 데이터가 연결되지 않았습니다.");
                return;
            }

            text.AppendLine("부착 위치: " + AttachmentSlotName(attachment.Slot));
            text.AppendLine("호환 탄종: " + AmmoName(attachment.CompatibleCaliber));
            if (System.Math.Abs(attachment.DamageMultiplier - 1f) > 0.001f)
                text.AppendLine($"피해 배율: ×{attachment.DamageMultiplier:0.##}");
            if (System.Math.Abs(attachment.FireIntervalMultiplier - 1f) > 0.001f)
                text.AppendLine($"발사 간격 배율: ×{attachment.FireIntervalMultiplier:0.##}");
            if (System.Math.Abs(attachment.AccuracySpreadMultiplier - 1f) > 0.001f)
                text.AppendLine($"명중 오차 배율: ×{attachment.AccuracySpreadMultiplier:0.##}");
            if (System.Math.Abs(attachment.ReloadDurationMultiplier - 1f) > 0.001f)
                text.AppendLine($"재장전 배율: ×{attachment.ReloadDurationMultiplier:0.##}");
            if (attachment.MagazineCapacityBonus > 0)
                text.AppendLine($"탄창 용량: +{attachment.MagazineCapacityBonus}");
            text.AppendLine("호환 무기 위나 장비 무기 슬롯으로 드래그해 설치합니다.");
        }

        private static void AppendStatusEffect(StringBuilder text, StatusEffectDefinition effect)
        {
            if (effect == null)
            {
                text.AppendLine("시간제 효과 데이터가 연결되지 않았습니다.");
                return;
            }

            text.AppendLine($"효과: {effect.DisplayName}");
            text.AppendLine($"구분: {(effect.Disposition == StatusEffectDisposition.Buff ? "버프" : "디버프")}");
            text.AppendLine($"지속시간: {effect.DurationSeconds:0.#}초 · 우선순위 {effect.Priority}");
            if (effect.Uses(EffectStatToggle.MovementSpeed))
                text.AppendLine($"이동 속도: ×{effect.MovementSpeedMultiplier:0.##}");
            if (effect.Uses(EffectStatToggle.IncomingDamage))
                text.AppendLine($"받는 피해: ×{effect.IncomingDamageMultiplier:0.##}");
            if (effect.Uses(EffectStatToggle.OutgoingDamage))
                text.AppendLine($"주는 피해: ×{effect.OutgoingDamageMultiplier:0.##}");
            text.AppendLine("같은 효과 그룹에서는 수치가 아니라 우선순위가 높은 효과만 유지됩니다.");
        }

        private static string AttachmentSlotName(WeaponAttachmentSlot slot)
        {
            switch (slot)
            {
                case WeaponAttachmentSlot.Optic: return "조준경";
                case WeaponAttachmentSlot.Muzzle: return "총구";
                case WeaponAttachmentSlot.Magazine: return "탄창";
                default: return "설정되지 않음";
            }
        }

        private static string ArmorClassName(ArmorClass armorClass)
        {
            switch (armorClass)
            {
                case ArmorClass.Soft: return "연질 방탄";
                case ArmorClass.Tactical: return "전술 방탄";
                case ArmorClass.Heavy: return "중장갑";
                default: return "방어 없음";
            }
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
