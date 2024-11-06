#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
struct BodyData {
    float3 position;
    float mass;
    float3 velocity;
    float radius;
    float temperature;
    float luminosity;

    float apparentRadius;
    float apparentLuminosity;
};

StructuredBuffer<BodyData> _Data;
#endif

void ConfigureProcedural() {
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    #define unity_ObjectToWorld unity_ObjectToWorld
    
    BodyData dat = _Data[unity_InstanceID];
    
    unity_ObjectToWorld = 0.0;
    unity_ObjectToWorld._m03_m13_m23_m33 = float4(dat.position, 1.0);
    unity_ObjectToWorld._m00_m11_m22 = dat.apparentRadius;
#endif
}

void GetInstanceData_float(float3 In, uint InstanceID, out float3 Out, out float3 Position, out float Temperature, out float Luminosity) {
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    BodyData dat = _Data[InstanceID];
    Position = dat.position;
    Temperature = dat.temperature;
    Luminosity = dat.apparentLuminosity;
#else
    Position = 0.0;
    Temperature = 5000.0;
    Luminosity = 10.0;
#endif
    
    Out = In;
}

void GetInstanceData_half(half3 In, uint InstanceID, out half3 Out, out half3 Position, out half Temperature, out float Luminosity) {
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    BodyData dat = _Data[InstanceID];
    Position = half3(dat.position);
    Temperature = half(dat.temperature);
    Luminosity = half(dat.apparentLuminosity);
#else
    Position = 0.0;
    Temperature = 5000.0;
    Luminosity = 10.0;
#endif
    
    Out = In;
}

