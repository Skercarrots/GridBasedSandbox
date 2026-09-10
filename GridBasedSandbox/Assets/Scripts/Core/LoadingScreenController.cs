using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
//  LoadingScreenController — Minimal loading-screen UI, driven entirely by
//  WorldBootstrapper (Show / SetProgress / Hide). No game logic lives here —
//  just visuals — so you can restyle or replace this freely without touching
//  the bootstrap flow.
//
//  SCENE SETUP
//  • A full-screen Canvas, with a CanvasGroup on its root panel (for fading).
//  • A child Image, Image Type = Filled, Fill Method = Horizontal, as the
//    progress bar — assign it to progressFillImage.
//  • Optionally a TextMeshProUGUI showing the percentage — assign it to
//    progressLabel. The field type is TMP_Text (the base class) so either
//    TextMeshProUGUI or TextMeshPro can be dropped in from the Inspector.
//  • Start the GameObject active in the scene (or inactive — Show() activates
//    it either way) so WorldBootstrapper can find/use it immediately.
// ─────────────────────────────────────────────────────────────────────────────

public class LoadingScreenController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image progressFillImage;
    [SerializeField] private TMP_Text progressLabel;

    public void Show()
    {
        gameObject.SetActive(true);
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        SetProgress(0f);
    }

    public void SetProgress(float t)
    {
        t = Mathf.Clamp01(t);

        if (progressFillImage != null)
            progressFillImage.fillAmount = t;

        if (progressLabel != null)
            progressLabel.text = $"Generating world… {Mathf.RoundToInt(t * 100f)}%";
    }

    public void Hide()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        gameObject.SetActive(false);
    }
}