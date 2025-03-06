using System.Collections.Generic;
using System;
using UnityEngine;

public class Tracker
{
    public Tracker()
    {
        version = Application.version;
    }

    public string version;
    public List<TrackedObject.Data> objects;
}

[Serializable]
public struct RoomConfiguration
{
    public RoomConfiguration(string versionString) // c# 9 limitation, can remove parameter if this ever gets to c# 11
    {
        version = Application.version;
        roomDimension = default;
        collections = null;
    }

    public string version;
    public RoomDimension roomDimension;
    public List<Tracker> collections;
}