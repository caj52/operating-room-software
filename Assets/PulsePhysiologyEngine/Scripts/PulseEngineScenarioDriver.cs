/* Distributed under the Apache License, Version 2.0.
   See accompanying NOTICE file for details.*/

using System;
using System.Collections.Generic;
using UnityEngine;
using Pulse.CDM;

namespace Pulse.Unity
{
    [ExecuteInEditMode]
    public class PulseEngineScenarioDriver : PulseEngineSource
    {
        public static PulseEngineScenarioDriver Instance { get; private set; }

        public TextAsset scenarioJson;

        [NonSerialized]
        protected SEScenario scenario;

        // Use sorted list for better lookup performance
        protected SortedList<double, SEAction> actions = new SortedList<double, SEAction>();
        protected List<KeyValuePair<double, SEAction>> actionsToRemove = new List<KeyValuePair<double, SEAction>>(16);

        // Cache these values to avoid repeated calculations
        private double nextUpdateTime = 0;
        private double nextSampleTime = 0;
        private int preAllocatedSize = 1000; // Pre-allocate memory for collections

        // MARK: Monobehavior methods

        protected virtual void OnValidate()
        {
            sampleRate = Math.Round(sampleRate / 0.02) * 0.02;
        }

        protected virtual void Awake()
        {
            Instance = this;

            // Create data container with pre-allocated capacity
            data = ScriptableObject.CreateInstance<PulseData>();
            data.fields = new StringList(data_requests.Count + 1);
            data.timeStampList = new DoubleList(preAllocatedSize);
            data.valuesTable = new List<DoubleList>(data_requests.Count + 1);

            // The first field is always the simulation time in seconds
            data.fields.Add("Simulation Time(s)");
            var timeValues = new DoubleList(preAllocatedSize);
            data.valuesTable.Add(timeValues);

            foreach (var request in data_requests)
            {
                data.fields.Add(request.ToString().Replace("/", "\u2215"));
                data.valuesTable.Add(new DoubleList(preAllocatedSize));
            }

            pullAllData = (Math.Abs(sampleRate - pulseTimeStep) < 0.0001);

            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
                gameObject.SetActive(false);
            }
        }

        protected virtual void Start()
        {
            if (!Application.isPlaying)
                return;
            InitializeEngine();
            PrepareScenarioActions();

            pulseTime = 0;
            pulseSampleTime = 0;
            nextUpdateTime = pulseTimeStep;
            nextSampleTime = sampleRate;
        }

        private void InitializeEngine()
        {
            string dateAndTimeVar = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string logFilePath = $"{Application.persistentDataPath}/{gameObject.name}{dateAndTimeVar}.log";
            engine = new PulseUnityEngine();
            engine.SetLogFilename(logFilePath);
            scenario = new SEScenario();

            if (scenarioJson != null)
            {
                if (!scenario.SerializeFromString(scenarioJson.text, eSerializationFormat.JSON))
                {
                    Debug.LogError($"Unable to load scenario file {scenarioJson}", this);
                    return;
                }
            }
            else
            {
                LoadDefaultScenario();
            }

            if (scenario.HasPatientConfiguration())
                scenario.GetPatientConfiguration().SetDataRootDir($"{Application.streamingAssetsPath}/Data/");

            MergeDataRequests();
            InitializeEngineState();
        }

        private void LoadDefaultScenario()
        {
            string streamingScenarioFilename = $"{Application.streamingAssetsPath}/test_scenario.json";
            if (!scenario.SerializeFromFile(streamingScenarioFilename))
            {
                Debug.LogError($"Unable to load scenario file {streamingScenarioFilename}", this);
                CreateDefaultScenario();
            }
        }

        private void CreateDefaultScenario()
        {
            scenario.SetName("Scenario");
            scenario.SetDescription("Simple Scenario to demonstrate building a scenario by the CDM API");
            scenario.GetPatientConfiguration().SetPatientFile("StandardMale.json");

            SEDataRequest dr = SEDataRequest.CreatePhysiologyDataRequest("BloodVolume", VolumeUnit.mL);
            scenario.GetDataRequestManager().GetDataRequests().Add(dr);
        }

