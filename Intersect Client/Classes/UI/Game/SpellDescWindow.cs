﻿using System;
using Intersect;
using Intersect.Enums;
using Intersect.GameObjects;
using Intersect.Client.Classes.Localization;
using IntersectClientExtras.File_Management;
using IntersectClientExtras.Gwen.Control;
using Intersect_Client.Classes.General;

namespace Intersect_Client.Classes.UI.Game
{
    public class SpellDescWindow
    {
        ImagePanel mDescWindow;

        public SpellDescWindow(Guid spellId, int x, int y)
        {
            var spell = SpellBase.Lookup.Get<SpellBase>(spellId);
            if (spell == null)
            {
                return;
            }
            mDescWindow = new ImagePanel(Gui.GameUi.GameCanvas, "SpellDescWindow");

            ImagePanel icon = new ImagePanel(mDescWindow, "SpellIcon");

            Label spellName = new Label(mDescWindow, "SpellName");
            spellName.Text = spell.Name;

            Label spellType = new Label(mDescWindow, "SpellType");
            spellType.Text = Strings.SpellDesc.spelltypes[spell.SpellType];

            RichLabel spellDesc = new RichLabel(mDescWindow, "SpellDesc");
            //Load this up now so we know what color to make the text when filling out the desc
            mDescWindow.LoadJsonUi(GameContentManager.UI.InGame);
            if (spell.Desc.Length > 0)
            {
                spellDesc.AddText(Strings.SpellDesc.desc.ToString( spell.Desc), spellDesc.RenderColor);
                spellDesc.AddLineBreak();
                spellDesc.AddLineBreak();
            }

            if (spell.SpellType == (int) SpellTypes.CombatSpell)
            {
                spellType.Text = Strings.SpellDesc.targettypes[spell.TargetType].ToString(spell.CastRange,
                    spell.HitRadius);
            }
            if (spell.CastDuration > 0)
            {
                spellDesc.AddText(Strings.SpellDesc.casttime.ToString( ((float) spell.CastDuration / 10f)),
                    spellDesc.RenderColor);
                spellDesc.AddLineBreak();
                spellDesc.AddLineBreak();
            }
            if (spell.CooldownDuration > 0)
            {
				decimal cdr = 1 - (Globals.Me.GetCooldownReduction() / 100);
				spellDesc.AddText(Strings.SpellDesc.cooldowntime.ToString( ((float) (spell.CooldownDuration * cdr) / 10f)),
                    spellDesc.RenderColor);
                spellDesc.AddLineBreak();
                spellDesc.AddLineBreak();
            }

            bool requirements = (spell.VitalCost[(int) Vitals.Health] > 0 || spell.VitalCost[(int) Vitals.Mana] > 0);

            if (requirements == true)
            {
                spellDesc.AddText(Strings.SpellDesc.prereqs, spellDesc.RenderColor);
                spellDesc.AddLineBreak();
                if (spell.VitalCost[(int) Vitals.Health] > 0)
                {
                    spellDesc.AddText(Strings.SpellDesc.vitalcosts[(int)Vitals.Health].ToString( spell.VitalCost[(int) Vitals.Health]),
                        spellDesc.RenderColor);
                    spellDesc.AddLineBreak();
                }
                if (spell.VitalCost[(int) Vitals.Mana] > 0)
                {
                    spellDesc.AddText(Strings.SpellDesc.vitalcosts[(int)Vitals.Mana].ToString( spell.VitalCost[(int) Vitals.Mana]),
                        spellDesc.RenderColor);
                    spellDesc.AddLineBreak();
                }
                spellDesc.AddLineBreak();
            }

            string stats = "";
            if (spell.SpellType == (int) SpellTypes.CombatSpell)
            {
                stats = Strings.SpellDesc.effects;
                spellDesc.AddText(stats, spellDesc.RenderColor);
                spellDesc.AddLineBreak();

                if (spell.Data3 > 0)
                {
                    spellDesc.AddText(Strings.SpellDesc.effectlist[spell.Data3], spellDesc.RenderColor);
                    spellDesc.AddLineBreak();
                }

                for (var i = 0; i < (int) Vitals.VitalCount; i++)
                {
                    var vitalDiff = spell.VitalDiff?[i] ?? 0;
                    if (vitalDiff == 0) continue;
                    var vitalSymbol = vitalDiff < 0 ? Strings.SpellDesc.addsymbol : Strings.SpellDesc.removesymbol;
                    stats = Strings.SpellDesc.vitals[i].ToString(vitalSymbol, Math.Abs(vitalDiff));
                    spellDesc.AddText(stats, spellDesc.RenderColor);
                    spellDesc.AddLineBreak();
                }

                if (spell.Data2 > 0)
                {
                    for (int i = 0; i < Options.MaxStats; i++)
                    {
                        if (spell.StatDiff[i] != 0)
                        {
                            spellDesc.AddText(Strings.SpellDesc.stats[i].ToString((spell.StatDiff[i] > 0 ? Strings.SpellDesc.addsymbol.ToString() : Strings.SpellDesc.removesymbol.ToString()) + spell.StatDiff[i]), spellDesc.RenderColor);
                            spellDesc.AddLineBreak();
                        }
                    }
                    spellDesc.AddText(Strings.SpellDesc.duration.ToString( (float) spell.Data2 / 10f),
                        spellDesc.RenderColor);
                    spellDesc.AddLineBreak();
                }
            }
            //Load Again for positioning purposes.
            mDescWindow.LoadJsonUi(GameContentManager.UI.InGame);
            icon.Texture = Globals.ContentManager.GetTexture(GameContentManager.TextureType.Spell, spell.Pic);
            spellDesc.SizeToChildren(false, true);
            mDescWindow.SetPosition(x, y);
        }

        public void Dispose()
        {
            if (mDescWindow == null)
            {
                return;
            }
            Gui.GameUi.GameCanvas.RemoveChild(mDescWindow, false);
            mDescWindow.Dispose();
        }
    }
}