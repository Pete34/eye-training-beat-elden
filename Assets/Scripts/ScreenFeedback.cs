using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

// Large transient banners for learning feedback, plus a persistent end-of-game banner with restart.
public sealed class ScreenFeedback : MonoBehaviour
{
    private static ScreenFeedback instance;
    private Text flashText;
    private Text endText;
    private float flashUntil;
    private float restartAllowedTime;
    private bool triggerWasPressed = true;

    public static bool IsGameOver { get; private set; }

    public static void Flash(string message, Color color, float seconds = 1.3f)
    {
        var feedback = GetInstance();
        feedback.flashText.text = message;
        feedback.flashText.color = color;
        feedback.flashText.enabled = true;
        feedback.flashUntil = Time.unscaledTime + seconds;
    }

    public static void EndGame(string message, Color color)
    {
        var feedback = GetInstance();
        IsGameOver = true;
        feedback.flashText.enabled = false;
        feedback.endText.text = message + "\n<size=48>Press R or pull a trigger to restart</size>";
        feedback.endText.color = color;
        feedback.endText.enabled = true;
        feedback.restartAllowedTime = Time.unscaledTime + 1.5f;
    }

    private static ScreenFeedback GetInstance()
    {
        if (instance != null)
            return instance;

        var canvasObject = new GameObject("Screen Feedback", typeof(RectTransform), typeof(Canvas));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        // Parented to the head so the banner also works in VR, where overlay canvases do not render.
        var cam = Camera.main;
        if (cam != null)
            canvasObject.transform.SetParent(cam.transform, false);
        canvasObject.transform.localPosition = new Vector3(0f, 0f, 1.5f);
        canvasObject.transform.localRotation = Quaternion.identity;
        canvasObject.transform.localScale = Vector3.one * 0.001f;
        canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(1600f, 900f);

        instance = canvasObject.AddComponent<ScreenFeedback>();
        instance.flashText = CreateText(canvasObject.transform, "Flash", 110, new Vector2(0f, 150f));
        instance.endText = CreateText(canvasObject.transform, "End", 110, Vector2.zero);
        instance.flashText.enabled = false;
        instance.endText.enabled = false;
        return instance;
    }

    private static Text CreateText(Transform parent, string objectName, int fontSize, Vector2 position)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(1500f, 300f);

        var text = textObject.GetComponent<Text>();
        // Draw over the sword/shield placeholders that sit between the eye and the banner.
        text.material = new Material(text.defaultMaterial);
        text.material.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = true;
        textObject.AddComponent<Outline>().effectDistance = new Vector2(4f, -4f);
        return text;
    }

    private void Update()
    {
        if (flashText.enabled && Time.unscaledTime >= flashUntil)
            flashText.enabled = false;

        if (!IsGameOver || Time.unscaledTime < restartAllowedTime)
            return;

        var keyboard = Keyboard.current;
        var triggerPressed = InputDevices.GetDeviceAtXRNode(XRNode.RightHand).TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out var right) && right;
        var restart = (keyboard != null && keyboard.rKey.wasPressedThisFrame) || (triggerPressed && !triggerWasPressed);
        triggerWasPressed = triggerPressed;
        if (restart)
        {
#if UNITY_EDITOR
            // The prototype scene is not in Build Settings, so reload it through the editor API.
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                SceneManager.GetActiveScene().path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
#endif
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            IsGameOver = false;
        }
    }
}
