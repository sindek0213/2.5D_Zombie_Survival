using System.Collections.Generic;
using UnityEngine;

namespace ZombieSurvival.Art
{
    // Resources 폴더에 복사된 생성 원화를 현재 게임 코드가 공통으로 불러오도록 경로와 캐시를 한곳에서 관리합니다.
    // 에디터 적용 메뉴를 실행하지 못한 환경에서도 런타임에 같은 그림을 찾을 수 있는 안전망 역할을 합니다.
    public static class GeneratedArtResources
    {
        private const string Root = "GeneratedArt";
        private const string UiShaderPath = Root + "/Shaders/GeneratedUiWhiteKey";
        private const string SpriteShaderPath = Root + "/Shaders/GeneratedSpriteWhiteKey";

        private static readonly Dictionary<string, string> ItemIconPaths = new Dictionary<string, string>
        {
            { "weapon_pistol", Root + "/Equipment/Weapon_Pistol_9mm_Draft" },
            { "weapon_rifle", Root + "/Equipment/Weapon_AssaultRifle_Draft" },
            { "weapon_shotgun", Root + "/Equipment/Weapon_PumpShotgun_Draft" },
            { "armor_light", Root + "/Equipment/Armor_PoliceVest_Draft" },
            { "rig_small", Root + "/Equipment/Armor_PlateCarrier_Draft" },
            { "backpack_small", Root + "/Equipment/Bag_Student_Draft" }
        };

        // 생성된 장비 PNG는 실제 물체보다 훨씬 큰 1024×1536 캔버스를 사용합니다.
        // 아래 Rect는 Unity의 왼쪽 아래 원점을 기준으로 물체와 작은 여백만 포함하는 영역이며,
        // 전체 캔버스를 아이콘으로 사용했을 때 장비가 아주 작게 보이던 문제를 막습니다.
        private static readonly Dictionary<string, Rect> ItemIconPixelRects = new Dictionary<string, Rect>
        {
            { "weapon_pistol", new Rect(167f, 279f, 364f, 495f) },
            { "weapon_rifle", new Rect(8f, 158f, 1004f, 1269f) },
            { "weapon_shotgun", new Rect(25f, 178f, 986f, 1214f) },
            { "armor_light", new Rect(232f, 519f, 562f, 618f) },
            { "rig_small", new Rect(216f, 501f, 592f, 699f) },
            { "backpack_small", new Rect(235f, 480f, 553f, 721f) }
        };

        private static readonly Dictionary<string, Sprite> ItemIconCache = new Dictionary<string, Sprite>();
        private static Material uiWhiteKeyMaterial;
        private static Material spriteWhiteKeyMaterial;

        public static Texture2D LoadTexture(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            return Resources.Load<Texture2D>($"{Root}/{relativePath}");
        }

        public static Sprite GetItemIcon(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) || !ItemIconPaths.TryGetValue(itemId, out string path))
                return null;
            if (ItemIconCache.TryGetValue(itemId, out Sprite cached)) return cached;

            Texture2D texture = Resources.Load<Texture2D>(path);
            if (texture == null) return null;

            // Resources의 복사본은 Texture2D 기본 형식으로도 동작하도록 런타임 Sprite로 감쌉니다.
            // 이때 실제 장비가 들어 있는 부분만 선택하므로 커다란 흰색·체크무늬 여백이 아이콘 크기 계산에 포함되지 않습니다.
            Rect cropRect = ItemIconPixelRects.TryGetValue(itemId, out Rect configuredRect)
                ? ClampPixelRect(configuredRect, texture)
                : new Rect(0f, 0f, texture.width, texture.height);
            Sprite sprite = Sprite.Create(
                texture,
                cropRect,
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = itemId + "_GeneratedIcon";
            ItemIconCache[itemId] = sprite;
            return sprite;
        }

        private static Rect ClampPixelRect(Rect requestedRect, Texture2D texture)
        {
            // 원본 그림이 나중에 다시 교체되어 크기가 달라져도 Sprite.Create에 잘못된 범위를 넘기지 않도록
            // 시작점과 끝점을 현재 Texture 안으로 제한하고 최소 1픽셀 크기를 보장합니다.
            float xMin = Mathf.Clamp(requestedRect.xMin, 0f, Mathf.Max(0f, texture.width - 1f));
            float yMin = Mathf.Clamp(requestedRect.yMin, 0f, Mathf.Max(0f, texture.height - 1f));
            float xMax = Mathf.Clamp(requestedRect.xMax, xMin + 1f, texture.width);
            float yMax = Mathf.Clamp(requestedRect.yMax, yMin + 1f, texture.height);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        public static Material GetUiWhiteKeyMaterial()
        {
            if (uiWhiteKeyMaterial == null)
                uiWhiteKeyMaterial = CreateWhiteKeyMaterial(UiShaderPath, "Generated UI White Key Runtime");
            return uiWhiteKeyMaterial;
        }

        public static Material GetSpriteWhiteKeyMaterial()
        {
            if (spriteWhiteKeyMaterial == null)
                spriteWhiteKeyMaterial = CreateWhiteKeyMaterial(SpriteShaderPath, "Generated Sprite White Key Runtime");
            return spriteWhiteKeyMaterial;
        }

        private static Material CreateWhiteKeyMaterial(string shaderResourcePath, string materialName)
        {
            Shader shader = Resources.Load<Shader>(shaderResourcePath);
            if (shader == null) return null;

            var material = new Material(shader)
            {
                name = materialName,
                // 씬이나 프리팹 에셋에 저장되지 않는 공용 런타임 재질임을 Unity에 알려 플레이 종료 뒤 임시 객체로 남지 않게 합니다.
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetFloat("_WhiteDistance", 0.06f);
            material.SetFloat("_EdgeSoftness", 0.08f);
            // 일부 원본은 투명 배경처럼 보이는 회색 체크무늬가 실제 RGB 픽셀로 저장되어 있습니다.
            // 체크무늬의 회색값만 별도로 투명 처리하여 방탄복과 전신 그림 뒤에 격자가 보이지 않게 합니다.
            material.SetFloat("_CheckerBrightness", 0.62f);
            material.SetFloat("_CheckerChroma", 0.035f);
            material.SetFloat("_CheckerSoftness", 0.06f);
            return material;
        }
    }
}
