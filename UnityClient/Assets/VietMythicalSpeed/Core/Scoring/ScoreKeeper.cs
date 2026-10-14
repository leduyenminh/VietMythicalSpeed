using System;

namespace VMS.Core
{
    public enum Judgement
    {
        Perfect,
        Good,
        Miss,
        HazardHit,
        HazardDodge,
    }

    public struct JudgeOutcome
    {
        public Judgement judgement;
        public int points;
        public bool big;
        public bool comboBroken;
        public bool shieldUsed;
        public bool guardUsed;
    }

    /// <summary>Lane-distance based judging. Distances are measured in lane widths.</summary>
    public static class JudgeRules
    {
        public const float PerfectWindow = 0.3f;
        public const float GoodWindow = 0.62f;
        public const float HazardRadius = 0.55f;

        public static Judgement JudgePearl(float laneDistance, float perfectWindowMultiplier, float extraReach)
        {
            float d = Math.Abs(laneDistance);
            if (extraReach > 0f && d <= extraReach + PerfectWindow) return Judgement.Perfect;
            if (d <= PerfectWindow * perfectWindowMultiplier) return Judgement.Perfect;
            if (d <= GoodWindow * Math.Max(1f, perfectWindowMultiplier * 0.9f)) return Judgement.Good;
            return Judgement.Miss;
        }

        public static Judgement JudgeHazard(float laneDistance) =>
            Math.Abs(laneDistance) < HazardRadius ? Judgement.HazardHit : Judgement.HazardDodge;
    }

    /// <summary>Score, combo and spirit-gauge bookkeeping for one race.</summary>
    public sealed class ScoreKeeper
    {
        public const float SpiritMax = 100f;
        public const int PerfectPoints = 300;
        public const int GoodPoints = 120;
        public const int DodgePoints = 50;

        public long Score { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public int Perfect { get; private set; }
        public int Good { get; private set; }
        public int Miss { get; private set; }
        public int HazardHits { get; private set; }
        public int HazardDodges { get; private set; }
        public float Spirit { get; private set; }

        /// <summary>Temporary multiplier from an active skill.</summary>
        public float SkillScoreMultiplier = 1f;
        public float SkillSpiritMultiplier = 1f;
        /// <summary>Permanent bonuses from the navigator passive, e.g. 0.05 = +5%.</summary>
        public float PassiveScoreBonus;
        public float PassiveSpiritBonus;
        public int ShieldCharges;
        public int ComboGuardCharges;

        public int Judged => Perfect + Good + Miss + HazardHits + HazardDodges;
        public bool SpiritFull => Spirit >= SpiritMax - 0.001f;

        public float ComboMultiplier => 1f + Math.Min(10, Combo / 20) * 0.1f;

        public float Accuracy
        {
            get
            {
                int judged = Judged;
                if (judged == 0) return 1f;
                return (Perfect + 0.6f * Good + HazardDodges) / judged;
            }
        }

        public JudgeOutcome Register(Judgement judgement, bool big)
        {
            var outcome = new JudgeOutcome { judgement = judgement, big = big };

            if ((judgement == Judgement.Miss || judgement == Judgement.HazardHit) && ShieldCharges > 0)
            {
                ShieldCharges--;
                outcome.shieldUsed = true;
                judgement = judgement == Judgement.Miss ? Judgement.Good : Judgement.HazardDodge;
                outcome.judgement = judgement;
            }

            switch (judgement)
            {
                case Judgement.Perfect:
                    Perfect++;
                    Combo++;
                    outcome.points = Award(PerfectPoints * (big ? 2 : 1));
                    AddSpirit(big ? 7f : 4f);
                    break;
                case Judgement.Good:
                    Good++;
                    Combo++;
                    outcome.points = Award(GoodPoints * (big ? 2 : 1));
                    AddSpirit(big ? 4f : 2f);
                    break;
                case Judgement.HazardDodge:
                    HazardDodges++;
                    Combo++;
                    outcome.points = Award(DodgePoints);
                    AddSpirit(2f);
                    break;
                case Judgement.Miss:
                case Judgement.HazardHit:
                    if (judgement == Judgement.Miss) Miss++;
                    else HazardHits++;
                    if (ComboGuardCharges > 0)
                    {
                        ComboGuardCharges--;
                        outcome.guardUsed = true;
                    }
                    else
                    {
                        outcome.comboBroken = Combo > 0;
                        Combo = 0;
                    }
                    break;
            }
            if (Combo > MaxCombo) MaxCombo = Combo;
            return outcome;
        }

        int Award(int basePoints)
        {
            float mult = ComboMultiplier * SkillScoreMultiplier * (1f + PassiveScoreBonus);
            int points = (int)Math.Round(basePoints * mult);
            Score += points;
            return points;
        }

        void AddSpirit(float amount)
        {
            Spirit = Math.Min(SpiritMax, Spirit + amount * SkillSpiritMultiplier * (1f + PassiveSpiritBonus));
        }

        public bool TrySpendSpirit(float cost)
        {
            if (Spirit + 0.001f < cost) return false;
            Spirit = Math.Max(0f, Spirit - cost);
            return true;
        }

        public string Rank
        {
            get
            {
                float acc = Accuracy;
                if (acc >= 0.985f && Miss == 0 && HazardHits == 0 && Judged > 0) return "S+";
                if (acc >= 0.95f) return "S";
                if (acc >= 0.88f) return "A";
                if (acc >= 0.78f) return "B";
                if (acc >= 0.65f) return "C";
                return "D";
            }
        }
    }
}
