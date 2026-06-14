using UnityEngine;
using TMPro;
using System.Collections;

public class DebugLabel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshPro _label;

    [Header("Settings")]
    [SerializeField] private Vector3 _offset = new Vector3(0f, 1.5f, 0f);
    [SerializeField] private float _fontSize = 3f;
    [SerializeField] private Color _textColor = Color.white;

    [Header("Flash Settings")]
    [SerializeField] private float _fadeInDuration = 0.2f;
    [SerializeField] private float _fadeOutDuration = 0.4f;

    private Camera _mainCamera;
    private bool _isVisible;
    private Coroutine _flashCoroutine;

    private void Awake()
    {
        _mainCamera = Camera.main;

        if (_label == null)
            _label = CreateLabel();

        SetVisible(false);
    }

    private void LateUpdate()
    {
        if (!_isVisible || _mainCamera == null) return;

        _label.transform.forward = _mainCamera.transform.forward;
        _label.transform.position = transform.position + _offset;
    }

    public void Debug(object value)
    {
        _label.text = value?.ToString() ?? "null";
        SetVisible(true);
    }

    /// <summary>
    /// Shows the label with a fade in, holds for stayDuration seconds, then fades out.
    /// Interrupts any flash already in progress.
    /// </summary>
    public void Flash(object value, float stayDuration = 1f)
    {
        if (_flashCoroutine != null)
            StopCoroutine(_flashCoroutine);

        _label.text = value?.ToString() ?? "null";
        _flashCoroutine = StartCoroutine(FlashRoutine(stayDuration));
    }

    public void ClearDebug()
    {
        if (_flashCoroutine != null)
        {
            StopCoroutine(_flashCoroutine);
            _flashCoroutine = null;
        }

        SetVisible(false);
        _label.text = string.Empty;
    }

    private IEnumerator FlashRoutine(float stayDuration)
    {
        // Fade in
        SetVisible(true);
        yield return FadeRoutine(0f, 1f, _fadeInDuration);

        // Stay
        yield return new WaitForSeconds(stayDuration);

        // Fade out
        yield return FadeRoutine(1f, 0f, _fadeOutDuration);

        SetVisible(false);
        _flashCoroutine = null;
    }

    private IEnumerator FadeRoutine(float from, float to, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(from, to, elapsed / duration);
            _label.color = new Color(_textColor.r, _textColor.g, _textColor.b, alpha);
            yield return null;
        }

        _label.color = new Color(_textColor.r, _textColor.g, _textColor.b, to);
    }

    private void SetVisible(bool visible)
    {
        _isVisible = visible;
        _label.gameObject.SetActive(visible);
    }

    private TextMeshPro CreateLabel()
    {
        var labelGO = new GameObject("DebugLabel");
        labelGO.transform.SetParent(transform);
        labelGO.transform.localPosition = _offset;

        var tmp = labelGO.AddComponent<TextMeshPro>();
        tmp.fontSize = _fontSize;
        tmp.color = _textColor;
        tmp.alignment = TextAlignmentOptions.Center;

        return tmp;
    }
}