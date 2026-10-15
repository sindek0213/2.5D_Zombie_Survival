using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZombieSurvival.Core
{
    // 특정 레이드 지역과 그 지역이 해금됐을 때 사용할 로딩 이미지를 한 묶음으로 보관합니다.
    // 지역 에셋의 Available 값이 false인 동안에는 해당 이미지가 무작위 후보에 들어가지 않습니다.
    [Serializable]
    public sealed class LoadingRegionImagePool
    {
        [SerializeField] private RaidRegionDefinition region;
        [SerializeField] private Sprite[] images = Array.Empty<Sprite>();

        public RaidRegionDefinition Region => region;
        public IReadOnlyList<Sprite> Images => images ?? Array.Empty<Sprite>();
        public bool IsUnlocked => region != null && region.Available;

        // Editor 생성기가 지역과 이미지를 연결할 때 사용합니다. 배열을 복사해 외부 수정이 에셋 값에 섞이지 않게 합니다.
        public LoadingRegionImagePool(RaidRegionDefinition regionDefinition, Sprite[] regionImages)
        {
            region = regionDefinition;
            images = regionImages != null ? (Sprite[])regionImages.Clone() : Array.Empty<Sprite>();
        }
    }

    // 모든 로딩 화면이 공유하는 공용 이미지와 지역별 해금 이미지를 ScriptableObject로 관리합니다.
    // 새로운 맵을 추가할 때 코드를 수정하지 않고 RegionImagePools 배열에 지역 에셋과 Sprite를 넣으면 됩니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Loading Screen Catalog")]
    public sealed class LoadingScreenCatalog : ScriptableObject
    {
        [SerializeField] private Sprite[] commonImages = Array.Empty<Sprite>();
        [SerializeField] private LoadingRegionImagePool[] regionImagePools = Array.Empty<LoadingRegionImagePool>();

        public IReadOnlyList<Sprite> CommonImages => commonImages ?? Array.Empty<Sprite>();
        public IReadOnlyList<LoadingRegionImagePool> RegionImagePools
            => regionImagePools ?? Array.Empty<LoadingRegionImagePool>();

        // 공용 이미지는 항상 후보에 넣고, 지역별 이미지는 그 지역이 현재 출격 가능할 때만 추가합니다.
        // null Sprite와 같은 Sprite의 중복 참조를 제거해 빈 화면이나 같은 이미지 확률이 과도하게 높아지는 문제를 막습니다.
        public void CollectUnlockedImages(List<Sprite> destination)
        {
            if (destination == null) return;
            destination.Clear();
            var seen = new HashSet<Sprite>();

            AddUniqueImages(commonImages, destination, seen);
            if (regionImagePools == null) return;

            foreach (LoadingRegionImagePool pool in regionImagePools)
            {
                if (pool == null || !pool.IsUnlocked) continue;
                AddUniqueImages(pool.Images, destination, seen);
            }
        }

        // 프로토타입 생성기가 공용·지역 이미지를 한 번에 연결하는 전용 진입점입니다.
        public void Configure(Sprite[] alwaysAvailableImages, LoadingRegionImagePool[] unlockedRegionPools)
        {
            commonImages = alwaysAvailableImages != null
                ? (Sprite[])alwaysAvailableImages.Clone()
                : Array.Empty<Sprite>();
            regionImagePools = unlockedRegionPools != null
                ? (LoadingRegionImagePool[])unlockedRegionPools.Clone()
                : Array.Empty<LoadingRegionImagePool>();
        }

        private static void AddUniqueImages(
            IReadOnlyList<Sprite> source,
            List<Sprite> destination,
            HashSet<Sprite> seen)
        {
            if (source == null) return;
            for (int index = 0; index < source.Count; index++)
            {
                Sprite image = source[index];
                if (image != null && seen.Add(image)) destination.Add(image);
            }
        }
    }
}
