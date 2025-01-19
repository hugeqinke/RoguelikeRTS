using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class BuildingSelectButton : MonoBehaviour, IPointerClickHandler
{
    public Infrastructure.BuildingType BuildingType;
    public InfrastructureManager InfrastructureManager;

    public void OnPointerClick(PointerEventData eventData)
    {
        InfrastructureManager.EnterPlaceBuildingState(BuildingType);
    }
}
