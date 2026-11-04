using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SubmersedVR
{
    // The BZ FR localization contains characters the game font atlases may
    // lack: the ARABIC PERCENT SIGN (U+066A, the .NET fr-FR PercentSymbol -
    // verified by hexdump of the runtime string "Alimentation : 83 ٪"),
    // œ/Œ ("manœuvrer"), subscript digits for chemical formulas (H₂O...),
    // and a few others. A character missing from every font atlas renders as
    // "?" (the font's .notdef glyph). Sanitize at the central text entry
    // points: the font atlases are audited at runtime and ONLY the characters
    // actually missing get replaced, so official text is left untouched when
    // the fonts do cover it.
    static class TextGlyphFix
    {
        // Applied only when the character is absent from every loaded atlas
        static readonly Dictionary<char, string> Replacements = new Dictionary<char, string>
        {
            { '\u0153', "oe" }, { '\u0152', "OE" },
            { '\u2080', "0" }, { '\u2081', "1" }, { '\u2082', "2" }, { '\u2083', "3" },
            { '\u2084', "4" }, { '\u2085', "5" }, { '\u2086', "6" }, { '\u2087', "7" },
            { '\u2088', "8" }, { '\u2089', "9" },
            { '\u2074', "4" }, { '\u00BC', "1/4" },
            { '\u2013', "-" }, { '\u2022', "-" },
        };

        static HashSet<uint> atlasChars = new HashSet<uint>();
        static bool auditDone;
        static int callsSinceAudit;
        static readonly HashSet<char> loggedMissing = new HashSet<char>();

        // The input field manages its own text buffer + caret: the game (FR
        // localization) appends a trailing U+200B to the typed text and
        // sanitizing it here CHANGES THE LENGTH after TMP_InputField already
        // processed the value -> m_CaretPosition desyncs from the rendered
        // characterCount -> GenerateCaret IOR every frame and the PDA field
        // freezes (30/10 log: 'm\u200B' -> 'm' on every keystroke). Skip the
        // input field's text component: the ZWSP is zero-width (invisible)
        // and buffer/rendered stay consistent. The placeholder is a separate
        // TMP_Text instance and stays sanitized.
        static bool IsInputBuffer(TMP_Text inst)
        {
            if (inst == null)
            {
                return false;
            }
            var input = inst.GetComponentInParent<TMP_InputField>();
            return input != null && input.textComponent == inst;
        }

        [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.text), MethodType.Setter)]
        static class TmpText
        {
            static void Prefix(TMP_Text __instance, ref string value)
            {
                if (IsInputBuffer(__instance))
                {
                    return;
                }
                value = Sanitize(value);
            }
        }

        [HarmonyPatch(typeof(Text), nameof(Text.text), MethodType.Setter)]
        static class UIText
        {
            static void Prefix(Text __instance, ref string value)
            {
                value = Sanitize(value);
            }
        }

        // SetText writes the TMP backing field directly and does not go
        // through the `text` setter: without these patches, a percent sign
        // formatted by SetText slips past the fix. NOTE: Harmony matches ref
        // parameters BY NAME against the target - the real TMP signature is
        // SetText(string sourceText, bool syncTextInputBox), so the prefix
        // parameter MUST be named sourceText (a "text" name made the patch
        // fail silently at runtime, 2026-10-18).
        [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(string), typeof(bool) })]
        static class TmpSetText
        {
            static void Prefix(TMP_Text __instance, ref string sourceText)
            {
                if (IsInputBuffer(__instance))
                {
                    return;
                }
                sourceText = Sanitize(sourceText);
            }
        }

        static readonly HashSet<string> loggedPercentSamples = new HashSet<string>();

        static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }
            if (Settings.IsDebugEnabled && value.IndexOf('\u066A') >= 0)
            {
                LogPercentSample(value);
            }
            // The Arabic percent is never wanted in the UI: replaced
            // unconditionally, audit or not
            if (value.IndexOf('\u066A') >= 0)
            {
                value = value.Replace('\u066A', '%');
            }
            AuditIfDue();
            if (!auditDone)
            {
                return value;
            }
            bool changed = false;
            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (ch <= 0x7E || atlasChars.Contains(ch))
                {
                    sb.Append(ch);
                    continue;
                }
                string repl;
                if (Replacements.TryGetValue(ch, out repl))
                {
                    sb.Append(repl);
                }
                else
                {
                    LogMissing(ch, value); // no known replacement: drop it
                }
                changed = true;
            }
            return changed ? sb.ToString() : value;
        }

        // Retried (fonts load over time) until at least one atlas is found
        static void AuditIfDue()
        {
            if (auditDone)
            {
                return;
            }
            callsSinceAudit++;
            if (callsSinceAudit < 100)
            {
                return;
            }
            callsSinceAudit = 0;
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            var union = new HashSet<uint>();
            foreach (var f in fonts)
            {
                if (f == null || f.characterTable == null)
                {
                    continue;
                }
                foreach (var c in f.characterTable)
                {
                    union.Add(c.unicode);
                }
            }
            if (union.Count < 100)
            {
                return; // no atlas loaded yet
            }
            atlasChars = union;
            auditDone = true;
            if (Settings.IsDebugEnabled)
            {
                Mod.logger.LogInfo($"[TextGlyphFix] audit: {fonts.Length} font assets, {union.Count} unique chars");
                foreach (var kv in Replacements)
                {
                    bool has = union.Contains(kv.Key);
                    Mod.logger.LogInfo($"[TextGlyphFix] U+{((int)kv.Key):X4}: {(has ? "in atlases, kept" : "missing -> '" + kv.Value + "'")}");
                }
            }
        }

        // Temporary diagnostic (2026-10-18): one log per unique string that
        // carries an Arabic percent, with every non-ASCII code point and the
        // call path (which patched entry point delivered it). Settles the
        // "ion cube charge still shows ?" report: if this never logs on
        // hovering the item, the charge text bypasses all patched entry
        // points; if it logs, the ? is a different character (see the cps).
        static void LogPercentSample(string value)
        {
            string sample = value.Replace("\r", "").Replace("\n", "\\n");
            if (sample.Length > 90)
            {
                sample = sample.Substring(0, 90) + "...";
            }
            if (!loggedPercentSamples.Add(sample))
            {
                return;
            }
            var cps = new List<string>();
            foreach (var ch in value)
            {
                if (ch > 0x7E)
                {
                    cps.Add("U+" + ((int)ch).ToString("X4"));
                }
            }
            string stack = Environment.StackTrace
                .Replace("\r", "").Replace("\n", " | ")
                .Substring(0, Math.Min(400, Environment.StackTrace.Length));
            Mod.logger.LogInfo($"[TextGlyphFix] 066A sample: \"{sample}\" cps=[{string.Join(",", cps)}]");
            Mod.logger.LogInfo($"[TextGlyphFix] 066A stack: {stack}");
        }

        // One log line per character so the trail stays readable
        static void LogMissing(char ch, string value)
        {
            if (!Settings.IsDebugEnabled || !loggedMissing.Add(ch))
            {
                return;
            }
            string sample = value.Replace("\r", "").Replace("\n", "\\n");
            if (sample.Length > 60)
            {
                sample = sample.Substring(0, 60) + "...";
            }
            Mod.logger.LogInfo($"[TextGlyphFix] U+{((int)ch):X4} in no atlas, dropped from: \"{sample}\"");
        }
    }
}
