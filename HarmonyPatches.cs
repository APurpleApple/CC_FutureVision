using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Serialization;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Myosotis.DeepCopy;
using Nanoray.PluginManager;
using static OneOf.Types.TrueFalseOrNull;
namespace APurpleApple.FutureVision
{
    [HarmonyPatch]
    public static class HarmonyPatches
    {
        static State? nextTurnState;
        static State? beforeEnemyTurnState;
        static State? afterDroneState;

        static PFXState pfx = new();
        static bool isSimulating = false;
        static bool isSimulationDirty = true;

        static Box? hoveredBox = null;

        static bool isSimulationSuccessful = false;
        public static bool isPreviewEnabled = true;

        static bool isActionRunning = false;
        static bool isPreviewingCard = false;

        static RenderTarget2D? previewTexture = null; 

        [HarmonyPatch(typeof(Audio), nameof(Audio.Play), [typeof(FMOD.GUID?), typeof(bool)]), HarmonyPrefix]
        public static bool CutAudio()
        {
            return !isSimulating;
        }
        [HarmonyPatch(typeof(Audio), nameof(Audio.Play), [typeof(string), typeof(bool)]), HarmonyPrefix]
        public static bool CutAudioString()
        {
            return !isSimulating;
        }

        [HarmonyPatch(typeof(Glow), nameof(Glow.Draw), [typeof(Vec), typeof(double), typeof(Color)]), HarmonyPrefix]
        public static bool CutGlow()
        {
            return !isSimulating;
        }

        [HarmonyPatch(typeof(Glow), nameof(Glow.Draw), [typeof(Vec), typeof(Vec), typeof(Color)]), HarmonyPrefix]
        public static bool CutGlowAlt()
        {
            return !isSimulating;
        }

        [HarmonyPatch(typeof(G), nameof(G.Render)), HarmonyPrefix]
        public static void DrawToPreviewTexture(G __instance)
        {
            G g = __instance;
            //if (isSimulationDirty) return;
            if (g.state == null) return;
            if (nextTurnState == null || beforeEnemyTurnState == null) return;
            if (g.state.route is not Combat c) return;
            if (nextTurnState.route is not Combat cs) return;
            if (beforeEnemyTurnState.route is not Combat combatPreview) return;
            if (!c.PlayerCanAct(g.state)) return;
            if (previewTexture == null) return;
            //if (g.state.ship.x == nextTurnState.ship.x) return;

            State s = g.state;

            Matrix oldMatrix = MG.inst.cameraMatrix;
            MG.inst.cameraMatrix = Matrix.Identity;
            MG.inst.GraphicsDevice.SetRenderTarget(previewTexture);
            Draw.StartAutoBatchFrame();
            
            MG.inst.GraphicsDevice.Clear(Microsoft.Xna.Framework.Color.Transparent);

            combatPreview.hoveringDeck = null;

            Rect rect = Combat.marginRect;

            combatPreview.camX = c.camX;
            bool noInput = combatPreview.routeOverride != null;
            g.Push(null, rect, null, autoFocus: false, noHoverSound: false, gamepadUntargetable: false, ReticleMode.Quad, null, null, null, null, 0, null, null, null, null, noInput);
            isSimulating = true;
            g.state = beforeEnemyTurnState;
            beforeEnemyTurnState.ship.xLerped = beforeEnemyTurnState.ship.x;
            combatPreview.RenderShipsUnder(g);
            combatPreview.RenderShipsOver(g);
            g.state = s;
            g.Pop();

            Rect? r = Combat.marginRect + Combat.arenaPos + c.GetCamOffset() ;
            g.Push(null, r);
            foreach (var item in combatPreview.stuff)
            {
                Vec loc = g.Peek(item.Value.GetGetRect()).xy;
                item.Value.Render(g, loc);

                StuffBase? afterTurnEnd;
                if (cs.stuff.TryGetValue(item.Key, out afterTurnEnd) && afterTurnEnd != null && afterTurnEnd.GetType() == item.Value.GetType()) continue;
                if (c.stuff.TryGetValue(item.Key, out afterTurnEnd) && afterTurnEnd != null && afterTurnEnd.GetType() == item.Value.GetType()) continue;
                Draw.Sprite(SSpr.icons_heat_warning, loc.x + 2, loc.y + 10, color: Colors.white.fadeAlpha(Math.Abs(Math.Sin(g.time * 3))));
            }

            foreach (var item in c.stuff)
            {
                Vec loc = g.Peek(item.Value.GetGetRect()).xy;

                StuffBase? afterTurnEnd;
                if (cs.stuff.TryGetValue(item.Key, out afterTurnEnd) && afterTurnEnd != null && afterTurnEnd.GetType() == item.Value.GetType()) continue;
                Draw.Sprite(SSpr.icons_heat_warning, loc.x + 2, loc.y + 10, color: Colors.white.fadeAlpha(Math.Abs(Math.Sin(g.time * 3))));
            }

            g.Pop();
            isSimulating = false;
            Draw.EndAutoBatchFrame();
            MG.inst.GraphicsDevice.SetRenderTarget(null);
            MG.inst.cameraMatrix = oldMatrix;
        }

