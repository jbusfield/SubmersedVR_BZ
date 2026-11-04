using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SubmersedVR
{
    // BZ's custom TMP build IORs on the PDA map note field (30/10 log,
    // reproduced twice; IL-verified against Unity.TextMeshPro.dll):
    //  - OnPointerDown -> GetCursorIndexFromPosition ->
    //    FindNearestCharacterOnLine IORs when the field has no rendered
    //    characters (click on an empty/stale note field).
    //  - GenerateCaret IORs every frame when m_CaretPosition is out of range
    //    of the rendered characterCount - the field freezes and a full game
    //    exit is required.
    // Our contribution to the desync (Sanitize stripping the game's trailing
    // U+200B from the input buffer) is fixed in TextGlyphFix.IsInputBuffer;
    // these guards are the safety net for the game-side stale/empty textInfo.
    static class InputFieldCaretFix
    {
        // public static in the BZ build, IL-verified to have NO guard: its
        // first instruction is textInfo.lineInfo[line].firstCharacterIndex,
        // and FindNearestLine returns -1 for empty text -> lineInfo[-1] IOR
        // on a click. Only caller is TMP_InputField.OnPointerDown/OnDrag
        // (0 game references). Return -1 ("no character") + skip when there
        // is nothing rendered; untouched (return true) otherwise.
        [HarmonyPatch(typeof(TMP_TextUtilities), "FindNearestCharacterOnLine")]
        static class FindNearestCharacterOnLineGuard
        {
            static bool Prefix(TMP_Text text, ref int __result)
            {
                if (text == null || text.textInfo == null || text.textInfo.characterCount <= 0)
                {
                    __result = -1;
                    return false;
                }
                return true;
            }
        }

        // m_CaretPosition exists in the BZ build (byte-verified); the field
        // can still be null if the TMP version renames it -> never NRE from
        // the guard itself (it runs every rendered frame).
        static readonly FieldInfo CaretField = AccessTools.Field(typeof(TMP_InputField), "m_CaretPosition");

        // private void in the BZ build (string-based patch, 2 params),
        // IL-verified: it has ONLY an upper guard
        // (caretPositionInternal >= characterInfo.Length -> return), so a
        // NEGATIVE caret IORs on characterInfo[caret]. The BZ ClampCaretPos
        // clamps to characterCount-1, which is -1 when nothing is rendered
        // (the source of the caret=-1 seen at open). Clamp the caret into
        // [0, characterCount]; skip the original entirely when nothing is
        // rendered (no caret to draw).
        [HarmonyPatch(typeof(TMP_InputField), "GenerateCaret", new[] { typeof(VertexHelper), typeof(Vector2) })]
        static class GenerateCaretGuard
        {
            static bool Prefix(TMP_InputField __instance)
            {
                if (CaretField == null)
                {
                    return true; // unknown TMP internals: original behavior
                }
                var tc = __instance.textComponent;
                if (tc == null || tc.textInfo == null)
                {
                    return true;
                }
                int cc = tc.textInfo.characterCount;
                if (cc <= 0)
                {
                    return false; // nothing rendered: nothing to draw
                }
                int caret = (int)CaretField.GetValue(__instance);
                if (caret < 0 || caret > cc)
                {
                    CaretField.SetValue(__instance, Mathf.Clamp(caret, 0, cc));
                }
                return true;
            }
        }
    }
}
