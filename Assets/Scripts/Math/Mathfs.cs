using UnityEngine;

public static partial class Mathfs {
    public static float Sigmoid(float x) {
        return 1f / (1f + Mathf.Exp(-x));
    }
}
