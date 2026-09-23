using CinderPass.UI;
using CinderPass.Vehicle;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CinderPass.EditorTools
{
    /// <summary>Builds the minimal presentation HUD (uGUI, resolution-independent).</summary>
    public static class HudBuilder
    {
        static Font font;

        public static Hud Build(Transform parent, VehicleController vehicle)
        {
            font = AssetDatabase.LoadAssetAtPath<Font>($"{CinderPaths.Fonts}/Teko-Bold.ttf") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasGo = AssetUtil.Child(parent, "HUD");
            canvasGo.layer = 5;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var hud = canvasGo.AddComponent<Hud>();

            // Speed / gear (bottom right).
            var drive = Group(canvasGo.transform, "Drive", new Vector2(1, 0), new Vector2(-70, 60), new Vector2(330, 150));
            Panel(drive.transform, new Color(0.03f, 0.035f, 0.04f, 0.45f));
            var speed = Label(drive.transform, "Speed", "0", 104, TextAnchor.LowerRight, new Vector2(-120, 18), new Vector2(210, 120), Color.white);
            Label(drive.transform, "Unit", "KM/H", 26, TextAnchor.LowerLeft, new Vector2(100, 28), new Vector2(90, 40), new Color(1f, 1f, 1f, 0.7f));
            var gear = Label(drive.transform, "Gear", "N", 46, TextAnchor.LowerLeft, new Vector2(100, 62), new Vector2(90, 60), new Color(0.98f, 0.7f, 0.35f));

            // Mode chip (top left).
            var mode = Group(canvasGo.transform, "Mode", new Vector2(0, 1), new Vector2(60, -54), new Vector2(760, 46));
            Panel(mode.transform, new Color(0.03f, 0.035f, 0.04f, 0.45f));
            var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(mode.transform, false);
            var art = accent.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(0, 0); art.anchorMax = new Vector2(0, 1);
            art.pivot = new Vector2(0, 0.5f);
            art.sizeDelta = new Vector2(6, 0);
            art.anchoredPosition = Vector2.zero;
            var accentImg = accent.GetComponent<Image>();
            accentImg.color = new Color(0.36f, 0.86f, 0.8f);
            var modeText = Label(mode.transform, "Text", "AUTOPILOT", 30, TextAnchor.MiddleLeft, new Vector2(22, 0), new Vector2(730, 46), Color.white, stretchLeft: true);

            // Title card (upper centre).
            var title = Group(canvasGo.transform, "Title", new Vector2(0.5f, 1), new Vector2(0, -170), new Vector2(1400, 220));
            var titleText = Label(title.transform, "Title", "CINDER PASS", 128, TextAnchor.MiddleCenter, new Vector2(0, 30), new Vector2(1400, 150), Color.white);
            titleText.GetComponent<RectTransform>().anchorMin = titleText.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
            var subtitle = Label(title.transform, "Subtitle", "", 34, TextAnchor.MiddleCenter, new Vector2(0, -60), new Vector2(1400, 50), new Color(1f, 0.92f, 0.82f, 0.9f));
            subtitle.GetComponent<RectTransform>().anchorMin = subtitle.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);

            // Hint (bottom centre).
            var hint = Group(canvasGo.transform, "Hint", new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(1400, 52));
            Panel(hint.transform, new Color(0.03f, 0.035f, 0.04f, 0.5f));
            var hintText = Label(hint.transform, "Text", "", 30, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(1380, 52), Color.white);
            hintText.GetComponent<RectTransform>().anchorMin = hintText.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);

            // Hazard warning (centre).
            var hazard = Group(canvasGo.transform, "Hazard", new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1920, 110));
            Panel(hazard.transform, new Color(0.2f, 0.03f, 0.01f, 0.55f));
            var hazardText = Label(hazard.transform, "Text", "LAVA", 72, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(1900, 110), new Color(1f, 0.55f, 0.25f));
            hazardText.GetComponent<RectTransform>().anchorMin = hazardText.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);

            // Full-screen fade (top of the stack).
            var fade = new GameObject("Fade", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            fade.transform.SetParent(canvasGo.transform, false);
            var frt = fade.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.sizeDelta = Vector2.zero;
            fade.GetComponent<Image>().color = Color.black;
            fade.GetComponent<Image>().raycastTarget = false;
            var fadeGroup = fade.GetComponent<CanvasGroup>();
            fadeGroup.blocksRaycasts = false;
            fadeGroup.alpha = 0f;

            Wiring.Set(hud, "vehicle", vehicle);
            Wiring.Set(hud, "driveGroup", drive.GetComponent<CanvasGroup>());
            Wiring.Set(hud, "speedText", speed);
            Wiring.Set(hud, "gearText", gear);
            Wiring.Set(hud, "modeGroup", mode.GetComponent<CanvasGroup>());
            Wiring.Set(hud, "modeText", modeText);
            Wiring.Set(hud, "modeAccent", accentImg);
            Wiring.Set(hud, "titleGroup", title.GetComponent<CanvasGroup>());
            Wiring.Set(hud, "titleText", titleText);
            Wiring.Set(hud, "subtitleText", subtitle);
            Wiring.Set(hud, "hintGroup", hint.GetComponent<CanvasGroup>());
            Wiring.Set(hud, "hintText", hintText);
            Wiring.Set(hud, "hazardGroup", hazard.GetComponent<CanvasGroup>());
            Wiring.Set(hud, "hazardText", hazardText);
            Wiring.Set(hud, "fadeGroup", fadeGroup);

            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = AssetUtil.Child(parent, "EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
            return hud;
        }

        static GameObject Group(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var cg = go.GetComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
            return go;
        }

        static void Panel(Transform parent, Color c)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
        }

        static Text Label(Transform parent, string name, string text, int size, TextAnchor align, Vector2 pos, Vector2 box, Color color, bool stretchLeft = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            Vector2 anchor = stretchLeft ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 0.5f);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = stretchLeft ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = go.GetComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.55f);
            sh.effectDistance = new Vector2(2f, -2f);
            return t;
        }
    }
}