        [HarmonyPatch(typeof(Combat), nameof(Combat.RenderShipsUnder)), HarmonyPrefix]
        public static void RenderPreviewTexture(Combat __instance, G g)
        {
            if (!isPreviewEnabled) return;
            if (isSimulationDirty) return;
            Combat c = __instance;
            if (!c.PlayerCanAct(g.state)) return;
            //if (g.state.ship.x == nextTurnState.ship.x) return;


            Draw.SetBlendMode(BlendState.AlphaBlend, null, null);
            MG.inst.sb.Draw(previewTexture, G.screenRect.ToMgRect(), Colors.healthBarShield.fadeAlpha(.5).ToMgColor());
        }

        [HarmonyPatch(typeof(Combat), nameof(Combat.RenderMoveButtons)), HarmonyPostfix]
        public static void RenderEyeButton(Combat __instance, G g)
        {
            UIKey uIKey = new UIKey(UK.eyeball, 0, "FuturePreview");
            Rect rect = new Rect(Combat.cardCenter.x + 213, 150.0, 19.0, 20.0);
            UIKey key = uIKey;
            OnMouseDown onMouseDown = PMod.Instance;
            bool showAsPressed = false;
            string button_sprite = isPreviewEnabled ? (isSimulationSuccessful ? "button_vis" : "button_err") : "button_dis";
            SharedArt.ButtonResult buttonResult = SharedArt.ButtonSprite(g, rect, key, PMod.sprites[button_sprite].Sprite, PMod.sprites[button_sprite +"_on"].Sprite, null, null, inactive: false, flipX: true, flipY: false, onMouseDown, autoFocus: false, noHover: false, showAsPressed, gamepadUntargetable: true);
        }

        [HarmonyPatch(typeof(Combat), nameof(Combat.RenderDrones)), HarmonyPostfix]
        public static void RenderMidrowPreview(Combat __instance, G g)
        {
            if (!isPreviewEnabled) return;
            if (isSimulationDirty) return;
            if (nextTurnState == null) return;
            if (beforeEnemyTurnState == null) return;
            Combat c = __instance;
            if (!c.PlayerCanAct(g.state)) return;
            if (nextTurnState.route is not Combat cs) return;

            Rect? rect = default(Rect) + Combat.arenaPos + c.GetCamOffset();
            
            g.Push(null, rect);

            foreach (var item in c.stuff)
            {
                Vec loc = g.Peek(item.Value.GetGetRect()).xy;

                StuffBase? afterTurnEnd;
                if (cs.stuff.TryGetValue(item.Key, out afterTurnEnd) && afterTurnEnd != null && afterTurnEnd.GetType() == item.Value.GetType()) continue;
                Draw.Sprite(SSpr.icons_heat_warning, loc.x + 2, loc.y + 10, color: Colors.white.fadeAlpha(Math.Abs(Math.Sin(g.time * 3))));
            }

            g.Pop();
        }

