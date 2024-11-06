using System;
using System.Collections.Generic;
using System.Linq;

public static class Probability {
    public static List<float> Cumulative(Func<float, float> densityFunc, float a, float b, float step, bool normalize) {
        List<float> cdf = new();
        float sum = 0f;

        for(float x = a; x < b; x += step) {
            float value = densityFunc(x);
            
            if(cdf.Count == 0) {
                cdf.Add(value);
            } else {
                cdf.Add(value + cdf[^1]);
            }

            sum += value;
        }

        if(normalize) {
            cdf = cdf
                .Select(f => f / sum)
                .ToList();
        }

        return cdf;
    }
}
