using UnityEngine;

// Physics and rate numbers shared by every day. Starting values are the JS prototype's TUNE.
// Edit Assets/Data/Tuning.asset in the Inspector; changes made during Play mode stick.
[CreateAssetMenu(menuName = "Coal Run/Tuning")]
public class Tuning : ScriptableObject
{
    [Header("World")]
    public float step = 1f / 60;
    public float gravity = -18;
    public float walkSpeed = 5.1f;

    [Header("Cart")]
    public float cartMass = 16;
    public float coalMass = 1.7f;
    public float cartCapacity = 10;
    public float overfillCapacity = 12;

    [Header("Coal")]
    public float hopperCapacity = 30;
    public float handCapacity = 3;
    public float chuteRate = 5;
    public float shovelRate = 2.8f;
    public float pileVolume = .23f;
    public float columnWidth = .4f;

    [Header("Lift")]
    public float liftGear = .53f;
    public float liftKeyCrank = 6.5f;
    public float liftMaxSpeed = 8;
    public float liftAcceleration = 6;

    [Header("Levers")]
    public float leverTravel = 1.6f;

    [Header("Furnace")]
    public float[] burnRates = { .072f, .144f, .204f, .276f, .348f };
    public float[] heldTemperatures = { 8, -4, -14, -26, -38 };
    public float heatLoss = .006f;
    public float heatCapacity = .28f;
    public float comfortable = 20;
    public float freeze = 0;

    [Header("Shift")]
    public float shiftLength = 600;
    public float replacementDelay = 7;
}
