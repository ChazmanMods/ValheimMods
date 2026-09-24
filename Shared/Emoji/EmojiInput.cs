using TMPro;
using UnityEngine;

namespace Runic.Shared;

internal sealed class EmojiInput : MonoBehaviour
{
    private TMP_InputField _input;
    internal static void Attach(TMP_InputField input)
    {
        EmojiRenderer.Attach(input.textComponent);
        input.richText = false;
        input.resetOnDeActivation = false;
        input.onFocusSelectAll = false;
        input.gameObject.AddComponent<EmojiInput>()._input = input;
    }

    // TMP inserts a pasted surrogate pair one UTF-16 unit at a time. Repair only after
    // the input event completes, so the first half is not removed before the second arrives.
    private void LateUpdate()
    {
        if (!_input || !string.IsNullOrEmpty(Input.compositionString)) return;
        var value = EmojiText.Clean(_input.text, _input.characterLimit > 0 ? _input.characterLimit : int.MaxValue);
        if (value == _input.text) return;
        int caret = EmojiText.Clean(_input.text.Substring(0, Mathf.Min(_input.stringPosition, _input.text.Length)), value.Length).Length;
        _input.text = value;
        _input.stringPosition = caret;
    }
}
