using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // UI labels from the public ORDR 1.31 JASS ShowScoreBoard family.
    // The active 2.323[C] total-clear score ledger is NOT wired yet.
    // Only verified, successfully committed common-wisp draws are counted here.
    public sealed class MobileOriginalChatScoreboard
    {
        public static readonly string[] CommonCatalogIds =
        {
            "C-01", "C-02", "C-03", "C-04", "C-05", "C-06", "C-07", "C-08", "C-09"
        };

        public static readonly string[] CommonNames =
        {
            "루피", "조로", "나미", "우솝", "상디", "쵸파", "버기", "총병", "칼병"
        };

        public static readonly string[] GambleNames =
        {
            "하급", "중급", "고급", "다른세계", "압살롬"
        };

        public static readonly string[] PlusNames =
        {
            "스토리 점수", "(신세계) 마지막 라운드 유닛 점수",
            "(신세계) 라운드 유닛 점수", "(신세계) 신속한 보스 처치",
            "(신세계) 보스 처치", "도움소 마나", "(신세계) 진입"
        };

        public static readonly string[] MinusNames = { "(신세계) 보상 사용" };

        private readonly Dictionary<string, int> confirmedCommonDraws =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public int ConfirmedCommonDraws { get; private set; }

        public MobileOriginalChatScoreboard()
        {
            foreach (string catalogId in CommonCatalogIds)
                confirmedCommonDraws.Add(catalogId, 0);
        }

        // A successful source recruitment transaction is the ONLY caller.
        // Pirate ships, rejects and unrelated roster/fusion creations are excluded.
        public bool RecordCommittedCommonWispDraw(string catalogId)
        {
            int previous;
            if (catalogId == null || !confirmedCommonDraws.TryGetValue(catalogId, out previous))
                return false;
            int next = checked(previous + 1);
            int total = checked(ConfirmedCommonDraws + 1);
            confirmedCommonDraws[catalogId] = next;
            ConfirmedCommonDraws = total;
            return true;
        }

        public int CommonDrawCount(string catalogId)
        {
            int value;
            return catalogId != null && confirmedCommonDraws.TryGetValue(catalogId, out value)
                ? value : 0;
        }
    }
}
