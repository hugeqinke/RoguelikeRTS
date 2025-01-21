using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class OverviewIcon : MonoBehaviour, IPointerClickHandler
{
    public UnitType UnitType;
    public Image Image;
    public TMP_Text Text;

    private PlayerManager _playerManager;

    private bool Selected;
    public Image SelectedHighlight;

    private void Awake()
    {
        _playerManager = GameObject
            .FindGameObjectWithTag(Util.Tags.PlayerManager)
            .GetComponent<PlayerManager>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Selected = !Selected;
        _playerManager.SetFilter(UnitType, Selected);
        SelectedHighlight.gameObject.SetActive(Selected);
    }

    public void Deselect()
    {
        Selected = false;
        SelectedHighlight.gameObject.SetActive(false);
    }
}
