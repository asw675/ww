using System;
using System.Drawing;

namespace TideCombat
{
    internal static class TacetRewardApproachChecks
    {
        internal static void UsesQuietCurrentTreasureLocation()
        {
            var hud = new HudSnapshot { Valid = true };
            var ui = new FarmUi { Treasure = true, TreasureBox = new Rectangle(712, 691, 25, 22) };
            Require(DomainMath.CanReapproachTacetReward(hud, ui, verifiedWorld: true),
                "the recorded Tacet chamber can be approached after the old prompt disappears");
            hud.EnemyHealth = true;
            Require(!DomainMath.CanReapproachTacetReward(hud, ui, verifiedWorld: true),
                "a new enemy health bar prevents movement toward the chest");
            hud.EnemyHealth = false;
            ui.Claim = true;
            Require(!DomainMath.CanReapproachTacetReward(hud, ui, verifiedWorld: true),
                "an open reward modal must not cause movement behind it");
            ui.Claim = false;
            ui.OpenWorld = true;
            Require(!DomainMath.CanReapproachTacetReward(hud, ui, verifiedWorld: true),
                "the confirmed open-world HUD does not belong to the Tacet chamber");
            ui.OpenWorld = false;
            Require(!DomainMath.CanReapproachTacetReward(hud, ui, verifiedWorld: false),
                "unverified scenes cannot receive a correction");
            bool centered = false;
            Require(DomainMath.TreasureDirection(ui.TreasureBox, new Size(1600, 900), '\0', ref centered) == 'A',
                "correction uses the actual chest position from the first failure");
        }

        private static void Require(bool condition, string explanation)
        {
            if (!condition) throw new InvalidOperationException(explanation);
        }
    }
}
