using UnityEngine;

public class SimulationManager : MonoBehaviour {
    [Header("Dependencies")]
    public ComputeShader physicsComputeShader;
    public ReflectionProbe reflectionProbe;

    [Header("Galaxy Generation Parameters")]
    [Min(0)] public int generateCount = 1_000;
    public Vector3 generateDepthScale = new(1f, 0.2f, 1f);
    [Range(0f, 100_000f)] public float generateRadius = 1_500f;
    [Range(0f, 100f)] public float generateMaxVelOffset = 30f;
    [Range(0f, 1_000_000_000f)] public float generateGCMass = 800_000_000f;

    [Header("Star Generation Parameters")]
    public Vector2 genMassRange = new(1_000f, 500_000f);
    [Min(0f)] public float genReferenceMass = 10_000f;
    [Min(0f)] public float genMassIntegrateStep = 1f;
    [Min(0f)] public float genReferenceTemperature = 5778f;
    [Range(0f, 1f)] public float genTemperatureVariability = 0.05f;
    [Min(0f)] public float genReferenceRadius = 0.05f;
    [Range(0f, 1f)] public float genRadiusVariability = 0.05f;
    public AnimationCurve generateRadiusRemap;

    [Header("Spiral Arm Generation Parameters")]
    [Min(0)] public int genArmCount = 2;
    [Min(0f)] public float genSpiralStartRadius = 30f;
    [Min(0f)] public float genSpiralLooseness = 0.3f;
    [Range(0f, 1f)] public float genSpiralBias = 0.5f;
    [Min(0f)] public float genSpiralRadiusScatter = 8f;
    [Range(0f, 360f)] public float genSpiralAngularScatter = 15f;
    [Min(1f)] public float genSpiralCoreSmoothing = 10f;

    [Header("Simulation Parameters")]
    [Min(0f)] public float gConstant = 100f;
    [Min(0f)] public float darkMatterCentralDensity;
    [Min(0f)] public float darkMatterScaleRadius;

    [Header("Rendering")]
    [Min(0f)] public float starRadiusMul = 2f;
    [Min(0f)] public float starRadiusExp = 0.5f;
    [Min(0f)] public float luminosityMul = 0.01f;
    [Min(0f)] public float luminosityExp = 0.001f;

    public Bounds renderBounds;
    public Mesh renderMesh;
    public Material renderMaterial;

    [Header("Data")]
    public BodyData[] initialBodies;

    private static readonly int
        sizeId = Shader.PropertyToID("_Size"),
        deltaId = Shader.PropertyToID("_Delta"),
        gConstId = Shader.PropertyToID("_GConst"),
        radiusMulId = Shader.PropertyToID("_RadiusMul"),
        radiusExpId = Shader.PropertyToID("_RadiusExp"),
        luminMulId = Shader.PropertyToID("_LuminMul"),
        luminExpId = Shader.PropertyToID("_LuminExp"),
        darkMatterCentralDensityId = Shader.PropertyToID("_DarkMatterCentralDensity"),
        darkMatterScaleRadiusId = Shader.PropertyToID("_DarkMatterScaleRadius"),
        datBufId = Shader.PropertyToID("_Data"),
        outBufId = Shader.PropertyToID("_Out");

    private ComputeBuffer datBuffer, outBuffer;
    private MaterialPropertyBlock propertyBlock;
    private int applyGravKernelId, moveStepKernelId;
    private uint thdGroupSize;
    private BodyData[] bodies;

    public SimulationManager Instance { get; private set; }

