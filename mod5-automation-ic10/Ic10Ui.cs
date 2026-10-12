using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mod5AutomationIc10
{
    // The widgets Mod 5's screens are built from, in code: ONI's UI is prefabs in the game's
    // asset bundles, and a mod has none of its own. Shared by the housing's side screen
    // (Ic10HousingSideScreen) and the program editor (Ic10Editor), so the two look alike.
    internal static class Ic10Ui
    {
        // ONI's side screens are a light panel with dark text; the buttons are its blue.
        public static readonly Color TextColour = new Color32(0x2C, 0x2C, 0x36, 255);
        public static readonly Color ErrorColour = new Color32(0xB0, 0x2A, 0x2A, 255);
        public static readonly Color ButtonColour = new Color32(0x4C, 0x56, 0x7A, 255);
        public static readonly Color ButtonHover = new Color32(0x63, 0x6F, 0x9C, 255);
        public static readonly Color ButtonPressed = new Color32(0x3A, 0x42, 0x5E, 255);
        public static readonly Color ButtonDisabled = new Color32(0x8A, 0x8C, 0x96, 255);
        // A text box is darker than the panel it sits on: white on ONI's near-white side
        // screen could not be seen while empty.
        public static readonly Color FieldColour = new Color32(0xDC, 0xDE, 0xE6, 255);
        public static readonly Color HintColour = new Color32(0x80, 0x82, 0x8C, 255);

        public static TextMeshProUGUI Text(Transform parent, string name, float size, bool wrap)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            if (Localization.FontAsset != null)
            {
                text.font = Localization.FontAsset;
            }
            text.fontSize = size;
            text.color = TextColour;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            return text;
        }

        public static GameObject Button(Transform parent, string name, string caption, float width)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = Color.white;
            UnityEngine.UI.Button button = go.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            ColorBlock colours = button.colors;
            colours.normalColor = ButtonColour;
            colours.highlightedColor = ButtonHover;
            colours.pressedColor = ButtonPressed;
            colours.selectedColor = ButtonColour;
            colours.disabledColor = ButtonDisabled;
            colours.colorMultiplier = 1f;
            button.colors = colours;
            LayoutElement size = go.AddComponent<LayoutElement>();
            if (width > 0f)
            {
                size.minWidth = width;
                size.preferredWidth = width;
            }
            TextMeshProUGUI text = Text(go.transform, "Caption", 14f, false);
            text.text = caption;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            Fill(text.rectTransform);
            go.AddComponent<ToolTip>();
            return go;
        }

        // A plain coloured rectangle, for a window's body or a header bar.
        public static Image Panel(Transform parent, string name, Color colour)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = colour;
            return image;
        }

        // Stretch a child over the whole of its parent.
        public static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // A one-line text box: a white field with dark text, its parts made before it wakes.
        public static KInputTextField TextField(Transform parent, string name, float width, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            Image back = go.AddComponent<Image>();
            back.color = FieldColour;
            LayoutElement size = go.AddComponent<LayoutElement>();
            size.minHeight = height;
            size.preferredHeight = height;
            if (width > 0f)
            {
                size.minWidth = width;
                size.preferredWidth = width;
            }
            else
            {
                size.flexibleWidth = 1f;
            }
            GameObject view = new GameObject("Viewport", typeof(RectTransform));
            view.transform.SetParent(go.transform, false);
            RectTransform viewRect = view.GetComponent<RectTransform>();
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = new Vector2(6f, 2f);
            viewRect.offsetMax = new Vector2(-6f, -2f);
            view.AddComponent<RectMask2D>();
            TextMeshProUGUI text = Text(view.transform, "Text", 14f, false);
            text.richText = false;
            Fill(text.rectTransform);
            TextMeshProUGUI hint = Text(view.transform, "Placeholder", 14f, false);
            hint.text = "type here";
            hint.fontStyle = FontStyles.Italic;
            hint.color = HintColour;
            Fill(hint.rectTransform);

            KInputTextField field = go.AddComponent<KInputTextField>();
            field.targetGraphic = back;
            field.textViewport = viewRect;
            field.textComponent = text;
            field.placeholder = hint;
            if (Localization.FontAsset != null)
            {
                field.fontAsset = Localization.FontAsset;
            }
            field.pointSize = 14f;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.customCaretColor = true;
            field.caretColor = TextColour;
            go.SetActive(true);
            return field;
        }

        // A vertical list of fixed height that scrolls: the returned transform is where its
        // rows go.
        public static Transform ScrollList(Transform parent, string name, float height)
        {
            GameObject list = new GameObject(name, typeof(RectTransform));
            list.transform.SetParent(parent, false);
            Image back = list.AddComponent<Image>();
            back.color = new Color(0f, 0f, 0f, 0.06f);
            list.AddComponent<RectMask2D>();
            LayoutElement size = list.AddComponent<LayoutElement>();
            size.minHeight = height;
            size.preferredHeight = height;

            GameObject content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(list.transform, false);
            RectTransform rect = content.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            VerticalLayoutGroup column = content.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(2, 2, 2, 2);
            column.spacing = 2f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = list.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.viewport = list.GetComponent<RectTransform>();
            scroll.content = rect;
            return content.transform;
        }

        // A horizontal row of controls of a fixed height.
        public static GameObject Row(Transform parent, string name, float height)
        {
            GameObject row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup line = row.AddComponent<HorizontalLayoutGroup>();
            line.spacing = 3f;
            line.childControlWidth = true;
            line.childControlHeight = true;
            line.childForceExpandWidth = false;
            line.childForceExpandHeight = true;
            LayoutElement size = row.AddComponent<LayoutElement>();
            size.minHeight = height;
            // Its group expands its children's height, which would report the row flexible.
            size.flexibleHeight = 0f;
            return row;
        }

        // A column that stacks its children top to bottom at their preferred heights.
        public static VerticalLayoutGroup Column(GameObject go, int padding, float spacing)
        {
            VerticalLayoutGroup column = go.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(padding, padding, padding, padding);
            column.spacing = spacing;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            return column;
        }

        public static void Wire(Transform button, System.Action action, string tip)
        {
            if (button == null)
            {
                return;
            }
            UnityEngine.UI.Button b = button.GetComponent<UnityEngine.UI.Button>();
            if (b != null)
            {
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() => action());
            }
            if (tip == null)
            {
                return;
            }
            try
            {
                button.GetComponent<ToolTip>()?.SetSimpleTooltip(tip);
            }
            catch (Exception)
            {
                // PluginAssets not up yet, or ToolTip changed shape: a button with no tooltip
                // still works.
            }
        }
    }
}
