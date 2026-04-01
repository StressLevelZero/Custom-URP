using System;
using UnityEngine;

namespace SLZ
{
    public sealed class BakedVolumetricsData : ScriptableObject
    {
        [ColorUsage(false, true)]
        public Color meanSkyRadianceLinear = Color.black;
        public int environmentSampleCount;
        public string sourceScenePath;
        public long lastBakeUtcTicks;

        public float seaLevelAltitude = 0;
    }
}