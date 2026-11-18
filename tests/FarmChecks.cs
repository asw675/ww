using System;
using System.Drawing;

namespace TideCombat
{
    // Offline checks for the 4C farm state machine. They drive Farm4C with
    // synthetic HUD/UI states so the flow can be validated without the game.
    internal static class FarmChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        private static FarmUi Ui() { return new FarmUi(); }

        private static HudSnapshot Hud(bool enemy)
        {
            var hud = new HudSnapshot { Valid = true, PartySize = 1 };
            hud.Current = 0;
            hud.Health[0] = 1;
            hud.Fighters[0] = "Carlotta";
            hud.EnemyHealth = enemy;
            hud.Locked = enemy;
            return hud;
        }

        private static FarmDecision Step(Farm4C farm, HudSnapshot hud, FarmUi ui, ref long now, int advance = 300)
        {
            now += advance;
            return farm.Observe(new FarmFrame(hud, hud != null && hud.Valid, ui, new Size(1600, 900),
                new Rectangle(0, 0, 1920, 1080)), now);
        }

        internal static void Signatures()
        {
            foreach (string label in FarmIndicators.All)
                Require(PatternBank.For(label).Length > 0, "缺少签名: " + label);
        }

        internal static void StateMachine()
        {
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            long now = 1000;
            var idle = Ui();
            Step(farm, null, idle, ref now);
            Require(farm.Status.Contains("游戏"), "启动后应等待游戏画面");

            Step(farm, Hud(false), idle, ref now);
            FarmDecision walk = Step(farm, Hud(false), idle, ref now);
            Require(walk.Request == FarmRequest.KeyDown && walk.Key == 'W', "接近阶段应前进");

            var prompt = Ui();
            prompt.FPrompt = true;
            prompt.FBox = new Rectangle(1000, 500, 40, 40);
            FarmDecision press = Step(farm, Hud(false), prompt, ref now);
            Require(press.Request == FarmRequest.Key && press.Key == 'F', "看到 F 提示应按 F");

            var challenge = Ui();
            challenge.Challenge = true;
            bool clicked = false;
            for (int index = 0; index < 12; index++)
                if (Step(farm, Hud(false), challenge, ref now).Request == FarmRequest.Click)
                    clicked = true;
            Require(clicked, "挑战面板应触发点击");

            bool fought = false;
            for (int index = 0; index < 10; index++)
            {
                FarmDecision decision = Step(farm, Hud(true), idle, ref now, 400);
                if (decision.Request == FarmRequest.Battle && decision.Battle.Kind != ActionKind.Wait)
                    fought = true;
            }
            Require(fought, "战斗中应委派战斗动作");

            for (int index = 0; index < 12; index++)
                Step(farm, Hud(false), idle, ref now, 400);
            Require(farm.Kills == 1, "战斗结束应计入一次击杀");

            var f = Ui();
            f.FPrompt = true;
            FarmDecision pickup = Step(farm, Hud(false), f, ref now, 400);
            Require(pickup.Request == FarmRequest.Key && pickup.Key == 'F', "结算后应尝试拾取");
        }

        internal static void TeleportFlow()
        {
            FarmOptions options = FarmOptions.Defaults();
            options.Teleport = true;
            options.TeleportMode = FarmTeleportMode.BossChallenge;
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            long now = 1000;
            Step(farm, Hud(false), Ui(), ref now, 100);
            FarmDecision open = Step(farm, Hud(false), Ui(), ref now, 100);
            Require(open.Request == FarmRequest.Key && open.VirtualKey == 0x71, "自动传送应打开 F2 图鉴");

            var book = Ui();
            book.Book = true;
            Step(farm, Hud(false), book, ref now, 500);
            bool category = false, kind = false;
            for (int index = 0; index < 20; index++)
            {
                FarmDecision decision = Step(farm, Hud(false), book, ref now, 500);
                if (decision.Request != FarmRequest.Click) continue;
                if (Math.Abs(decision.Point.X - 58) < 20) category = true;
                if (Math.Abs(decision.Point.X - 384) < 20) kind = true;
            }
            Require(category, "应点击左侧 Boss 图鉴页签");
            Require(kind, "应点击二级分类页签");
        }

        internal static void ClaimRestart()
        {
            var farm = new Farm4C(CombatSettings.Defaults(), FarmOptions.Defaults());
            long now = 1000;
            Step(farm, null, Ui(), ref now, 100);
            Step(farm, Hud(false), Ui(), ref now, 100);
            for (int index = 0; index < 6; index++)
                Step(farm, Hud(true), Ui(), ref now, 400);
            for (int index = 0; index < 10; index++)
                Step(farm, Hud(false), Ui(), ref now, 400);
            Require(farm.Kills == 1, "应先完成一次击杀");

            var claim = Ui();
            claim.Claim = true;
            Step(farm, Hud(false), claim, ref now, 400);
            FarmDecision escape = Step(farm, Hud(false), claim, ref now, 700);
            Require(escape.Request == FarmRequest.Key && escape.Key == '\u001b', "领奖弹窗应发 ESC");

            var challenge = Ui();
            challenge.Challenge = true;
            bool clicked = false;
            for (int index = 0; index < 10; index++)
                if (Step(farm, Hud(false), challenge, ref now, 400).Request == FarmRequest.Click)
                    clicked = true;
            Require(clicked, "关闭领奖后应重新点击挑战");
        }

        internal static void TeleportBossRow()
        {
            FarmOptions options = FarmOptions.Defaults();
            options.Teleport = true;
            options.BossIndex = 2;
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            long now = 1000;
            Step(farm, Hud(false), Ui(), ref now, 100);
            Step(farm, Hud(false), Ui(), ref now, 100);
            var book = Ui();
            book.Book = true;
            book.BossRows = new System.Collections.Generic.List<Rectangle>
            {
                new Rectangle(1480, 300, 30, 50),
                new Rectangle(1480, 400, 30, 50)
            };
            bool second = false;
            for (int index = 0; index < 16; index++)
            {
                FarmDecision decision = Step(farm, Hud(false), book, ref now, 500);
                if (decision.Request == FarmRequest.Click && decision.Point.Y > 380)
                    second = true;
            }
            Require(second, "应按序号点击第二个 Boss 行");
        }

        internal static void ReviveFlow()
        {
            var options = FarmOptions.Defaults();
            var farm = new Farm4C(CombatSettings.Defaults(), options);
            long now = 1000;
            Step(farm, null, Ui(), ref now, 100);
            Step(farm, Hud(false), Ui(), ref now, 100);
            var dead = Ui();
            dead.Death = true;
            Step(farm, Hud(true), dead, ref now, 100);
            FarmDecision click = Step(farm, Hud(true), dead, ref now, 100);
            Require(click.Request == FarmRequest.Click, "死亡弹窗应点击复活");
            Require(farm.Status.Contains("复活"), "状态应显示等待复活");
        }
    }
}