        [HarmonyPatch(typeof(Combat), nameof(Combat.Update)), HarmonyPrefix]
        public static void SetDirtyOnAction(Combat __instance, G g)
        {
            if (isSimulating) return;

            if (__instance.currentCardAction != null || __instance.cardActions.Count > 0)
            {
                isActionRunning = true;
            }
            else if (isActionRunning)
            {
                isActionRunning = false;
                isSimulationDirty = true;
            }

            
        }

        [HarmonyPatch(typeof(G), nameof(G.UIAfterFrame)), HarmonyPrefix]
        public static void SetDirtyOnHover(G __instance)
        {
            if (__instance.hoverKey != hoveredBox?.key)
            {
                Box? box = __instance.boxes.FirstOrDefault(b => b.key.Equals(__instance.hoverKey));
                if (box?.onMouseDown != null || hoveredBox?.onMouseDown != null)
                {
                    isSimulationDirty = true;
                }
                hoveredBox = box;
            }
        }

        [HarmonyPatch(typeof(State), nameof(State.ChangeRoute)), HarmonyPostfix]
        public static void RemovePreviewOnRouteChnge()
        {
            nextTurnState = null;
            beforeEnemyTurnState = null;
            afterDroneState = null;
            isSimulationDirty = true;
        }

        [HarmonyPatch(typeof(Combat), nameof(Combat.Update)), HarmonyPostfix]
        public static void UpdateSimulation(Combat __instance, G g)
        {
            if (!isPreviewEnabled) return;
            if (isSimulating) return;
            if (isSimulationDirty && __instance.PlayerCanAct(g.state))
            {
                SimulateFuture(g);
            }
        }

