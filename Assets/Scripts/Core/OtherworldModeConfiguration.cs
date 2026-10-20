using System;

namespace Ordr.Mobile.Core
{
    // 이세계모드는 도감에 별도 유닛 표시 옵션으로 존재한다.
    // 전투 효과·해금 시점 등 확인되지 않은 규칙은 여기서 생성하지 않는다.
    public sealed class OtherworldModeConfiguration
    {
        public const string ModeId = "nightmare-otherworld";
        public const string SourceUrl = "https://ordr.kr/";
        public const string SourceVersion = "2.323";

        public bool NightmareOnly => true;
        public bool OtherworldEnabled => true;

        // 도감에 이세계모드 전용으로 분류된 유닛만 명시적으로 표기한다.
        // 기본 유닛에 임의의 특수 보정을 주지 않는다.
        public bool IsOtherworldUnit(UnitCatalogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            return entry.OtherworldOnly;
        }
    }

    public sealed class UnitCatalogEntry
    {
        public string Id { get; }
        public string Name { get; }
        public bool OtherworldOnly { get; }
        public string SourceUrl { get; }

        public UnitCatalogEntry(string id, string name, bool otherworldOnly, string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("도감 유닛 식별자와 이름은 필수입니다.");
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                throw new ArgumentException("확인 가능한 출처 URL이 필요합니다.");
            Id = id;
            Name = name;
            OtherworldOnly = otherworldOnly;
            SourceUrl = sourceUrl;
        }
    }
}
