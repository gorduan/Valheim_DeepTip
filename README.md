# DeepTip

Explanation windows with depth for Valheim, like the nested tooltips of Paradox games (Crusader Kings III, Victoria 3, Europa Universalis V). Rest the mouse on an item or button: its window opens beside it. Highlighted terms inside open further windows, a click pins a window, and pinned windows contain terms again, so you can read as deep as you like.

DeepTip works on its own and needs only BepInEx. Other mods can add their own explanations, with or without code.

**Every feature can be switched off on its own and adjusted** in `BepInEx/config/gorduan.deeptip.cfg` (or any in-game config menu).

## What it does

- **Valheim's tooltips become DeepTip windows.** Inventory, crafting, skills and every other tooltip open next to their element instead of following the mouse. You can move the mouse into them; they stay open on the way.
- **Highlighted terms.** Known terms in tooltip texts (damage types, status effects, skills, biomes, stats …) are highlighted automatically, in Valheim's texts and in other mods' texts. Hover a term: its explanation opens beside the window. Click: it stays.
- **Valheim glossary included**, in English and German: damage and resistances, blocking and parrying, stamina, eitr, rested and comfort, food, status effects, all skills, all biomes, crafting stations, durability, carry weight, Forsaken powers, taming and breeding, and help for DeepTip itself.
- **Lexicon in the Compendium.** A searchable page lists every entry; terms there open windows as well.
- **Your language.** DeepTip follows Valheim's language. Entries missing in it come from English (then German) and show a small language badge.
- **Works with EpicLoot.** EpicLoot's item tooltips (scrolling, magic shimmer, comparison) are kept and become DeepTip windows like all others.

## Controls

| Action | Mouse and keyboard |
|---|---|
| Open the window of an element | rest the mouse on it (0.5 s), or `F1` (opens it pinned) |
| Preview a term | move the mouse over it |
| Pin a window | click a term, click inside the window, or the pin button |
| Close the newest window | `Esc` (Valheim's menus react to Esc only after the last pinned window is closed) |
| Close all windows | `Shift+Esc` or hold `Esc` |
| Scroll a long window | mouse wheel over it |

On the way into a window it stays open while the mouse moves towards it, and terms you cross on the way do not replace it. Gamepad and touch keep Valheim's normal tooltips in this version.

## Settings

| Setting | Default | Meaning |
|---|---|---|
| General / Enabled | on | DeepTip on or off |
| General / ValheimTooltips | on | Valheim's tooltips become DeepTip windows (off: vanilla tooltips) |
| General / AutoLink | on | highlight known terms automatically |
| Timing / Delay | 0.5 s | rest time on an element |
| Timing / LinkDelay | 0.08 s | rest time on a term |
| Timing / CloseDelay | 0.3 s | grace before an unpinned window closes |
| Timing / LockAfter | 0 (off) | timer lock: resting inside a window pins it |
| Windows / CloseButton, PinButton, Hints, ClickInsidePins | on | window buttons and behaviour |
| Windows / MaxWidth | 420 | widest window |
| Windows / RootSides, TooltipSides, ChildSides | | where windows open, in order of preference |
| Keys / Open, CloseAll | F1, Shift+Esc | keys |
| Lexicon / Enabled, First | on, on | the lexicon page and its position in the Compendium |
| Colors / Link, Pinned | gold | colors of terms and pinned frames |

Your own texts: put glossary files into `BepInEx/config/DeepTip/` (for example `de.json`); they override every other source.

## For mod authors

### Without code

Ship one JSON file per language in a folder named `deeptip` inside your plugin folder, for example `BepInEx/plugins/You-YourMod/deeptip/en.json` and `de.json`. DeepTip loads every `deeptip` folder at start.

```json
{
  "$locale": "en",
  "yourmod.frostbite": {
    "title": "Frostbite",
    "short": "Cold damage over time from the YourMod frost axe.",
    "body": "Deals [[valheim.damage.frost|frost damage]] every second for 5 s. **Fire** removes it.\n\nSee also [[yourmod.frostaxe]].",
    "aliases": ["Frostbite", "frostbitten"]
  }
}
```

- Keys are dotted, language-neutral names; start them with your mod's name.
- `title` names the term, `short` is one line, `body` is the explanation (2–5 sentences).
- `aliases` are the words as they appear in game texts; DeepTip highlights their first occurrence in every tooltip.
- Markup: `[[key]]`, `[[key|text]]`, `**bold**`, `*italic*`, `` `code` ``, `{live_value}`, lines starting with `- `, a blank line starts a paragraph. Never HTML.
- Write `[[yourmod.frostbite]]` into your own item descriptions or tooltips: it becomes a term there too.
- Link to DeepTip's Valheim entries freely, e.g. `[[valheim.status.wet]]` (see the lexicon for all keys).

The full format is in the DeepTip specification (SPEC.md in the source repository); its validator (`tools/deeptip-check.mjs`, Node 18+) checks your files.

### With code

Reference `GC_Valheim_DeepTip.dll` (or call it via reflection) and add `[BepInDependency("gorduan.deeptip", BepInDependency.DependencyFlags.SoftDependency)]`.

```csharp
DeepTip.Api.Register("YourMod", jsonText);              // a glossary from code
DeepTip.Api.RegisterFolder("YourMod", folder);          // all *.json in a folder
DeepTip.Api.Mark(myButton, "yourmod.frostbite");        // resting on your UI element opens the window
DeepTip.Api.SetValue("frost_stacks", () => stacks.ToString()); // {frost_stacks} in texts
DeepTip.Api.Refresh();                                  // re-render open windows after values changed
string tmp = DeepTip.Api.Render("Deals [[valheim.damage.frost]]."); // TextMeshPro rich text with terms
DeepTip.Api.AttachLinks(myTmpText);                     // terms in your own panel open windows
DeepTip.Api.Open("yourmod.frostbite", someRectTransform, pinned: true);
```

Events: `Api.Opened`, `Api.Closed`, `Api.Pinned` (key), `Api.Changed` (glossary or language). Everything is safe to call on a dedicated server (no windows there).

## License

MIT. Source code: https://github.com/gorduan/DeepTip. Gorduan Community (GC): free for anyone to use, modify and re-upload.

Author: Gorduan.
