using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class FloatingIcon : MonoBehaviour, IPointerClickHandler
{
    private InfrastructureManager _infrastructureManager;
    private RectTransform _spriteRectTransform;
    private float _baseY;

    // TODO: this won't be correct if building has specific state
    // and need to be preserved its old values
    public Infrastructure.BuildingType BuildingType;
    public Image Sprite;
    public float FloatRadius;

    public void OnPointerClick(PointerEventData eventData)
    {
        _infrastructureManager.EnterPlaceFloatingBuilding(this);
    }

    // Start is called before the first frame update
    void Awake()
    {
        _infrastructureManager = GameObject.FindGameObjectWithTag(Util.Tags.InfrastructureManager).GetComponent<InfrastructureManager>();
        _spriteRectTransform = Sprite.GetComponent<RectTransform>();
    }

    public void Init(Image floatingBar)
    {
        transform.SetParent(floatingBar.transform);
        _baseY = _spriteRectTransform.anchoredPosition.y;
        StartCoroutine(Animate());
    }

    private IEnumerator Animate()
    {
        while (true)
        {
            var position = _spriteRectTransform.anchoredPosition;
            position.y = _baseY + Mathf.Sin(Time.time) * FloatRadius;
            _spriteRectTransform.anchoredPosition = position;
            yield return null;
        }
    }
}
