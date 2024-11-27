using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitComponent : MonoBehaviour, IComponent
{
    public GameObject SelectionHighlight;

    // Spherical selection triggers
    // I could just have a singular OBB trigger instead
    // but that requires some additional code, and will have
    // problems of its own, i.e. overtextended selection regions
    // on the corners for larger models
    public List<SphereCollider> SelectionTriggers;
}
