using Pulse;
using Pulse.Unity;
using UnityEngine;

public class WallMonitor : MonoBehaviour
{
    public static int activeCount = 0;
    public static GameObject pulseEngine;

    private void Awake()
    {
        if (pulseEngine == null)
            pulseEngine = PulseEngineScenarioDriver.Instance.gameObject;
    }

    private void OnEnable()
    {
        activeCount++;
        Debug.LogError("OnEnable: ActiveCount = " + activeCount);
        if (pulseEngine != null && !pulseEngine.activeSelf)
            pulseEngine.SetActive(true);
    }

    private void OnDisable()
    {
        activeCount--;
        Debug.LogError("OnDisable: ActiveCount = " + activeCount);
        if (activeCount <= 0 && pulseEngine != null)
            pulseEngine.SetActive(false);
    }
}
