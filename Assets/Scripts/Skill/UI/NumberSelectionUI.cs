using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NumberSelectionUI : MonoBehaviour
{
    public static NumberSelectionUI Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private Slider slider;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button okButton;
    [SerializeField] private Button cancelButton;

    private TaskCompletionSource<int> tcs;
    private int minValue;
    private int maxValue;

    void Awake()
    {
        if (Instance == null) Instance = this; else Destroy(gameObject);
        if (panel != null) panel.SetActive(false);

        if (okButton != null)
        {
            okButton.onClick.RemoveAllListeners();
            okButton.onClick.AddListener(OnOk);
        }
        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(OnCancel);
        }

        if (slider != null)
        {
            slider.onValueChanged.AddListener(OnSliderChanged);
        }
        if (inputField != null)
        {
            inputField.onEndEdit.AddListener(OnInputChanged);
        }
    }

    public async Task<int> ShowAsync(string title, int min, int max, int defaultValue = 0)
    {
        minValue = Mathf.Max(0, min);
        maxValue = Mathf.Max(minValue, max);

        if (SkillContext.IsOfflineCpuResolving)
        {
            int chosen = Random.Range(minValue, maxValue + 1);
            Debug.Log($"[NumberSelectionUI] CPUが数値をランダム選択: {chosen}");
            return chosen;
        }

        if (panel != null) panel.SetActive(true);
        if (titleText != null) titleText.text = title;

        if (slider != null)
        {
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.wholeNumbers = true;
            slider.value = Mathf.Clamp(defaultValue, minValue, maxValue);
        }

        if (inputField != null)
        {
            inputField.text = Mathf.Clamp(defaultValue, minValue, maxValue).ToString();
        }

        tcs = new TaskCompletionSource<int>();
        var result = await tcs.Task;
        if (panel != null) panel.SetActive(false);
        return result;
    }

    private void OnSliderChanged(float v)
    {
        if (inputField != null)
        {
            inputField.text = ((int)v).ToString();
        }
    }

    private void OnInputChanged(string s)
    {
        if (int.TryParse(s, out int val))
        {
            val = Mathf.Clamp(val, minValue, maxValue);
            if (slider != null) slider.value = val;
            if (inputField != null && inputField.text != val.ToString()) inputField.text = val.ToString();
        }
        else
        {
            if (inputField != null) inputField.text = ((int)(slider?.value ?? minValue)).ToString();
        }
    }

    private void OnOk()
    {
        int val = (int)(slider != null ? slider.value : minValue);
        tcs?.TrySetResult(val);
    }

    private void OnCancel()
    {
        tcs?.TrySetResult(0);
    }
}


