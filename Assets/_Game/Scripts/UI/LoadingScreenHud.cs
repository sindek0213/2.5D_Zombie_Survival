using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Core;

namespace ZombieSurvival.UI
{
    // Loading 씬의 전체 화면 배경, 진행 문구와 아래쪽 로딩 바를 표시합니다.
    // 실제 씬 로드는 LoadingSceneController가 담당하고 이 클래스는 화면 표현만 맡아 이미지와 UI 디자인을 쉽게 교체할 수 있습니다.
    public sealed class LoadingScreenHud : MonoBehaviour
    {
        [SerializeField] private LoadingScreenCatalog catalog;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private AspectRatioFitter backgroundAspectFitter;
        [SerializeField] private Image progressFillImage;
        [SerializeField] private Text progressText;
        [SerializeField] private Text statusText;
        [SerializeField] private Text hintText;

        private readonly List<Sprite> candidates = new List<Sprite>();
        private static Sprite lastShownImage;

        public int AvailableImageCount => candidates.Count;

        // Editor 생성기가 카탈로그와 각 uGUI 자식을 프리팹에 직렬화할 때 사용합니다.
        public void ConfigureViews(
            LoadingScreenCatalog loadingCatalog,
            Image background,
            AspectRatioFitter aspectFitter,
            Image progressFill,
            Text progressLabel,
            Text statusLabel,
            Text hintLabel)
        {
            catalog = loadingCatalog;
            backgroundImage = background;
            backgroundAspectFitter = aspectFitter;
            progressFillImage = progressFill;
            progressText = progressLabel;
            statusText = statusLabel;
            hintText = hintLabel;

            // 프리팹 편집 화면에서도 로딩 바가 완전히 비어 보이지 않도록 0% 상태를 저장합니다.
            SetProgress(0f, "이동할 지역을 준비하는 중입니다...");
        }

        private void Awake()
        {
            SelectRandomBackground();
            SetProgress(0f, "저장 데이터와 지역을 준비하는 중입니다...");
        }

        // 공용 이미지와 현재 해금된 지역 이미지를 모은 뒤 한 장을 무작위로 선택합니다.
        // 후보가 두 장 이상이면 직전에 표시한 이미지와 같은 결과를 한 칸 넘겨 연속 중복을 줄입니다.
        public void SelectRandomBackground()
        {
            candidates.Clear();
            catalog?.CollectUnlockedImages(candidates);
            if (backgroundImage == null) return;

            if (candidates.Count == 0)
            {
                backgroundImage.sprite = null;
                backgroundImage.color = new Color(0.025f, 0.035f, 0.04f, 1f);
                if (hintText != null) hintText.text = "로딩 이미지가 연결되지 않았습니다.";
                return;
            }

            int selectedIndex = Random.Range(0, candidates.Count);
            if (candidates.Count > 1 && candidates[selectedIndex] == lastShownImage)
                selectedIndex = (selectedIndex + 1) % candidates.Count;

            Sprite selected = candidates[selectedIndex];
            lastShownImage = selected;
            backgroundImage.sprite = selected;
            backgroundImage.color = Color.white;

            // EnvelopeParent는 원본 비율을 유지하면서 화면 전체를 덮고 남는 가장자리만 잘라, 해상도가 달라도 이미지가 찌그러지지 않게 합니다.
            if (backgroundAspectFitter != null && selected.rect.height > 0f)
            {
                backgroundAspectFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                backgroundAspectFitter.aspectRatio = selected.rect.width / selected.rect.height;
            }

            if (hintText != null)
                hintText.text = "해금된 지역의 로딩 이미지 중 무작위로 표시됩니다.";
        }

        // 0~1 진행률을 Image.fillAmount와 정수 백분율에 동시에 반영합니다.
        // 호출부가 비정상 범위를 전달해도 Clamp01로 로딩 바가 프레임 밖으로 넘어가지 않게 합니다.
        public void SetProgress(float normalizedProgress, string message)
        {
            float safeProgress = Mathf.Clamp01(normalizedProgress);
            if (progressFillImage != null) progressFillImage.fillAmount = safeProgress;
            if (progressText != null) progressText.text = Mathf.RoundToInt(safeProgress * 100f) + "%";
            if (statusText != null && !string.IsNullOrWhiteSpace(message)) statusText.text = message;
        }
    }
}
