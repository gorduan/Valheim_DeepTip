using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeepTip.Core;

// Same cases as test/deeptip.test.mjs for the parser and placement, plus glossary and auto-link tests.
int failed = 0;
int passed = 0;

void Check(bool ok, string name)
{
	if (ok)
	{
		passed++;
	}
	else
	{
		failed++;
		Console.WriteLine("FAIL " + name);
	}
}

void Eq<T>(T actual, T expected, string name) => Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{name}: got <{actual}>, expected <{expected}>");

// --- parseText
{
	var p = Markup.Parse("Frees [[hw.vram]] and [[ai.comfyui|the image server]].")[0].Inline!;
	Eq(p.Count, 5, "links count");
	Eq(p[1].Kind, Markup.Kind.Link, "link kind");
	Eq(p[1].Value, "hw.vram", "link key");
	Check(p[1].LinkText == null, "link without text");
	Eq(p[3].LinkText, "the image server", "link with text");
	Eq(p[4].Value, ".", "trailing text");
}
{
	var p = Markup.Parse("[[not a key]] and [[]]")[0].Inline!;
	Eq(p.Count, 1, "invalid keys stay text");
	Eq(p[0].Value, "[[not a key]] and [[]]", "invalid keys text");
}
{
	var p = Markup.Parse("**bold [[a.b]]** *it **x** ok* `c*d` {vram_free} \\*lit\\* 3 * 4 * 5")[0].Inline!;
	Eq(string.Join(",", p.Select(n => n.Kind)), "Bold,Text,Italic,Text,Code,Text,Var,Text", "inline kinds");
	Eq(p[0].Children![1].Value, "a.b", "link in bold");
	Eq(p[2].Children![1].Kind, Markup.Kind.Bold, "bold in italic");
	Eq(p[4].Value, "c*d", "code");
	Eq(p[6].Value, "vram_free", "var");
	Eq(p[7].Value, " *lit* 3 * 4 * 5", "escapes");
}
{
	var b = Markup.Parse("one\ntwo\n\n- a [[x]]\n- b\ntail\n\n\nlast");
	Eq(string.Join(",", b.Select(x => x.Items != null ? "ul" : "p")), "p,ul,p,p", "blocks");
	Eq(b[0].Inline![1].Kind, Markup.Kind.Br, "line break");
	Eq(b[1].Items!.Count, 2, "list items");
	Eq(b[1].Items![0][1].Value, "x", "link in list");
}
{
	var (links, vars) = Markup.CollectRefs("**[[a]]** - [[b|B]] {v}\n\n- [[c]]");
	Eq(string.Join(",", links), "a,b,c", "collect links");
	Eq(string.Join(",", vars), "v", "collect vars");
}

// --- placement
{
	Box a = new(500, 100, 800, 400);
	Placed p = Placement.Compute(300, 200, a, new[] { "left", "right", "top", "bottom" }, 1000, 800);
	Eq(p.Side, "left", "first side with room");
	Eq(p.X, 500f - 8 - 300, "left x");
	Eq(p.Y, 150f, "centred");
	Check(p.Fits, "fits");
}
{
	Placed p = Placement.Compute(300, 200, new Box(100, 100, 400, 400), new[] { "left", "right", "top", "bottom" }, 1000, 800);
	Eq(p.Side, "right", "no room left");
	Eq(p.X, 408f, "right x");
}
{
	Placed p = Placement.Compute(300, 200, new Box(600, 700, 900, 790), new[] { "left" }, 1000, 800);
	Eq(p.Y, 800f - 8 - 200, "cross axis on screen");
}
{
	Placed p = Placement.Compute(300, 200, new Box(960, 10, 990, 40), new[] { "bottom", "top", "right", "left" }, 1000, 800);
	Eq(p.Side, "bottom", "header button below");
	Eq(p.Y, 48f, "below y");
	Eq(p.X, 1000f - 8 - 300, "clamped right");
}
{
	Placed p = Placement.Compute(340, 260, new Box(8, 150, 367, 450), new[] { "left", "right", "top", "bottom" }, 375, 700);
	Check(!p.Fits, "nothing fits");
	Eq(p.Side, "bottom", "most room");
	Eq(p.MaxH, 700f - 450 - 16, "shrunk");
	Eq(p.Y, 458f, "shrunk y");
}

