/* Distributed under the Apache License, Version 2.0.
   See accompanying NOTICE file for details.*/

using UnityEngine;

namespace Pulse.Unity
{
  public class PulseEngineController : MonoBehaviour
  {
    [HideInInspector]
    public PulseEngineSource driver;  // Holds the pulse engine data source
  }
}
