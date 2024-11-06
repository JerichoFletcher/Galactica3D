using System.Collections.Generic;

public class InverseSampler {
    private readonly List<float> cdf;

    public InverseSampler(List<float> cdf) {
        this.cdf = cdf;
    }

    public float Sample(float t) {
        int i = cdf.BinarySearch(t);
        if(i < 0) {
            i = ~i;
        }

        float y = (float)i / cdf.Count;
        return y;
    }
}