    private void InitializeBodyArray() {
        if(initialBodies.Length == 0) {
            InverseSampler massSampler = new(
                Probability.Cumulative(
                    f => IMF.Kroupa(f, genReferenceMass),
                    genMassRange.x,
                    genMassRange.y,
                    genMassIntegrateStep,
                    true
                )
            );

            bodies = new BodyData[generateCount];
            float maxArmAngle = Mathf.Log(generateRadius / genSpiralStartRadius) / genSpiralLooseness;

            for(int i = 0; i < bodies.Length; i++) {
                if(i == 0) {
                    bodies[0].position = transform.position;
                    bodies[0].velocity = Vector3.zero;
                    bodies[0].mass = generateGCMass;
                    bodies[0].temperature = 0f;
                    bodies[0].radius = 0f;
                    bodies[0].luminosity = 0f;

                    continue;
                }

                Vector3 genPos = Random.insideUnitSphere;
                genPos *= generateRadius * generateRadiusRemap.Evaluate(genPos.magnitude);

                if(genArmCount > 0) {
                    float armOffset = Random.Range(0f, maxArmAngle);
                    float radius = genSpiralStartRadius * Mathf.Exp(armOffset * genSpiralLooseness);
                    armOffset += Random.Range(-genSpiralAngularScatter, genSpiralAngularScatter) * Mathf.Deg2Rad;

                    float armAngle = 2f * Mathf.PI * (i % genArmCount) / genArmCount;
                    float coreSmoothing = 2f * Mathfs.Sigmoid(genSpiralCoreSmoothing * armOffset) - 1f;

                    Vector3 u = radius * coreSmoothing * new Vector3(Mathf.Cos(armOffset), 0f, Mathf.Sin(armOffset));
                    u = Quaternion.Euler(0f, armAngle * Mathf.Rad2Deg, 0f) * u + genSpiralRadiusScatter * Random.insideUnitSphere;

                    genPos.x = Mathf.Lerp(genPos.x, u.x, genSpiralBias);
                    genPos.z = Mathf.Lerp(genPos.z, u.z, genSpiralBias);
                }

                genPos.x *= generateDepthScale.x;
                genPos.y *= generateDepthScale.y;
                genPos.z *= generateDepthScale.z;

                bodies[i].position = genPos;
                bodies[i].velocity = generateMaxVelOffset * Random.insideUnitSphere;

                bodies[i].mass = Mathf.Lerp(genMassRange.x, genMassRange.y, massSampler.Sample(Random.value));
                bodies[i].temperature = genReferenceTemperature * Mathf.Sqrt(bodies[i].mass / genReferenceMass);
                bodies[i].temperature *= Random.Range(1f - genTemperatureVariability, 1f + genTemperatureVariability);
                bodies[i].radius = genReferenceRadius * Mathf.Pow(bodies[i].mass / genReferenceMass, 0.8f);
                bodies[i].radius *= Random.Range(1f - genRadiusVariability, 1f + genRadiusVariability);
                bodies[i].luminosity = Mathf.Pow(bodies[i].radius, 2f) * Mathf.Pow(bodies[i].temperature, 4f);
            }

            System.Array.Sort(bodies, (a, b) =>
                a.position.sqrMagnitude.CompareTo(b.position.sqrMagnitude)
            );

            float totalMass = bodies[0].mass;
            float totalStellarMass = 0f;
            int[] histogram = new int[10];
            for(int i = 1; i < bodies.Length; i++) {
                Vector3 rv = Vector3.Cross(Vector3.up, bodies[i].position).normalized;
                Vector3 accel = GCAcceleration(bodies[i].position, totalMass) + DarkMatterAcceleration(bodies[i].position);

                bodies[i].velocity += Mathf.Sqrt(accel.magnitude * bodies[i].position.magnitude) * rv;

                totalMass += bodies[i].mass;
                totalStellarMass += bodies[i].mass;

                int histIdx = Mathf.FloorToInt(histogram.Length * Mathf.InverseLerp(genMassRange.x, genMassRange.y, bodies[i].mass));
                histogram[histIdx]++;
            }

            for(int i = 0; i < histogram.Length; i++) {
                Debug.Log($"HIST[{i}] = {histogram[i]}");
            }
            Debug.Log($"Average mass: {totalStellarMass / (generateCount - 1)}");
        } else {
            bodies = initialBodies;
        }
    }

