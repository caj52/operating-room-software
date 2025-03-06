/* Distributed under the Apache License, Version 2.0.
   See accompanying NOTICE file for details.*/

using System;
using System.Collections.Generic;
using UnityEngine;

using Pulse.CDM;

namespace Pulse.Unity
{
  public class PulseEngineSource : PulseDataSource
  {
    [Range(0.02f, 2.0f)]
    public double sampleRate = 0.02;    // How often you wish to get data from Pulse

    [NonSerialized]
    public PulseUnityEngine engine;     // Pulse engine to drive

    [NonSerialized]
    public bool pauseUpdate = false;    // Do not advance any time

    protected double pulseTime;
    protected double pulseTimeStep = 0.02;
    protected double pulseSampleTime;
    protected bool pullAllData = true;

    // We need to define data requests we want available in the Editor up front
    // These data requests are needed to hook up the vitals monitor or other GUI based editing
    // PLEASE ENSURE THERE ARE NO DUPLICATE DATA REQUESTS, THE ENGINE WILL NOT INITIALIZE
    // IT IS UNABLE TO PROPERLY ORDER THE PullData ARRAY WITH DUPLIATES
    // If you are using the scnario driver and not connecting data via the Editor (such as the vitals monitor),
    // you don't need this, you could just define your data requests in the scenario file.
    // But we demonstrate this class in a scene with the vitals monitor
    // So we still need this list up front in order to hook up our vitals monitor components in the editor
    public readonly List<SEDataRequest> data_requests = new List<SEDataRequest>
    {
        SEDataRequest.CreateECGDataRequest("Lead3ElectricPotential", ElectricPotentialUnit.mV),
        SEDataRequest.CreatePhysiologyDataRequest("HeartRate", FrequencyUnit.Per_min),
        SEDataRequest.CreatePhysiologyDataRequest("ArterialPressure", PressureUnit.mmHg),
        SEDataRequest.CreatePhysiologyDataRequest("MeanArterialPressure", PressureUnit.mmHg),
        SEDataRequest.CreatePhysiologyDataRequest("SystolicArterialPressure", PressureUnit.mmHg),
        SEDataRequest.CreatePhysiologyDataRequest("DiastolicArterialPressure", PressureUnit.mmHg),
        SEDataRequest.CreatePhysiologyDataRequest("OxygenSaturation"),
        SEDataRequest.CreatePhysiologyDataRequest("EndTidalCarbonDioxidePressure", PressureUnit.mmHg),
        SEDataRequest.CreatePhysiologyDataRequest("RespirationRate", FrequencyUnit.Per_min),
        SEDataRequest.CreatePhysiologyDataRequest("SkinTemperature", TemperatureUnit.C),
        SEDataRequest.CreateGasCompartmentDataRequest("Carina", "CarbonDioxide", "PartialPressure", PressureUnit.mmHg)
    };

    // Create a reference to a double[] that will contain the data returned from Pulse
    protected double[] data_values;
  }
}
