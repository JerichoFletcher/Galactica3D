using UnityEngine;

[System.Serializable]
public struct BodyData {
    public Vector3 position;
    public float mass;
    public Vector3 velocity;
    public float radius;
    public float temperature;
    public float luminosity;

    public float apparentRadius;
    public float apparentLuminosity;

    public static int Size =>
        3 * sizeof(float)
        + sizeof(float)
        + 3 * sizeof(float)
        + sizeof(float)
        + sizeof(float)
        + sizeof(float)
        + sizeof(float)
        + sizeof(float);
}