        [HarmonyPatch(typeof(Ship),nameof(Ship.RenderHealthBar)), HarmonyPostfix]
        public static void DrawHealthPreview(Ship __instance, G g, bool isPreview)
        {
            if (!isPreviewEnabled) return;
            if (isPreview) return;
            if (g.state.route is not Combat c) return;
            if (!c.PlayerCanAct(g.state)) return;

            if (nextTurnState == null) return;
            if (beforeEnemyTurnState == null) return;
            if (nextTurnState.route is not Combat cn) return;
            if (beforeEnemyTurnState.route is not Combat cb) return;

            int shield = __instance.Get(Status.shield);
            int tempShield = __instance.Get(Status.tempShield);
            int maxShield = __instance.GetMaxShield();
            int hullPlusMaxShield = __instance.hullMax + maxShield;
            int fullBarLength = hullPlusMaxShield + tempShield;
            int shipWidth = 16 * (__instance.parts.Count + 2);
            int chunkWidth = Mutil.Clamp(shipWidth / hullPlusMaxShield, 2, 4) - 1;
            int chunkMargin = 1;
            int healthChunkHeight = 5;
            int shieldChunkHeight = 3;

            int shieldPreview = shield;
            int tempShieldPreview = tempShield;
            int healthPreview = __instance.hull;
            int tempShieldAtTurnEnd = 0;
            int shieldAtTurnEnd = 0;
            int hullAtTurnEnd = 0;
            int maxShieldAtTurnEnd = maxShield;

            bool isPlayer = __instance == g.state.ship;

            if (isPlayer)
            {
                maxShieldAtTurnEnd = nextTurnState.ship.GetMaxShield();
                healthPreview = nextTurnState.ship.hull;
                shieldPreview = nextTurnState.ship.Get(Status.shield);
                tempShieldPreview = nextTurnState.ship.Get(Status.tempShield);
                tempShieldAtTurnEnd = beforeEnemyTurnState.ship.Get(Status.tempShield);
                shieldAtTurnEnd = beforeEnemyTurnState.ship.Get(Status.shield);
                hullAtTurnEnd = beforeEnemyTurnState.ship.hull;
            }
            else
            {
                Ship enemyNextTurn = cn.otherShip;
                Ship enemyBeforeTurnEnd = cb.otherShip;
                healthPreview = isPreviewingCard ? enemyBeforeTurnEnd.hull : enemyNextTurn.hull;
                shieldPreview = isPreviewingCard ? enemyBeforeTurnEnd.Get(Status.shield) : enemyNextTurn.Get(Status.shield);
                tempShieldPreview = isPreviewingCard ? enemyBeforeTurnEnd.Get(Status.tempShield) : enemyNextTurn.Get(Status.tempShield);
                tempShieldAtTurnEnd = isPreviewingCard ? enemyBeforeTurnEnd.Get(Status.tempShield) : enemyNextTurn.Get(Status.tempShield);
            }

            Vec vec = new Vec(hullPlusMaxShield * chunkWidth + (hullPlusMaxShield - 1) * chunkMargin + 2, shieldChunkHeight + 2);
            Vec vec2 = new Vec(fullBarLength * chunkWidth + (fullBarLength - 1) * chunkMargin + 2, shieldChunkHeight + 2);
            Vec v = g.Peek(new Rect(-Math.Round(vec.x / 2.0), 0, vec2.x, healthChunkHeight + 3)).xy;


            int totalShieldAtTurnEnd = maxShieldAtTurnEnd + tempShieldAtTurnEnd;
            double x = v.x + fullBarLength * chunkWidth + (fullBarLength - 1) * chunkMargin + 1;

            int diff = totalShieldAtTurnEnd - (maxShield + tempShield);
            if (diff != 0)
            {
               Vec shieldBarSize = new Vec(diff * chunkWidth + (diff - 1) * chunkMargin + 2, shieldChunkHeight + 2);
               Draw.Rect(x+1, v.y - 2.0, shieldBarSize.x + 1.0, shieldBarSize.y + 4.0, Colors.black.fadeAlpha(0.75));
               Draw.Rect(x+1, v.y - 1.0, shieldBarSize.x, shieldBarSize.y + 2.0, Colors.healthBarBorder);
               Draw.Rect(x+1, v.y, shieldBarSize.x-1, shieldBarSize.y, Colors.healthBarBbg);
            }


            if (healthPreview <= __instance.hull)
            {
                for (int j = healthPreview; j < __instance.hull; j++)
                {
                    DrawChunk(j, healthChunkHeight, Colors.white.fadeAlpha(Math.Abs(Math.Sin(g.time * 3))), j < __instance.hull - 1);
                }
            }
            else
            {
                for (int j = __instance.hull; j < healthPreview; j++)
                {
                    DrawChunk(j, healthChunkHeight, Colors.heal.fadeAlpha(Math.Abs(Math.Sin(g.time * 3))), j < __instance.hull - 1);
                }
            }

            for (int k = shield; k < shieldPreview; k++)
            {
                DrawChunk(__instance.hullMax + k, shieldChunkHeight, Colors.healthBarShield.fadeAlpha(Math.Abs(Math.Sin(g.time * 3))), k < maxShield - 1 && k < shield - 1);
            }

            if (isPlayer)
            {
                for (int k = shieldPreview; k < shieldAtTurnEnd; k++)
                {
                    DrawChunk(__instance.hullMax + k, shieldChunkHeight, Colors.white.fadeAlpha(Math.Abs(Math.Sin(g.time * 3))), k < maxShield - 1 && k < shield - 1);
                }
            }
            else
            {
                for (int k = shieldPreview; k < shield; k++)
                {
                    DrawChunk(__instance.hullMax + k, shieldChunkHeight, Colors.white.fadeAlpha(Math.Abs(Math.Sin(g.time * 3))), k < maxShield - 1 && k < shield - 1);
                }
            }

            /*
            if (shieldPreview <= shield)
            {
                for (int k = shieldPreview; k < shield; k++)
                {
                    DrawChunk(__instance.hullMax + k, shieldChunkHeight, Colors.white.fadeAlpha(Math.Abs(Math.Sin(g.time * 3))), k < maxShield - 1 && k < shield - 1);
                }
            }
            else
            {
                for (int k = shield; k < shieldPreview; k++)
                {
                    DrawChunk(__instance.hullMax + k, shieldChunkHeight, Colors.heal, k < maxShield - 1 && k < shield - 1);
                }
            }*/

            for (int l = tempShield; l < tempShieldAtTurnEnd; l++)
            {
                DrawChunk(__instance.hullMax + maxShield + l, shieldChunkHeight, Colors.healthBarTempShield.fadeAlpha(Math.Abs(Math.Sin(g.time * 3))), l < tempShield - 1 && l < tempShield - 1);
            }

            for (int l = tempShieldPreview; l < tempShield + __instance.ghostTempShield; l++)
            {
                DrawChunk(__instance.hullMax + maxShield + l, shieldChunkHeight, Colors.white.fadeAlpha(Math.Abs(Math.Sin(g.time * 3))), l < tempShield - 1 && l < tempShield - 1);
            }

            
                

            /*
            if (tempShieldPreview <= tempShield)
            {
                for (int l = tempShieldPreview; l < tempShield + __instance.ghostTempShield; l++)
                {
                    DrawChunk(__instance.hullMax + maxShield + l, shieldChunkHeight, Colors.white.fadeAlpha(Math.Abs(Math.Sin(g.time * 3))), l < tempShield - 1 && l < tempShield - 1);
                }
            }
            else
            {
                for (int l = tempShieldPreview; l < tempShield + __instance.ghostTempShield; l++)
                {
                    DrawChunk(__instance.hullMax + maxShield + l, shieldChunkHeight, Colors.healthBarTempShield.fadeAlpha(Math.Abs(Math.Sin(g.time * 3))), l < tempShield - 1 && l < tempShield - 1);
                }
            }*/

            void DrawChunk(int i, int height, Color color, bool rightMargin)
            {
                double num9 = v.x + 1.0 + (double)(i * (chunkWidth + chunkMargin));
                double y = v.y + 1.0;
                Draw.Rect(num9, y, chunkWidth, height, color);
                if (rightMargin)
                {
                    Draw.Rect(num9 + (double)chunkWidth, y, chunkMargin, height, color.fadeAlpha(0.5));
                }
            }
        }

