using System;
using System.Globalization;
using Firebot.UI.Config;
using UnityEngine;
using UnityEngine.UI;

namespace Firebot.UI.Widgets;

internal sealed class PreciseIntegerRow : UiRow
{
    public override float Height => 76f;

    public PreciseIntegerRow(ConfigEntry config, RectTransform rowRect) : base(config, rowRect)
    {
        var label = BuildLabel();
        label.rectTransform.anchorMin = new Vector2(0f, 1f);
        label.rectTransform.anchorMax = new Vector2(LabelRatio, 1f);
        label.rectTransform.offsetMin = new Vector2(UiTheme.RowInsetLeft, -UiTheme.RowHeight);
        label.rectTransform.offsetMax = new Vector2(-UiTheme.RowInsetRight, 0f);

        var background = UiFactory.CreateImage("input", RowRect, UiTheme.Button, true);
        var rect = background.rectTransform;
        rect.anchorMin = new Vector2(LabelRatio + 0.03f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = new Vector2(0f, -UiTheme.InputHeight - 7f);
        rect.offsetMax = new Vector2(-UiTheme.InputInsetRight, -7f);

        var viewport = UiFactory.CreateNode("textArea", rect);
        UiFactory.Stretch(viewport, UiTheme.InputTextInset);
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = UiFactory.CreateLabel(viewport, config.ReadInt().ToString(CultureInfo.InvariantCulture),
            UiTheme.LabelFontSize, UiTheme.Text, TextAlignmentOptions.Left);
        UiFactory.Stretch(text.rectTransform);

        var input = background.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = viewport;
        input.textComponent = text;
        if (UiTheme.Font != null) input.fontAsset = UiTheme.Font;
        input.pointSize = UiTheme.LabelFontSize;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.characterLimit = 10;
        input.SetTextWithoutNotify(config.ReadInt().ToString(CultureInfo.InvariantCulture));

        // The prestige entries accept any non-negative number; the Myxo wave keeps to its declared range.
        var bounded = config.Key.EndsWith("myxo_max_until_wave", StringComparison.Ordinal);
        var min = bounded ? (int)config.Range.Min : 0;
        var max = bounded ? (int)config.Range.Max : int.MaxValue;
        var hintText = bounded
            ? "Max below this wave, Next milestone from it"
            : config.Key.EndsWith("efficiency_percent", StringComparison.Ordinal)
                ? "0 disables this trigger" : "0 removes the minimum";
        var hint = UiFactory.CreateLabel(RowRect, hintText, UiTheme.ChipFontSize, UiTheme.TextMuted,
            TextAlignmentOptions.Left);
        hint.rectTransform.anchorMin = new Vector2(LabelRatio + 0.03f, 0f);
        hint.rectTransform.anchorMax = new Vector2(1f, 0f);
        hint.rectTransform.offsetMin = new Vector2(0f, 3f);
        hint.rectTransform.offsetMax = new Vector2(-UiTheme.InputInsetRight, 25f);

        input.onEndEdit.AddListener((Action<string>)(value =>
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
                number >= min && number <= max)
            {
                config.Write(number);
                hint.text = hintText;
                hint.color = UiTheme.TextMuted;
                return;
            }

            hint.text = $"Enter a whole number from {min} to {max}.";
            hint.color = UiTheme.Danger;
        }));
    }
}
