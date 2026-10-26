using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    // All seed and source witnesses are controlled fixtures, not proof of an
    // actual Warcraft client. Dummy damage is NOT inferred by these tests.
    public sealed class Original323AttackedDummySpellTests
    {
        private sealed class DrawWitness : IOriginal323ScriptRandomDrawWitness
        {
            public int Calls;
            public bool TryConfirmOriginalDraw(long ordinal,
                Original323ScriptRandomPurpose purpose,
                int oldState, int newState, int min, int max,
                int value, out string proof)
            {
                Calls++;
                proof = "observed original BFi attack event";
                return true;
            }
        }

        private sealed class AttackWitness : IOriginal323ScriptAttackProcWitness
        {
            public bool Approved = true;
            public int Calls;
            public bool TryConfirmOriginalAttackEvent(string function,
                string actor, string target, long sequence, out string proof)
            {
                Calls++;
                proof = "observed original attacked trigger for matching unit";
                return Approved && target == "enemy" &&
                    actor == "attacker";
            }
        }

        private sealed class SpellWorld : IOriginal323VerifiedAttackDummySpellWorld
        {
            public int Calls;
            public bool Approve = true;
            public Original323AttackDummySpellCommand Last;
            public string Actor;
            public string Target;
            public bool TryApplyOriginalDummySpell(
                Original323AttackDummySpellCommand order,
                CharacterCombatRuntime combat, string actor,
                string attacked, out string evidence)
            {
                Calls++; Last = order; Actor = actor; Target = attacked;
                evidence = Approve ? "observed Warcraft original dummy spell" :
                    "original order is not yet supported";
                return Approve;
            }
        }

        private static CharacterCombatRuntime Combat()
        {
            var u = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[]}");
            var s = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var defer = new RejectUnverifiedDamageFormula();
            return new CharacterCombatRuntime(u,s,defer,defer,
                new SeededNonOriginalProcRandom(2));
        }

        private static Original323ScriptAttackEffectCoordinator Coordinator(
            int sourceSeed, AttackWitness attacked, SpellWorld spell,
            DrawWitness draws = null)
        {
            var stream = new Original323SharedBfiRandom(sourceSeed,
                "source observed native initial seed", draws ?? new DrawWitness());
            return new Original323ScriptAttackEffectCoordinator(
                new Original323ScriptAttackProcRolls(stream,attacked),spell);
        }

        [Test]
        public void SourceConfirmedAttackerNamesAndOrdersAreExact()
        {
            Original323AttackDummySpellCommand blue, fuk, enel, missing;
            Assert.IsTrue(Original323ScriptAttackEffectCoordinator.TryCreateOriginalSpell(
                "h00M", out blue));
            Assert.IsTrue(Original323ScriptAttackEffectCoordinator.TryCreateOriginalSpell(
                "h00A", out fuk));
            Assert.IsTrue(Original323ScriptAttackEffectCoordinator.TryCreateOriginalSpell(
                "h00Q", out enel));
            Assert.AreEqual("블루노",blue.HumanOwnerName);
            Assert.AreEqual("후쿠로",fuk.HumanOwnerName);
            Assert.AreEqual("에넬",enel.HumanOwnerName);
            Assert.AreEqual("e0KD",blue.DummyUnitRawcode);
            Assert.AreEqual("e0KD",enel.DummyUnitRawcode);
            Assert.AreEqual("carrionswarm",blue.WarcraftOrder);
            Assert.AreEqual("carrionswarm",fuk.WarcraftOrder);
            Assert.AreEqual("forkedlightning",enel.WarcraftOrder);
            Assert.AreEqual("A0VS",blue.OriginalAbilityRawcode);
            Assert.AreEqual("A086",enel.OriginalAbilityRawcode);
            Assert.AreEqual(Original323DummySpellOrderKind.PointAtAttackerLocation,
                blue.OrderKind);
            Assert.AreEqual(Original323DummySpellOrderKind.TargetAttackedEnemy,
                enel.OrderKind);
            Assert.IsTrue(blue.SpawnAtAttackerLocation);
            Assert.IsTrue(enel.StoreAttackerPlayerSlotAsDummyUserData);
            Assert.IsTrue(blue.UseAttackerFacing);
            Assert.IsFalse(enel.UseAttackerFacing);
            Assert.IsFalse(Original323ScriptAttackEffectCoordinator.TryCreateOriginalSpell(
                "h00D",out missing));
        }

        [Test]
        public void BluenoTenPercentProcDispatchesNativeSwarmOrder()
        {
            var witness = new AttackWitness();
            var world = new SpellWorld();
            var coordinator = Coordinator(3,witness,world);
            Original323AttackDummySpellResult result;
            Assert.IsTrue(coordinator.TryDispatch("h00M","attacker","enemy",1,
                Combat(),out result));
            Assert.IsTrue(result.Triggered);
            Assert.IsTrue(result.Applied);
            Assert.AreEqual("범위 충격파",result.HumanSkillName);
            Assert.AreEqual("Bnm",world.Last.OriginalScriptFunction);
            Assert.AreEqual("enemy",world.Target);
            Assert.AreEqual(1,world.Calls);
        }

        [Test]
        public void FukuroOwnsDistinctOriginalTriggerAndNoExtraRandomDraw()
        {
            var draw = new DrawWitness();
            var world = new SpellWorld();
            var coordinator = Coordinator(3,new AttackWitness(),world,draw);
            Original323AttackDummySpellResult one, repeat;
            Assert.IsTrue(coordinator.TryDispatch("h00A","attacker","enemy",1,
                Combat(),out one));
            Assert.IsTrue(one.Applied);
            Assert.AreEqual("Bnp",world.Last.OriginalScriptFunction);
            Assert.IsFalse(coordinator.TryDispatch("h00A","attacker","enemy",1,
                Combat(),out repeat));
            Assert.AreEqual(1,draw.Calls);
            Assert.AreEqual(1,world.Calls);
        }

        [Test]
        public void EnelForkedLightningUsesAttackedEnemyUnitAsTarget()
        {
            var world = new SpellWorld();
            var coordinator = Coordinator(3,new AttackWitness(),world);
            Original323AttackDummySpellResult result;
            Assert.IsTrue(coordinator.TryDispatch("h00Q","attacker","enemy",1,
                Combat(),out result));
            Assert.IsTrue(result.Applied);
            Assert.AreEqual("Bnr",world.Last.OriginalScriptFunction);
            Assert.AreEqual(Original323DummySpellOrderKind.TargetAttackedEnemy,
                world.Last.OrderKind);
            Assert.AreEqual("enemy",world.Target);
        }

        [Test]
        public void FailedRollConsumesOneDrawButDoesNotSpawnDummy()
        {
            var world = new SpellWorld();
            var coordinator = Coordinator(3,new AttackWitness(),world);
            Original323AttackDummySpellResult one,two;
            Assert.IsTrue(coordinator.TryDispatch("h00Q","attacker","enemy",1,
                Combat(),out one));
            Assert.IsTrue(coordinator.TryDispatch("h00Q","attacker","enemy",2,
                Combat(),out two));
            Assert.IsFalse(two.Triggered); // seed 3: second BFi(1,100)=67
            Assert.AreEqual(1,world.Calls);
        }

        [Test]
        public void WrongAttackerOrUnobservedEventCannotConsumeOriginalRandom()
        {
            var draw = new DrawWitness();
            var attack = new AttackWitness { Approved = false };
            var world = new SpellWorld();
            var coord = Coordinator(3,attack,world,draw);
            Original323AttackDummySpellResult result;
            Assert.IsFalse(coord.TryDispatch("h00D","attacker","enemy",1,
                Combat(),out result));
            Assert.IsFalse(coord.TryDispatch("h00M","attacker","enemy",1,
                Combat(),out result));
            Assert.AreEqual(0,draw.Calls);
            Assert.AreEqual(0,world.Calls);
        }

        [Test]
        public void NativeUnverifiedSpellDoesNotManufactureDamageOrSuccess()
        {
            var world = new SpellWorld { Approve = false };
            var coord = Coordinator(3,new AttackWitness(),world);
            Original323AttackDummySpellResult result;
            Assert.IsTrue(coord.TryDispatch("h00M","attacker","enemy",1,
                Combat(),out result));
            Assert.IsTrue(result.Triggered);
            Assert.IsFalse(result.Applied);
            Assert.AreEqual(1,world.Calls);
        }

        private sealed class ZeroRandom : ICombatProcRandom
        {
            public int Next(int max) { return 0; }
        }
        private sealed class Formula : IOriginalDamageResolver
        {
            public int Declared;
            public long ResolveBasicDamage(BasicDamageRequest request,
                CombatEnemy enemy)
            {
                Assert.AreEqual(1,Declared);
                return 7;
            }
        }

        [Test]
        public void AttackedScriptCallbackOccursBeforeHitWithoutOverwritingBonneyHook()
        {
            var session = new GameSession();
            session.Start(); session.BeginWave(1);
            var scheduler = new CombatScheduler();
            var resolution = new CombatResolution(session,scheduler);
            resolution.Spawn(new CombatEnemy("enemy",100));
            var triggers = new CombatSkillTriggerEngine(new ZeroRandom());
            var resolver = new Formula();
            var pipeline = new CombatAttackPipeline(scheduler,resolution,
                triggers,resolver);
            pipeline.ConfigureVerifiedAttackDeclared((unit,target)=>{
                Assert.AreEqual("actor",unit);
                Assert.AreEqual("enemy",target);
                resolver.Declared++;
            });
            int observed = 0;
            pipeline.ConfigureOriginalScriptAttackDeclared((unit,raw,target)=>{
                Assert.AreEqual("actor",unit);
                Assert.AreEqual("h00M",raw);
                Assert.AreEqual("enemy",target);
                Assert.AreEqual(1,resolver.Declared);
                observed++;
            });
            var spec = new UnitCombatSpec("h00M",
                new BasicAttackSpec(20,1,20,DamageSchool.Physical,
                    DamageTraits.None),
                new CombatSkillSpec[0],"TEST_FIXTURE");
            pipeline.RegisterUnit("actor",spec,0);
            BasicAttackResult result;
            Assert.IsTrue(pipeline.TryBasicAttack("actor",
                new[]{new TargetCandidate("enemy",1,0,true)},out result));
            Assert.AreEqual(7,result.ActualDamage);
            Assert.AreEqual(1,observed);
            Assert.AreEqual(93,resolution.GetEnemy("enemy").Health);
            Assert.Throws<InvalidOperationException>(() =>
                pipeline.ConfigureOriginalScriptAttackDeclared((a,b,c)=>{}));
        }
    }
}
