using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Core;

namespace ZombieSurvival.UI
{
    // Canvas 아래 모든 Text의 원래 글자 크기를 기억하고 접근성 설정 배율만 곱합니다.
    // 화면마다 개별 스크립트를 수정하지 않아도 되며, 설정을 여러 번 바꿔도 이미 커진 크기에 다시 배율을 곱하지 않습니다.
    public sealed class AccessibilityTextScaler : MonoBehaviour
    {
        private readonly Dictionary<Text, int> baseSizes = new Dictionary<Text, int>();

        private void Awake() => CaptureAndApply();

        private void OnEnable()
        {
            GameSettings.Changed -= CaptureAndApply;
            GameSettings.Changed += CaptureAndApply;
            CaptureAndApply();
        }

        private void OnDisable() => GameSettings.Changed -= CaptureAndApply;

        private void CaptureAndApply()
        {
            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                if (text == null) continue;
                if (!baseSizes.ContainsKey(text)) baseSizes[text] = Mathf.Max(1, text.fontSize);
                text.fontSize = Mathf.Max(10, Mathf.RoundToInt(baseSizes[text] * GameSettings.FontScale));
            }
        }
    }
}
