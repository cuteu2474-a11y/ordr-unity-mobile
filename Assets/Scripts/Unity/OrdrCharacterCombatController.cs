using System;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Unity
{
    // 씬의 빈 GameObject에 추가하는 데이터/전투 연결 진입점.
    // 원본 피해 계산기가 확인되기 전에는 런타임을 시작하지 않으며 임시 피해량을 쓰지 않는다.
    public sealed class OrdrCharacterCombatController : MonoBehaviour
    {
        [SerializeField] private TextAsset unitCatalogJson;
        [SerializeField] private TextAsset characterSkillsJson;

        public CharacterUnitCatalog Units { get; private set; }
        public CharacterSkillCatalog Skills { get; private set; }
        public CharacterCombatRuntime Combat { get; private set; }

        // 전투 UI/로그가 스킬·피해·상태 적용 이벤트를 구독할 수 있다.
        public event Action<CharacterCombatEvent> OnCombatEvent;

        private void Awake()
        {
            // 동봉된 TextAsset이 없으면 Resources 폴더에서 읽는다.
            var unitsData = unitCatalogJson != null ? unitCatalogJson :
                Resources.Load<TextAsset>("OrdrData/units");
            var skillsData = characterSkillsJson != null ? characterSkillsJson :
                Resources.Load<TextAsset>("OrdrData/character-skill-profiles");
            if (unitsData == null || skillsData == null)
            {
                Debug.LogError("ORDR 데이터 미포함: Assets/Resources/OrdrData 내 JSON을 동기화하거나 Inspector에 TextAsset을 지정하세요.");
                enabled = false;
                return;
            }
            try
            {
                Units = CharacterUnitCatalog.FromJson(unitsData.text);
                Skills = CharacterSkillCatalog.FromJson(skillsData.text);
                if (Units.Count != 271 || Skills.CharacterCount != 200)
                    throw new FormatException("2.323[C] 캐릭터 데이터 등록 수가 기대값과 다릅니다.");
                Debug.Log("ORDR 캐릭터 카탈로그 로딩: " + Units.Count +
                    "종, 스킬 프로필 " + Skills.CharacterCount + "종, 스킬 항목 " +
                    Skills.SkillCount + "개.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                enabled = false;
            }
        }

        // 구현된 원작 물리/마법·스킬 피해 계산기와 난수 공급원을 연결한 후 호출한다.
        public void ConfigureRuntime(IOriginalDamageResolver verifiedBasicFormula,
            IVerifiedCharacterSkillDamageResolver verifiedSkillFormula,
            ICombatProcRandom random)
        {
            if (Units == null || Skills == null)
                throw new InvalidOperationException("도감 JSON 로딩이 완료되지 않았습니다.");
            if (Combat != null)
                throw new InvalidOperationException("이미 전투 런타임이 구성됐습니다.");
            Combat = new CharacterCombatRuntime(Units, Skills,
                verifiedBasicFormula, verifiedSkillFormula, random);
        }

        // 원작 피해 공식 없이도 캐릭터 소환·선택·배치·웨이브 월드를 구성할 수 있다.
        // 피해 공식은 반드시 미구현 예외로 보류된다. 실제 난이도/전투 검증에 사용하지 않는다.
        public void ConfigureCatalogOnlyWorld(uint seed)
        {
            var unavailable = new RejectUnverifiedDamageFormula();
            ConfigureRuntime(unavailable, unavailable, new SeededNonOriginalProcRandom(seed));
            Debug.LogWarning("ORDR 도감/월드 전용 모드: 평타·스킬 피해 공식은 미구현으로 보류됩니다.");
        }

        public void BeginSession()
        {
            if (Combat == null)
                throw new InvalidOperationException("원작 피해 공식 구현체 연결이 필요합니다.");
            Combat.Start();
        }

        private void Update()
        {
            if (Combat == null || Combat.Game.Phase != Ordr.Mobile.Core.SessionPhase.Running)
                return;
            foreach (var item in Combat.Step(Time.deltaTime))
            {
                var handler = OnCombatEvent;
                if (handler != null) handler(item);
            }
        }
    }
}
