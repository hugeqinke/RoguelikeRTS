using UnityEngine;
using UnityEngine.EventSystems;

public class UnitBuildIcon : MonoBehaviour, IPointerClickHandler
{
    public UnitType UnitType;
    private InfrastructureManager _infrastructureManager;

    private void Start()
    {
        _infrastructureManager = GameObject
            .FindGameObjectWithTag(Util.Tags.InfrastructureManager)
            .GetComponent<InfrastructureManager>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        _infrastructureManager.CreateUnit(UnitType);
    }
}