        private void MergeDataRequests()
        {
            // Add scenario data requests to our data container
            foreach (var request in scenario.GetDataRequestManager().GetDataRequests())
            {
                data.fields.Add(request.ToString().Replace("/", "\u2215"));
                data.valuesTable.Add(new DoubleList(preAllocatedSize));
            }

            // Push standard data requests to the front of the scenario
            for (int i = data_requests.Count; i > 0; i--)
                scenario.GetDataRequestManager().GetDataRequests().Insert(0, data_requests[i - 1]);
        }

        private void InitializeEngineState()
        {
            if (scenario.HasEngineState())
            {
                string state = $"{Application.streamingAssetsPath}/Data/states/{scenario.GetEngineState()}";
                if (!engine.SerializeFromFile(state, scenario.GetDataRequestManager()))
                {
                    Debug.LogError($"Unable to load state file {state}", this);
                }
            }
            else if (scenario.HasPatientConfiguration())
            {
                if (!engine.InitializeEngine(scenario.GetPatientConfiguration(), scenario.GetDataRequestManager()))
                {
                    Debug.LogError("Unable to initialize patient", this);
                }
            }
            else
            {
                Debug.LogError("Invalid Scenario provided", this);
            }
        }

        private void PrepareScenarioActions()
        {
            double simTime_s = 0;
            foreach (SEAction a in scenario.GetActions())
            {
                if (a is SEAdvanceTime advanceTime)
                {
                    simTime_s += advanceTime.GetTime().GetValue(TimeUnit.s);
                }
                else
                {
                    actions.Add(simTime_s, a);
                }
            }
        }

        protected virtual void Update()
        {
            if (!Application.isPlaying || engine == null || pauseUpdate)
                return;

            double currentTime = Time.time;
            if (currentTime < nextUpdateTime)
                return; // Not time to update yet

            // Clear data only when we'll be adding new data
            if (pullAllData || pulseSampleTime >= sampleRate)
            {
                ClearDataContainer();
            }

            ProcessTimeSteps(currentTime);

            nextUpdateTime = Time.time + pulseTimeStep;
        }

        private void ClearDataContainer()
        {
            if (!data.timeStampList.IsEmpty())
            {
                data.timeStampList.Clear();
                for (int j = 0; j < data.valuesTable.Count; ++j)
                    data.valuesTable[j].Clear();
            }
        }

        private void ProcessTimeSteps(double currentTime)
        {
            int stepsToProcess = Mathf.CeilToInt((float)((currentTime - pulseTime) / pulseTimeStep));
            stepsToProcess = Mathf.Clamp(stepsToProcess, 1, 10); // Limit maximum steps to prevent freezing

            for (int i = 0; i < stepsToProcess; ++i)
            {
                ProcessActions();
                AdvanceSimulation();

                if (pullAllData || pulseSampleTime >= sampleRate)
                {
                    SampleData();
                }
            }
        }

        private void ProcessActions()
        {
            actionsToRemove.Clear();

            // Find actions to process at current time
            foreach (var actionPair in actions)
            {
                if (actionPair.Key <= pulseTime)
                {
                    actionsToRemove.Add(actionPair);
                    if (!engine.ProcessAction(actionPair.Value))
                    {
                        Debug.LogError($"Could not process action {actionPair.Value}", this);
                    }
                }
                else
                {
                    // Actions are sorted, so we can break early
                    break;
                }
            }

            // Remove processed actions
            foreach (var action in actionsToRemove)
            {
                actions.Remove(action.Key);
            }
        }

        private void AdvanceSimulation()
        {
            // Increment time
            pulseTime += pulseTimeStep;
            pulseSampleTime += pulseTimeStep;

            // Advance simulation
            engine.AdvanceTime_s(pulseTimeStep);
        }

        private void SampleData()
        {
            pulseSampleTime = 0;
            data.timeStampList.Add(pulseTime);
            data_values = engine.PullData();

            for (int j = 0; j < data_values.Length; ++j)
            {
                data.valuesTable[j].Add(data_values[j]);
            }
        }

        protected virtual void OnApplicationQuit()
        {
            if (engine != null)
            {
                engine = null;
            }
        }
    }
}