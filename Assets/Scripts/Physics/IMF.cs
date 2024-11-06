using UnityEngine;

public static class IMF {
    public static float Kroupa(float mass, float referenceMass = 1f) {
        if(mass < 0.08f * referenceMass) {
            return Mathf.Pow(mass, -0.3f);
        }else if(mass < 0.5f * referenceMass) {
            return Mathf.Pow(mass, -1.3f);
        } else {
            return Mathf.Pow(mass, -2.3f);
        }
    }
}
