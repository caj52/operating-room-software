/* Distributed under the Apache License, Version 2.0.
   See accompanying NOTICE file for details.*/

using System;
using System.Runtime.InteropServices;
using UnityEngine;

using Pulse;

namespace Pulse.Unity
{
  // This is essentially a copy of the PulseEngine class from our engine repository
  // We have to do this to support the different Attribute values for iOS and all other platforms
  // This will need to stay in sync with the PulseEngine class in the engine repository
  // But I do not see that class changing very often....

  [ExecuteInEditMode]
  public class PulseUnityEngine : PulseEngineBase
  {
#if UNITY_IOS
    private const string Attribute = "__Internal";
#else
    private const string Attribute = "PulseC";
#endif

    public PulseUnityEngine(eModelType m = eModelType.HumanAdultWholeBody, string data_dir = "./") : base(m, data_dir) { }

    [DllImport(Attribute)]
    private static extern double PulseVersion(out IntPtr version_str);
    public static string Version()
    {
      if (version.Length == 0)
      {
        IntPtr str_addr;
        PulseVersion(out str_addr);
        version = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(str_addr);
      }
      return version;
    }

    [DllImport(Attribute)]
    private static extern double PulseHash(out IntPtr version_str);
    public static string Hash()
    {
      if (hash.Length == 0)
      {
        IntPtr str_addr;
        PulseHash(out str_addr);
        hash = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(str_addr);
      }
      return hash;
    }

    [DllImport(Attribute)]
    private static extern IntPtr Allocate(int model, string data_dir);
    protected override IntPtr BaseAllocate(int model, string data_dir)
    {
      return Allocate(model, data_dir);
    }

    [DllImport(Attribute)]
    private static extern void Deallocate(IntPtr pulse);
    protected override void BaseDeallocate()
    {
      Deallocate(pulse_cptr);
    }

    protected override void BaseClear()
    {

    }

    [DllImport(Attribute)]
    private static extern double GetTimeStep(IntPtr pulse, string unit);
    protected override double BaseGetTimeStep(string unit)
    {
      return GetTimeStep(pulse_cptr, unit);
    }

    [DllImport(Attribute)]
    private static extern bool SerializeFromFile(IntPtr pulse, string filename, string data_mgr, int data_mgr_format);
    protected override bool BaseSerializeFromFile(string filename, string data_mgr, int data_mgr_format)
    {
      return SerializeFromFile(pulse_cptr, filename, data_mgr, data_mgr_format);
    }

    [DllImport(Attribute)]
    private static extern bool SerializeToFile(IntPtr pulse, string filename);
    protected override bool BaseSerializeToFile(string filename)
    {
      return SerializeToFile(pulse_cptr, filename);
    }

    [DllImport(Attribute)]
    private static extern bool SerializeFromString(IntPtr pulse, string state, string data_mgr, int format);
    protected override bool BaseSerializeFromString(string state, string data_mgr, int format)
    {
      return SerializeFromString(pulse_cptr, state, data_mgr, format);
    }

    [DllImport(Attribute)]
    private static extern bool SerializeToString(IntPtr pulse, int format, out IntPtr state_str);
    protected override bool BaseSerializeToString(int format, out IntPtr state_str)
    {
      return SerializeToString(pulse_cptr, format, out state_str);
    }

    [DllImport(Attribute)]
    private static extern bool InitializeEngine(IntPtr pulse, string patient_configuration, string data_mgr, int thunk_format);
    protected override bool BaseInitializeEngine(string patient_configuration, string data_mgr, int thunk_format)
    {
      return InitializeEngine(pulse_cptr, patient_configuration, data_mgr, thunk_format);
    }

    [DllImport(Attribute)]
    private static extern bool GetInitialPatient(IntPtr pulse, int format, out IntPtr initial_patient);
    protected override bool BaseGetInitialPatient(int format, out IntPtr initial_patient)
    {
      return GetInitialPatient(pulse_cptr, format, out initial_patient);
    }

    [DllImport(Attribute)]
    private static extern bool GetConditions(IntPtr pulse, int format, out IntPtr conditions);
    protected override bool BaseGetConditions(int format, out IntPtr conditions)
    {
      return GetConditions(pulse_cptr, format, out conditions);
    }

    [DllImport(Attribute)]
    private static extern bool GetPatientAssessment(IntPtr pulse, int type, int format, out IntPtr assessment);
    protected override bool BaseGetPatientAssessment(int type, int format, out IntPtr assessment)
    {
      return GetPatientAssessment(pulse_cptr, type, format, out assessment);
    }

    [DllImport(Attribute)]
    private static extern void LogToConsole(IntPtr pulse, bool b);
    protected override void BaseLogToConsole(bool b)
    {
      LogToConsole(pulse_cptr, b);
    }

    [DllImport(Attribute)]
    private static extern void SetLogFilename(IntPtr pulse, string filename);
    protected override void BaseSetLogFilename(string filename)
    {
      SetLogFilename(pulse_cptr, filename);
    }

    [DllImport(Attribute)]
    private static extern void KeepLogMessages(IntPtr pulse, bool save);// Let the engine know to save log msgs or not
    protected override void BaseKeepLogMessages(bool save)
    {
      KeepLogMessages(pulse_cptr, save);
    }

    [DllImport(Attribute)]
    private static extern bool PullLogMessages(IntPtr pulse, int format, out IntPtr event_changes);
    protected override bool BasePullLogMessages(int format, out IntPtr event_changes)
    {
      return PullLogMessages(pulse_cptr, format, out event_changes);
    }

    [DllImport(Attribute)]
    private static extern void KeepEventChanges(IntPtr pulse, bool keep);// Let the engine know to save events or not
    protected override void BaseKeepEventChanges(bool keep)
    {
      KeepEventChanges(pulse_cptr, keep);
    }

    [DllImport(Attribute)]
    private static extern bool PullEvents(IntPtr pulse, int format, out IntPtr event_changes);
    protected override bool BasePullEvents(int format, out IntPtr event_changes)
    {
      return PullEvents(pulse_cptr, format, out event_changes);
    }

    [DllImport(Attribute)]
    private static extern bool PullActiveEvents(IntPtr pulse, int format, out IntPtr active_events);
    protected override bool BasePullActiveEvents(int format, out IntPtr active_events)
    {
      return PullActiveEvents(pulse_cptr, format, out active_events);
    }

    [DllImport(Attribute)]
    private static extern bool ProcessActions(IntPtr pulse, string any_action_list, int format);
    protected override bool BaseProcessActions(string any_action_list, int format)
    {
      return ProcessActions(pulse_cptr, any_action_list, format);
    }

    [DllImport(Attribute)]
    private static extern bool PullActiveActions(IntPtr pulse, int format, out IntPtr actions);
    protected override bool BasePullActiveActions(int format, out IntPtr actions)
    {
      return PullActiveActions(pulse_cptr, format, out actions);
    }

    [DllImport(Attribute)]
    private static extern bool AdvanceTimeStep(IntPtr pulse);
    protected override bool BaseAdvanceTimeStep()
    {
      return AdvanceTimeStep(pulse_cptr);
    }

    [DllImport(Attribute)]
    private static extern IntPtr PullData(IntPtr pulse);
    protected override IntPtr BasePullData()
    {
      return PullData(pulse_cptr);
    }

    [DllImport(Attribute)]
    public static new extern bool AreCompatibleUnits(string fromUnit, string toUnit);

    [DllImport(Attribute)]
    public static new extern double ConvertValue(double value, string fromUnit, string toUnit);
  }
}
