using UnityEngine;
using VMS.Core;

namespace VMS.Race
{
    /// <summary>
    /// The navigator's active skill and passive during a race. Skills only work in Skill and Dodge modes;
    /// Classic keeps scores comparable, like Riichi City's no-skill rule set.
    /// </summary>
    public sealed class SkillRuntime
    {
        readonly SkillDef _skill;
        readonly PassiveDef _passive;
        readonly int _awakening;

        public readonly bool Enabled;
        public float Remaining { get; private set; }
        public float Duration { get; private set; }
        public bool Active => Remaining > 0f;
        public string SkillType => _skill != null ? _skill.type : null;
        public float Cost => _skill != null ? Mathf.Clamp(_skill.cost, 1f, ScoreKeeper.SpiritMax) : ScoreKeeper.SpiritMax;

        float _passivePerfect;

        public SkillRuntime(NavigatorDef navigator, int awakening, bool enabled)
        {
            _skill = navigator != null ? navigator.skill : null;
            _passive = navigator != null ? navigator.passive : null;
            _awakening = awakening;
            Enabled = enabled && _skill != null;
        }

        /// <summary>Applies passive bonuses that live inside the score keeper.</summary>
        public void ApplyPassive(ScoreKeeper score)
        {
            if (!Enabled || _passive == null) return;
            float v = _passive.ValueAt(_awakening);
            switch (_passive.type)
            {
                case PassiveTypes.Score: score.PassiveScoreBonus = v; break;
                case PassiveTypes.Spirit: score.PassiveSpiritBonus = v; break;
                case PassiveTypes.PerfectWindow: _passivePerfect = v; break;
            }
        }

        public bool CanCast(ScoreKeeper score) => Enabled && !Active && score.Spirit + 0.001f >= Cost;

        public bool TryCast(ScoreKeeper score)
        {
            if (!CanCast(score) || !score.TrySpendSpirit(Cost)) return false;
            Duration = Mathf.Max(0.5f, _skill.DurationAt(_awakening));
            Remaining = Duration;
            float v = _skill.ValueAt(_awakening);
            switch (_skill.type)
            {
                case SkillTypes.Shield: score.ShieldCharges += Mathf.Max(1, Mathf.RoundToInt(v)); break;
                case SkillTypes.ComboGuard: score.ComboGuardCharges += Mathf.Max(1, Mathf.RoundToInt(v)); break;
                case SkillTypes.ScoreMult: score.SkillScoreMultiplier = Mathf.Max(1f, v); break;
                case SkillTypes.SpiritBoost: score.SkillSpiritMultiplier = Mathf.Max(1f, v); break;
            }
            return true;
        }

        public void Tick(float dt, ScoreKeeper score)
        {
            if (!Active) return;
            Remaining -= dt;
            if (Remaining > 0f) return;
            Remaining = 0f;
            score.SkillScoreMultiplier = 1f;
            score.SkillSpiritMultiplier = 1f;
            score.ShieldCharges = 0;
            score.ComboGuardCharges = 0;
        }

        float ActiveValue(string type) => Active && _skill.type == type ? _skill.ValueAt(_awakening) : 0f;

        public float MagnetReach => ActiveValue(SkillTypes.Magnet);
        public float PerfectWindowMultiplier => 1f + _passivePerfect + ActiveValue(SkillTypes.WidePerfect);
        public bool Autopilot => Active && _skill.type == SkillTypes.Autopilot;
        public bool Transmute => Active && _skill.type == SkillTypes.Transmute;
    }
}
