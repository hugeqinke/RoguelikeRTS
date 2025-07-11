using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;

public class UnitController : MonoBehaviour, IComponent
{
    // Spherical selection triggers
    // I could just have a singular OBB trigger instead but that requires some 
    // additional code for the frustrum check, and woudl have
    // problems of its own, i.e. overtextended selection regions
    // on the corners for larger models and slightly more expensive computation
    public List<SphereCollider> SelectionTriggers;
    public GameObject SelectionHighlight;
    public GameObject HealthBar;
    public GameObject BackgroundBar;
    public GameObject Billboard;

    public Config Config;
    public MovementComponent DBG_Movement;

    public bool DBG;
}

[System.Serializable]
public struct Config
{
    // Metadata
    public Owner Owner;
    public float Radius;

    // Movement
    public float MaxSpeed;
    public float Mass;
    public float MaxAcceleration;
    public float TimeHorizon;

    public bool HoldingPosition;

    public float FlockRadius;

    public float FlockWeight;
    public float LateralWeight;
    public float SeparationWeight;
    public float OtherSeparationWeight;

    public float AttackRadius;
    public float AttackSpeed;
    public int Damage;
    public float AttackAnimationLength;
    public int MaxHealth;
}

[System.Serializable]
public struct UnitStatsComponent
{
    public UnitType UnitType;
    public string Name;
    public int MaxHealth;
    public int Health;
    public int Kills;
}

[System.Serializable]
public struct MovementComponent
{
    [Header("Meta")]
    // Meta
    public Owner Owner;
    public float Radius;

    [Header("Movement")]
    // Config
    public float MaxSpeed;
    public float Acceleration;
    public float TimeHorizon;

    [Header("Misc States")]
    // State + Movement heuristics
    public bool Resolved;
    public bool ResolvedCombat;
    public bool HoldingPosition;
    public int SidePreference; // Side preference key: -1 -> left / 0 -> none / 1 -> right
    public float LastMoveTime;

    [Header("Steering")]
    public float FlockRadius;
    public float FlockWeight;
    public float LateralWeight;
    public float SeparationWeight;
    public float OtherSeparationWeight;

    [Header("Combat")]
    // Combat
    public int Target;
    public bool Attacking;
    public float AttackRadius;
    public float AttackSpeed;
    public int MaxHealth;
    public int Health;
    public int Damage;
    public float LastAttackTime;
    public float AttackAnimationLength;
    public bool AttackMoveResolved;
    public float3 AttackMoveDestination;

    // Physics
    public float Mass;
    public float3 Velocity;
    public float3 MoveStartPosition;
    public float3 Position;
    public float3 OldPosition;
    public float3 TargetPosition;
    public float3 StopPosition; // This is where the unit actually stopped
    public float Orientation;

    public float3 PreferredDir;

    public int CurrentGroup;
    public bool DBG;

    public MovementComponent(UnitController unitController)
    {
        Owner = unitController.Config.Owner;
        Radius = unitController.Config.Radius;

        MaxSpeed = unitController.Config.MaxSpeed;
        Acceleration = unitController.Config.MaxAcceleration;
        TimeHorizon = unitController.Config.TimeHorizon;
        LastMoveTime = Mathf.NegativeInfinity;

        Resolved = true;
        ResolvedCombat = true;
        HoldingPosition = unitController.Config.HoldingPosition;
        SidePreference = 0;

        Target = -1;
        Attacking = false;
        AttackRadius = unitController.Config.AttackRadius;
        AttackSpeed = unitController.Config.AttackSpeed;
        Damage = unitController.Config.Damage;
        MaxHealth = unitController.Config.MaxHealth;
        Health = unitController.Config.MaxHealth;
        AttackAnimationLength = unitController.Config.AttackAnimationLength;
        LastAttackTime = -math.INFINITY;
        AttackMoveResolved = true;
        AttackMoveDestination = unitController.transform.position;

        Mass = unitController.Config.Mass;
        Velocity = float3.zero;
        MoveStartPosition = unitController.transform.position;
        Position = unitController.transform.position;
        StopPosition = unitController.transform.position;
        OldPosition = unitController.transform.position;
        TargetPosition = unitController.transform.position;
        Orientation = 0;

        PreferredDir = float3.zero;

        FlockRadius = unitController.Config.FlockRadius;
        FlockWeight = unitController.Config.FlockWeight;
        LateralWeight = unitController.Config.LateralWeight;
        SeparationWeight = unitController.Config.SeparationWeight;
        OtherSeparationWeight = unitController.Config.OtherSeparationWeight;

        CurrentGroup = -1;

        DBG = unitController.DBG;
    }
}

public enum Owner
{
    Player,
    AI
}