        [HarmonyPatch(typeof(Combat), nameof(Combat.BeginCardAction)), HarmonyPrefix]
        public static bool CutCardActions(CardAction a)
        {
            if (isSimulating)
            {
                foreach (Tuple<Type, Predicate<CardAction>?> item in PMod.Instance.actionsDisallowedFromPreview)
                {
                    if (item.Item1 == a.GetType())
                    {
                        if (item.Item2 == null || item.Item2(a))
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        private static void SimulateFuture(G g)
        {
            isSimulationSuccessful = true;
            isPreviewingCard = false;
            if (previewTexture == null)
            {
                previewTexture = new RenderTarget2D(MG.inst.GraphicsDevice, (int)G.screenSize.x, (int)G.screenSize.y);
            }
            
            State realState = g.state;
            nextTurnState = Mutil.DeepCopy(g.state);

            beforeEnemyTurnState = nextTurnState;

            if (nextTurnState.route is Combat c)
            {
                g.state = nextTurnState;
                uint currentRNG = nextTurnState.rngActions.seed;
                pfx.Save();
                isSimulating = true;

                if (hoveredBox?.onMouseDown != null)
                {
                    FieldInfo[]? fieldPath = DeepCopy.GetReferencePath(realState, hoveredBox.onMouseDown);
                    if (fieldPath != null)
                    {
                        OnMouseDown? interactor = DeepCopy.ReadAtReferencePath(fieldPath, nextTurnState) as OnMouseDown;
                        interactor?.OnMouseDown(g, hoveredBox);
                    }
                }

                CardAction? action = null;
                c.Queue(new AEndTurn() { timer = 0.01});

                foreach (Part p in c.otherShip.parts)
                {
                    if (p.brittleIsHidden)
                    {
                        p.damageModifier = PDamMod.none;
                    }
                }

                foreach (Part p in nextTurnState.ship.parts)
                {
                    if (p.brittleIsHidden)
                    {
                        p.damageModifier = PDamMod.none;
                    }
                }

                while (c.cardActions.Count > 0)
                {
                    if (nextTurnState.rngActions.seed != currentRNG)
                    {
                        nextTurnState = null;
                        beforeEnemyTurnState = null;
                        isSimulationSuccessful = false;
                        break;
                    }
                    if (c.routeOverride != null)
                    {
                        nextTurnState = null;
                        beforeEnemyTurnState = null;
                        isSimulationSuccessful = false;
                        break;
                    }

                    c.Update(g);
                    if (action != c.currentCardAction)
                    {
                        action = c.currentCardAction;
                        if (c.currentCardAction is AAfterPlayerTurn)
                        {
                            beforeEnemyTurnState = Mutil.DeepCopy(nextTurnState);
                        }
                        if (action is AStartPlayerTurn)
                        {
                            break;
                        }
                    }
                }
                isSimulating = false;
                pfx.Load();
                g.state = realState;
            }

            isSimulationDirty = false;
        }

        private class PFXState()
        {
            public ParticleSystem? combatAlpha = new();
            public ParticleSystem? combatAdd = new();
            public Sparks? combatSparks = new();
            public ParticleSystem? combatExplosion = new();
            public ParticleSystem? combatExplosionUnder = new();
            public ParticleSystem? combatExplosionSmoke = new();
            public ParticleSystem? combatExplosionWhiteSmoke = new();
            public ParticleSystem? combatScreenFadeOut = new();
            public ParticleSystem? screenSpaceAdd = new();
            public ParticleSystem? screenSpaceAlpha = new();
            public ParticleSystem? screenSpaceExplosion = new();
            public Sparks? screenSpaceSparks = new();
            public void Save()
            {
                combatAlpha = PFX.combatAlpha;
                combatAdd = PFX.combatAdd;
                combatExplosion = PFX.combatExplosion;
                combatExplosionUnder = PFX.combatExplosionUnder;
                combatExplosionSmoke = PFX.combatExplosionSmoke;
                combatExplosionWhiteSmoke = PFX.combatExplosionWhiteSmoke;
                screenSpaceExplosion = PFX.screenSpaceExplosion;
                screenSpaceAdd = PFX.screenSpaceAdd;
                screenSpaceAlpha = PFX.screenSpaceAlpha;
                combatScreenFadeOut = PFX.combatScreenFadeOut;
                combatSparks = PFX.combatSparks;
                screenSpaceSparks = PFX.screenSpaceSparks;

                PFX.combatAlpha = new();
                PFX.combatAdd = new();
                PFX.combatExplosion = new();
                PFX.combatExplosionUnder = new();
                PFX.combatExplosionSmoke = new();
                PFX.combatExplosionWhiteSmoke= new();
                PFX.screenSpaceExplosion = new();
                PFX.screenSpaceAdd = new();
                PFX.screenSpaceAlpha = new();
                PFX.combatScreenFadeOut= new();
                PFX.combatSparks= new();
                PFX.screenSpaceSparks= new();
            }

            public void Load()
            {
                PFX.combatAlpha = combatAlpha!;
                PFX.combatAdd = combatAdd!;
                PFX.combatExplosion = combatExplosion!;
                PFX.combatExplosionUnder = combatExplosionUnder!;
                PFX.combatExplosionSmoke = combatExplosionSmoke!;
                PFX.combatExplosionWhiteSmoke = combatExplosionWhiteSmoke!;
                PFX.screenSpaceExplosion = screenSpaceExplosion!;
                PFX.screenSpaceAdd = screenSpaceAdd!;
                PFX.screenSpaceAlpha = screenSpaceAlpha!;
                PFX.combatScreenFadeOut = combatScreenFadeOut!;
                PFX.combatSparks = combatSparks!;
                PFX.screenSpaceSparks = screenSpaceSparks!;

                combatAlpha = null;
                combatAdd = null;
                combatExplosion = null;
                combatExplosionUnder = null;
                combatExplosionSmoke = null;
                combatExplosionWhiteSmoke = null;
                screenSpaceExplosion = null;
                screenSpaceAdd = null;
                screenSpaceAlpha = null;
                combatScreenFadeOut = null;
                combatSparks = null;
                screenSpaceSparks = null;
            }
        }
    }
}
