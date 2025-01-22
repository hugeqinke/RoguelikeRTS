using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DeployManager : MonoBehaviour
{
    // Sprites
    public Sprite GhostSprite;
    public Sprite BattleCruiserSprite;
    public Sprite HellionSprite;
    public Sprite MarauderSprite;
    public Sprite MarineSprite;
    public Sprite MedivacSprite;
    public Sprite ReaperSprite;
    public Sprite SiegeTankSprite;

    public Image Overview;
    public Image Roster;

    public DeployRosterIcon DeployRosterIconPrefab;
    public DeployOverviewIcon DeployOverviewPrefab;

    public Dictionary<UnitType, UnitStatsComponent> Units;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }
}
