using System;
using UnityEngine;
using UnityEngine.Timeline;

namespace JunaidIRF.SyncedInteractions.Editor
{
    /// <summary>A Timeline marker for one of the interaction's events (made and read by the stage).</summary>
    [Serializable, System.ComponentModel.DisplayName("Interaction Event")]
    public class InteractionEventMarker : Marker
    {
        [HideInInspector] public string eventId;
    }
}
