﻿using PacManBot.Extensions;

namespace PacManBot.Games.Concrete.Rpg.Skills
{
    public class SiphonStrike : Skill
    {
        public override string Name => "Siphon Strike";
        public override string Description => "Attack a random enemy and steal 300% HP.";
        public override string Shortcut => "siphon";
        public override int ManaCost => 3;
        public override SkillType Type => SkillType.Def;
        public override int SkillGet => 15;

        public override string Effect(RpgGame game)
        {
            bool crit = Program.Random.NextDouble() < game.player.CritChance;
            int dmg = Entity.ModifiedDamage(game.player.Damage, crit);

            var target = Program.Random.Choose(game.Opponents);

            string effectMessage = game.player.weapon.GetWeapon().AttackEffects(game.player, target);
            int dealt = target.Hit(dmg, game.player.DamageType, game.player.MagicType);
            int heal = game.IsPvp ? dealt : dealt * 3;
            game.player.Life += heal;
            return $"{this} dealt {dealt} damage to {target}{" (!)".If(crit)} and siphoned {heal} HP!\n{effectMessage}";
        }
    }
}
