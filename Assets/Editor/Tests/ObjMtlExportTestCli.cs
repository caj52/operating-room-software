using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// One-shot EditMode run when TestData/_run_obj_mtl_tests.request exists.
/// Writes TestData/_obj_mtl_test_results.txt
/// </summary>
[InitializeOnLoad]
static class ObjMtlExportTestCli
{
    const string RequestPath = "TestData/_run_obj_mtl_tests.request";
    const string ResultPath = "TestData/_obj_mtl_test_results.txt";

    static ObjMtlExportTestCli()
    {
        if (!File.Exists(RequestPath))
            return;
        EditorApplication.delayCall += Run;
    }

    static void Run()
    {
        if (!File.Exists(RequestPath))
            return;

        File.Delete(RequestPath);
        Directory.CreateDirectory("TestData");

        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Writer());
        api.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.EditMode,
            groupNames = new[] { "ObjMtlExportTests" }
        }));
    }

    sealed class Writer : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"result={result.ResultState}");
            sb.AppendLine($"pass={result.PassCount} fail={result.FailCount} skip={result.SkipCount} inconclusive={result.InconclusiveCount}");
            Dump(result, sb, 0);
            File.WriteAllText(ResultPath, sb.ToString());
            Debug.Log($"ObjMtlExportTests finished: pass={result.PassCount} fail={result.FailCount}\n{sb}");
        }

        static void Dump(ITestResultAdaptor r, StringBuilder sb, int indent)
        {
            if (r == null)
                return;

            if (!r.HasChildren)
            {
                sb.Append(' ', indent);
                sb.Append(r.FullName);
                sb.Append(": ");
                sb.Append(r.ResultState);
                if (!string.IsNullOrEmpty(r.Message))
                {
                    sb.Append(" — ");
                    sb.Append(r.Message.Replace("\r", " ").Replace("\n", " "));
                }
                sb.AppendLine();
                return;
            }

            foreach (var child in r.Children)
                Dump(child, sb, indent);
        }
    }
}
