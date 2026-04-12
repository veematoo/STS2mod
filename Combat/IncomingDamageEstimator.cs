using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace PathPlanner.Combat;

/// <summary>
/// Estimates local player HP after upcoming blockable hits and known debuff damage.
/// Ordering is approximate (Plating/Frost → Constrict → enemy attacks → Disintegration → poison).
/// </summary>
internal static class IncomingDamageEstimator
{
    internal static int? TryGetProjectedHpAfterUpcomingDamage(Creature local, CombatState? state)
    {
        if (state == null || local.Player == null || !LocalContext.IsMe(local.Player))
            return null;
        if (!local.IsAlive || local.ShowsInfiniteHp)
            return null;

        int block = local.Block + ProjectedPlatingBlock(local) + ProjectedFrostBlock(local.Player);
        int hp = local.CurrentHp;

        if (local.GetPower<ConstrictPower>() is { } constrict)
            ApplyBlockableHit(ref hp, ref block, constrict.Amount, local, dealer: null);

        IEnumerable<Creature> playerTargets = state.Players.Select(p => p.Creature).ToArray();

        foreach (Creature enemy in state.HittableEnemies)
        {
            if (enemy.Monster?.NextMove.Intents == null)
                continue;

            foreach (AbstractIntent intent in enemy.Monster.NextMove.Intents)
            {
                if (intent is not AttackIntent attack)
                    continue;

                int single = attack.GetSingleDamage(playerTargets, enemy);
                int repeats = Math.Max(1, attack.Repeats);
                for (int i = 0; i < repeats; i++)
                    ApplyBlockableHit(ref hp, ref block, single, local, enemy);
            }
        }

        if (local.GetPower<DisintegrationPower>() is { } dis)
            ApplyBlockableHit(ref hp, ref block, dis.Amount, local, dealer: null);

        if (local.GetPower<PoisonPower>() is { } poison)
        {
            int p = poison.CalculateTotalDamageNextTurn();
            if (p > 0)
                ApplyUnblockableHpLoss(ref hp, p, local, dealer: null);
        }

        return Math.Clamp(hp, 0, local.MaxHp);
    }

    private static int ProjectedPlatingBlock(Creature local)
    {
        // PlatingPower grants block on BeforeTurnEndEarly for the owner's side (player).
        int sum = 0;
        foreach (var plating in local.GetPowerInstances<PlatingPower>())
            sum += plating.Amount;
        return sum;
    }

    private static int ProjectedFrostBlock(Player player)
    {
        var q = player.PlayerCombatState?.OrbQueue;
        if (q == null)
            return 0;

        int sum = 0;
        foreach (OrbModel orb in q.Orbs)
        {
            if (orb is FrostOrb frost)
                sum += (int)Math.Round(frost.PassiveVal);
        }

        return sum;
    }

    private static void ApplyBlockableHit(ref int hp, ref int block, int raw, Creature self, Creature? dealer)
    {
        if (raw <= 0)
            return;

        int blocked = Math.Min(block, raw);
        block -= blocked;
        int through = raw - blocked;
        through = ApplyIntangibleCap(through, self, dealer);
        hp -= through;
    }

    private static void ApplyUnblockableHpLoss(ref int hp, int raw, Creature self, Creature? dealer)
    {
        if (raw <= 0)
            return;
        hp -= ApplyIntangibleCap(raw, self, dealer);
    }

    /// <summary>Matches <see cref="IntangiblePower"/> damage cap vs <see cref="TheBoot"/>.</summary>
    private static int ApplyIntangibleCap(int damage, Creature self, Creature? dealer)
    {
        if (damage <= 0 || !self.HasPower<IntangiblePower>())
            return damage;

        int cap = GetIntangibleCap(dealer);
        return Math.Min(damage, cap);
    }

    private static int GetIntangibleCap(Creature? dealer)
    {
        Player? p = dealer?.Player ?? dealer?.PetOwner;
        if (p == null || !p.Relics.Any(static r => r is TheBoot))
            return 1;
        return 5;
    }
}