    private void InitializeBuffers() {
        datBuffer = new ComputeBuffer(bodies.Length, BodyData.Size);
        outBuffer = new ComputeBuffer(bodies.Length, 3 * sizeof(float));
        propertyBlock = new MaterialPropertyBlock();

        applyGravKernelId = physicsComputeShader.FindKernel("ApplyGravity");
        moveStepKernelId = physicsComputeShader.FindKernel("MoveStep");

        physicsComputeShader.SetBuffer(applyGravKernelId, datBufId, datBuffer);
        physicsComputeShader.SetBuffer(applyGravKernelId, outBufId, outBuffer);
        physicsComputeShader.SetBuffer(moveStepKernelId, datBufId, datBuffer);
        physicsComputeShader.SetBuffer(moveStepKernelId, outBufId, outBuffer);
        renderMaterial.SetBuffer(datBufId, datBuffer);

        datBuffer.SetData(bodies);

        physicsComputeShader.GetKernelThreadGroupSizes(applyGravKernelId, out thdGroupSize, out _, out _);
    }

    private void DisposeBuffers() {
        datBuffer.GetData(bodies);

        datBuffer.Release();
        datBuffer = null;
        outBuffer.Release();
        outBuffer = null;
    }

    private void UpdateGPUKernel() {
        int groupCount = (bodies.Length + (int)thdGroupSize - 1) / (int)thdGroupSize;

        physicsComputeShader.SetInt(sizeId, bodies.Length);
        physicsComputeShader.SetFloat(deltaId, Time.deltaTime);
        physicsComputeShader.SetFloat(gConstId, gConstant);
        physicsComputeShader.SetFloat(darkMatterCentralDensityId, darkMatterCentralDensity);
        physicsComputeShader.SetFloat(darkMatterScaleRadiusId, darkMatterScaleRadius);

        physicsComputeShader.SetFloat(radiusMulId, starRadiusMul);
        physicsComputeShader.SetFloat(radiusExpId, starRadiusExp);
        physicsComputeShader.SetFloat(luminMulId, luminosityMul);
        physicsComputeShader.SetFloat(luminExpId, luminosityExp);

        physicsComputeShader.Dispatch(applyGravKernelId, groupCount, 1, 1);
        physicsComputeShader.Dispatch(moveStepKernelId, groupCount, 1, 1);
    }

    private void DrawBodies() {
        if(reflectionProbe != null && reflectionProbe.realtimeTexture != null) {
            propertyBlock.SetTexture("unity_SpecCube0", reflectionProbe.realtimeTexture);
            propertyBlock.SetTexture("unity_SpecCube1", reflectionProbe.realtimeTexture);
        }

        Graphics.DrawMeshInstancedProcedural(
            renderMesh, 0, renderMaterial, renderBounds, bodies.Length,
            properties: propertyBlock, lightProbeUsage: UnityEngine.Rendering.LightProbeUsage.Off
        );
    }

    private Vector3 GCAcceleration(Vector3 position, float enclosedMass) {
        float r = position.magnitude;
        return enclosedMass * gConstant / (r * r) * -position.normalized;
    }

    private Vector3 DarkMatterAcceleration(Vector3 position) {
        if(position == Vector3.zero || darkMatterCentralDensity == 0f || darkMatterScaleRadius == 0f) {
            return Vector3.zero;
        }

        float r = position.magnitude;
        float enclosedMass = 4f * Mathf.PI * darkMatterCentralDensity * darkMatterScaleRadius * darkMatterScaleRadius * darkMatterScaleRadius
            * (Mathf.Log(1 + r / darkMatterScaleRadius) - (r / (r + darkMatterScaleRadius)));

        return enclosedMass * gConstant / (r * r) * -position.normalized;
    }

    private void Awake() {
        if(Instance != null) {
            Destroy(this);
        }
        Instance = this;

        InitializeBodyArray();
    }

    private void OnEnable() {
        InitializeBuffers();
    }
    
    private void OnDisable() {
        DisposeBuffers();
    }

    private void Update() {
        UpdateGPUKernel();
        DrawBodies();
    }
}
