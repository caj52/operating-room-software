using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// Runs the Edit Mode suite: export paths, quote math, colliders, and the prefab contracts.
/// </summary>
public static class RegressionTestMenu
{
    [MenuItem("Operating Room/Run regression tests")]
    public static void Run()
    {
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new LogResults(api));
        api.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.EditMode
        }));
    }

    sealed class LogResults : ICallbacks
    {
        readonly TestRunnerApi _api;

        public LogResults(TestRunnerApi api) => _api = api;

        public void RunStarted(ITestAdaptor testsToRun) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            int failed = result.FailCount;
            string summary = failed == 0
                ? "Regression tests passed (" + result.PassCount + ")."
                : failed + " regression test(s) failed. " + result.PassCount + " passed.";

            if (failed == 0)
                Debug.Log(summary);
            else
                Debug.LogError(summary);

            EditorUtility.DisplayDialog("Regression tests", summary, "OK");
            _api.UnregisterCallbacks(this);
            Object.DestroyImmediate(_api);
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.TestStatus == TestStatus.Failed && !result.HasChildren)
                Debug.LogError(result.Name + "\n" + result.Message);
        }
    }
}