// --- glossary: flat, nested, meta, problems, fallback chain
{
	var problems = Glossary.Add("test", "{\"$locale\":\"de\",\"$ui\":{\"close\":\"X\"},\"hw.vram\":{\"title\":\"VRAM\"},\"ai\":{\"lora\":{\"title\":\"LoRA\"},\"model\":{\"short\":\"…\"}},\"bad key!\":{\"title\":\"?\"},\"broken\":5}", priority: Glossary.BuiltIn);
	Eq(problems.Count, 2, "glossary problems");
	Glossary.SetLocale("de", new[] { "en" });
	Check(Glossary.Has("ai.lora") && Glossary.Has("ai.model") && !Glossary.Has("bad key!"), "nested entries");
	Eq(Glossary.Ui("close"), "X", "ui override");
	Eq(Glossary.Ui("pin"), "Festhalten", "ui default de");
	Glossary.Add("test2", "{\"hw.vram\":{\"title\":\"Video RAM\"},\"only.en\":{\"title\":\"English only\",\"short\":\"x\"}}", fileName: "mod.en.json");
	Glossary.TryGet("only.en", out Entry e, out string loc);
	Eq(loc, "en", "fallback locale");
	Glossary.TryGet("hw.vram", out e, out loc);
	Eq(e.Title, "VRAM", "first locale wins");
	Glossary.Add("test3", "{\"$locale\":\"de\",\"hw.vram\":{\"title\":\"Grafikspeicher\"}}", priority: Glossary.UserFiles);
	Glossary.TryGet("hw.vram", out e, out _);
	Eq(e.Title, "Grafikspeicher", "later layer overrides");
}

// --- auto-linking inside TMP text
{
	Glossary.Add("auto", "{\"$locale\":\"de\",\"v.slash\":{\"title\":\"Hiebschaden\",\"short\":\"x\",\"aliases\":[\"Hieb\"]},\"v.block\":{\"title\":\"Blockkraft\",\"short\":\"y\",\"aliases\":[\"Blockkraft\",\"Block\"]}}", priority: Glossary.BuiltIn);
	var style = new Markup.Style();
	string src = "<color=orange>Hieb</color>: 30\nBlockkraft: 20 (Hieb)\n<link=\"other\">Block</link> <noparse>Hieb</noparse>";
	string outText = AutoLink.Process(src, true, style);
	Check(outText.Contains("<color=orange><link=\"dt:v.slash\">"), "alias inside color tag linked");
	Eq(outText.Split("dt:v.slash").Length - 1, 1, "first occurrence only");
	Check(outText.Contains("<link=\"dt:v.block\">"), "longest alias wins");
	Check(outText.Contains("<link=\"other\">Block</link>"), "existing links untouched");
	Check(outText.EndsWith("<noparse>Hieb</noparse>"), "noparse untouched");
	string explicitText = AutoLink.Process("Siehe [[v.block|Blocken]] und [[missing.key]].", false, style);
	Check(explicitText.Contains("<link=\"dt:v.block\"><color=#F6C453>Blocken</color></link>"), "explicit markup");
	Check(explicitText.Contains("<color=#9A9A9A>missing.key</color>"), "missing link grey");
	Eq(AutoLink.Process("Hiebe sind keine Hiebschäden", true, style), "Hiebe sind keine Hiebschäden", "whole words only");
}

// --- markup to TMP
{
	var style = new Markup.Style { Value = (name, key) => name == "lvl" ? "42" : null };
	string tmp = Markup.ToTmp("**Stufe** {lvl} {x}\n\n- a <b>\n- [[v.slash]]", "v.block", style);
	Check(tmp.StartsWith("<b>Stufe</b> <color=#9FE0A8>42</color> <color=#9FE0A8>{x}</color>\n\n• <noparse>a <b></noparse>\n• "), "tmp render: " + tmp);
	Check(tmp.EndsWith("• <link=\"dt:v.slash\"><color=#F6C453>Hiebschaden</color></link>"), "tmp list link");
	Check(Markup.ToTmp("[[v.block]]", "v.block", style) == "<b>Blockkraft</b>", "self link bold");
}

// --- real glossary files, if present
foreach (string file in new[] { "../glossary/de.json", "../glossary/en.json" })
{
	string path = Path.Combine(AppContext.BaseDirectory, "../../..", file);
	if (File.Exists(path))
	{
		var problems = Glossary.Add("files", File.ReadAllText(path), fileName: Path.GetFileName(path), priority: Glossary.BuiltIn);
		Check(problems.Count == 0, Path.GetFileName(path) + ": " + string.Join("; ", problems));
	}
}

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
