/* Distributed under the Apache License, Version 2.0.
   See accompanying NOTICE file for details.*/

using System;
using System.Collections.Generic;
using UnityEngine;

using Pulse.CDM;

namespace Pulse.Unity
{
  // Component used to manage a PulseUnityEngine object, advance its simulation time,
  // and broadcast the resulting data. You can use this object as it, or extend it
  // or use it as a basis for your own driver.
  [ExecuteInEditMode]
  public class PulseEngineDriver : PulseEngineSource
  {
    public TextAsset initialStateFile;  // Initial stable state to load
    public eSerializationFormat serializationFormat = eSerializationFormat.JSON; // state file format

    // MARK: Monobehavior methods

    // Called when the inspector inputs are modified
    protected virtual void OnValidate()
    {
      // Round down to closest factor of 0.02. Need to use doubles due to
      // issues with floats multiplication (0.1 -> 0.0999999)
      sampleRate = Math.Round(sampleRate / 0.02) * 0.02;
    }

    // Called when application or editor opens
    protected virtual void Awake()
    {
      // Create our data container
      data = ScriptableObject.CreateInstance<PulseData>();

      // Store data field names
      // data_values[0] is always the simulation time in seconds
      // The rest of the data values are in order of the data_requests list
      data.fields = new StringList();// string[vitals_monitor_data_requests.Count + 1];
      data.fields.Add("Simulation Time(s)");
      for (int i = 0; i < data_requests.Count; i++)
        data.fields.Add(data_requests[i].ToString().Replace("/", "\u2215"));

      // Allocate space for data times and values
      data.timeStampList = new DoubleList();
      data.valuesTable = new List<DoubleList>();
      for (int fieldId = 0; fieldId < data.fields.Count; ++fieldId)
        data.valuesTable.Add(new DoubleList());
      pullAllData = (sampleRate == pulseTimeStep);
    }

    // Called at the first frame when the component is enabled
    protected virtual void Start()
    {
      // Ensure we only read data if the application is playing
      // and we have a state file to initialize the engine with
      if (!Application.isPlaying)
        return;

      // Allocate PulseUnityEngine with path to logs and needed data files
      string dateAndTimeVar = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
      string logFilePath = Application.persistentDataPath + "/" +
                                      gameObject.name +
                                      dateAndTimeVar + ".log";
      engine = new PulseUnityEngine();
      engine.SetLogFilename(logFilePath);

      SEDataRequestManager data_mgr = new SEDataRequestManager(data_requests);

      // NOTE, there are other ways to initialize the engine, see here
      // https://gitlab.kitware.com/physiology/engine/-/blob/4.x/src/csharp/howto/HowTo_EngineUse.cs

      // Initialize engine state from tje state file content
      if (initialStateFile != null)
      {
        if (!engine.SerializeFromString(initialStateFile.text, data_mgr, serializationFormat))
          Debug.unityLogger.LogError("PulsePhysiologyEngine", "Unable to load state file " + initialStateFile);
      }
      else
      {
        // You do not have to use the Editor control if you don't want to,
        // You could simply specify a file on disk via use of the Streaming Assets folder
        string state = Application.streamingAssetsPath + "/Data/states/StandardMale@0s.pbb";
        if (!engine.SerializeFromFile(state, data_mgr))
          Debug.unityLogger.LogError("PulsePhysiologyEngine", "Unable to load state file " + state);
      }

      pulseTime = 0;
      pulseSampleTime = 0;
    }

    // Called before every frame
    protected virtual void Update()
    {
      // Ensure we only broadcast data if the application is playing
      // and there a valid pulse engine to simulate data from
      if (!Application.isPlaying || engine == null || pauseUpdate)
        return;

      double timeElapsed = Time.time - pulseTime;
      if (timeElapsed < pulseTimeStep)
        return;// Not running yet

      // Clear PulseData container
      if (!data.timeStampList.IsEmpty())
      {
        data.timeStampList.Clear();
        for (int j = 0; j < data.valuesTable.Count; ++j)
          data.valuesTable[j].Clear();
      }

      // Iterate over multiple time steps if needed
      int numberOfDataPointsNeeded = (int)Math.Floor(timeElapsed / pulseTimeStep);
      //if (numberOfDataPointsNeeded > 2)
      //  Debug.unityLogger.Log("Big Catchup "+ numberOfDataPointsNeeded + ", timeElapsed = " + timeElapsed);
      for (int i = 0; i < numberOfDataPointsNeeded; ++i)
      {
        // Increment pulse time
        pulseTime += pulseTimeStep;
        pulseSampleTime += pulseTimeStep;

        // Advance simulation by time step
        bool success = engine.AdvanceTime_s(pulseTimeStep);
        if (!success)
          continue;

        // Copy simulated data to data container (if its time)
        if (pullAllData || pulseSampleTime >= sampleRate)
        {
          pulseSampleTime = 0;
          data.timeStampList.Add(pulseTime);
          data_values = engine.PullData();
          for (int j = 0; j < data_values.Length; ++j)
            data.valuesTable[j].Add((float)data_values[j]);
        }
      }
    }
    protected virtual void OnApplicationQuit()
    {
      engine = null;
    }
  }
}
