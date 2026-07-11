using System.Collections.Generic;
using System;
using UnityEngine;

public class Tracker
{
    public Tracker()
    {
        version = AppVersion.Number;
    }

    public string version;
    public List<TrackedObject.Data> objects;
}

[Serializable]
public struct RoomConfiguration
{
    public RoomConfiguration(string versionString) // c# 9 limitation, can remove parameter if this ever gets to c# 11
    {
        version = AppVersion.Number;
        roomDimension = default;
        collections = null;
        // client metadata
        clientAccountName = null;
        clientAccountAddressLine1 = null;
        clientAccountAddressLine2 = null;
        clientProjectName = null;
        clientProjectNumber = null;
        clientOrderReferenceNumber = null;
    }

    public string version;
    public RoomDimension roomDimension;
    public List<Tracker> collections;

    // --- Added client meta data ---
    public string clientAccountName;
    public string clientAccountAddressLine1;
    public string clientAccountAddressLine2;
    public string clientProjectName;
    public string clientProjectNumber;
    public string clientOrderReferenceNumber;
}