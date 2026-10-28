using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // UI-only one-target checklist. Never grants crafting permission,
    // alters cost/materials, spawns units or issues transactions.
    public sealed class MobileFusionGoalTracker
    {
        public string RecipeId { get; private set; }
        public bool HasGoal => !string.IsNullOrEmpty(RecipeId);

        public bool Toggle(string recipeId)
        {
            if (string.IsNullOrWhiteSpace(recipeId))
                throw new ArgumentException("유효한 조합식 ID가 필요합니다.", nameof(recipeId));
            if (string.Equals(RecipeId, recipeId, StringComparison.Ordinal))
            {
                RecipeId = null;
                return false;
            }
            RecipeId = recipeId;
            return true;
        }

        public void Clear() { RecipeId = null; }

        public MobileFusionGoalState Resolve(
            IReadOnlyList<MobileFusionPreview> source,
            Func<string, bool> isApproved)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (isApproved == null) throw new ArgumentNullException(nameof(isApproved));
            if (!HasGoal) return null;
            foreach (var offer in source)
            {
                if (offer == null || offer.RecipeId != RecipeId) continue;
                return new MobileFusionGoalState(offer.Title, RecipeId,
                    offer.IsReady, isApproved(RecipeId), offer.MissingMaterials);
            }
            return new MobileFusionGoalState("조합식 목록 확인 필요", RecipeId,
                false, false, new string[0], false);
        }
    }

    public sealed class MobileFusionGoalState
    {
        public string Title { get; private set; }
        public string RecipeId { get; private set; }
        public bool IsFound { get; private set; }
        public bool MaterialsReady { get; private set; }
        public bool Approved { get; private set; }
        public bool CanCraftNow => IsFound && MaterialsReady && Approved;
        public IReadOnlyList<string> MissingMaterials { get; private set; }
        public string Status => !IsFound ? "조합식 확인 필요" :
            !Approved ? "원본 제작 기능 미연결" :
            !MaterialsReady ? "재료 부족" : "지금 제작 가능";

        public MobileFusionGoalState(string title,string recipeId,
            bool materialsReady,bool approved,IReadOnlyList<string> missing,
            bool found=true)
        {
            Title=title ?? "";
            RecipeId=recipeId ?? "";
            IsFound=found;
            MaterialsReady=materialsReady;
            Approved=approved;
            MissingMaterials=missing ?? new string[0];
        }

        public string MissingSummary(int maxLines=3)
        {
            if (maxLines<1)throw new ArgumentOutOfRangeException(nameof(maxLines));
            if (!IsFound)return "조합식 목록을 다시 불러와 주세요.";
            if (MissingMaterials.Count==0)
                return !Approved ? "원작 제작 조건 검증 대기" :
                    "보유 재료 확보 · 제작 가능 여부는 확정 시 재검증";
            var count=Math.Min(MissingMaterials.Count,maxLines);
            var shown=new List<string>();
            for(int i=0;i<count;i++)
                shown.Add(MissingMaterials[i]);
            return string.Join(" / ",shown.ToArray())+
                (MissingMaterials.Count>count ?
                    " 외 "+(MissingMaterials.Count-count)+"종" : "");
        }
    }
}
