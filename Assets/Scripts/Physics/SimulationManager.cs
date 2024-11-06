using UnityEngine;

public class SimulationManager : MonoBehaviour {
    [Header("Dependencies")]
    public ComputeShader physicsComputeShader;
    public ReflectionProbe reflectionProbe;

    [Header("Generation Parameters")]
    public int generateCount = 1_000;
    public Vector3 generateDepthScale = new(1f, 0.2f, 1f);
    [Range(0f, 100_000f)] public float generateRadius = 1_500f;
    [Range(0f, 100f)] public float generateMaxVelOffset = 30f;
    [Range(0f, 1_000_000_000f)] public float generateGCMass = 800_000_000f;

    public Vector2 genMassRange = new(1_000f, 500_000f);
    public float genReferenceMass = 10_000f;
    public float genMassIntegrateStep = 1f;
    public float genReferenceTemperature = 5778f;
    [Range(0f, 1f)] public float genTemperatureVariability = 0.05f;
    public float genReferenceRadius = 0.05f;
    [Range(0f, 1f)] public float genRadiusVariability = 0.05f;

    public AnimationCurve generateRadiusRemap;

    [Header("Simulation Parameters")]
    public float gConstant = 100f;

    [Header("Rendering")]
    public float starRadiusMul = 2f;
    public float starRadiusExp = 0.5f;
    public float luminosityScale = 0.01f;
    public float luminosityTemperatureScale = 0.001f;
    
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
        datBufId = Shader.PropertyToID("_Data"),
        outBufId = Shader.PropertyToID("_Out");

    private ComputeBuffer datBuffer, outBuffer;
    private MaterialPropertyBlock propertyBlock;
    private int applyGravKernelId, moveStepKernelId;
    private uint thdGroupSize;
    private BodyData[] bodies;

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

                Vector3 v = Random.insideUnitSphere;
                v *= generateRadius * generateRadiusRemap.Evaluate(v.magnitude);
                v.x *= generateDepthScale.x;
                v.y *= generateDepthScale.y;
                v.z *= generateDepthScale.z;
                bodies[i].position = transform.position + v;
                bodies[i].velocity = generateMaxVelOffset * Random.insideUnitSphere;

                bodies[i].mass = Mathf.Lerp(genMassRange.x, genMassRange.y, massSampler.Sample(Random.value));
                bodies[i].temperature = genReferenceTemperature * Mathf.Sqrt(bodies[i].mass / genReferenceMass);
                bodies[i].temperature *= Random.Range(1f - genTemperatureVariability, 1f + genTemperatureVariability);
                bodies[i].radius = genReferenceRadius * Mathf.Pow(bodies[i].mass / genReferenceMass, 0.8f);
                bodies[i].radius *= Random.Range(1f - genRadiusVariability, 1f + genRadiusVariability);
                bodies[i].luminosity = Mathf.Pow(bodies[i].radius, 2f) * Mathf.Pow(bodies[i].temperature * luminosityTemperatureScale, 4f);
            }

            System.Array.Sort(bodies, (a, b) =>
                a.position.sqrMagnitude.CompareTo(b.position.sqrMagnitude)
            );

            float totalMass = bodies[0].mass;
            float totalStellarMass = 0f;
            int[] histogram = new int[10];
            for(int i = 1; i < bodies.Length; i++) {
                Vector3 rv = Vector3.Cross(Vector3.up, bodies[i].position).normalized;
                bodies[i].velocity += Mathf.Sqrt(gConstant * totalMass / bodies[i].position.magnitude) * rv;

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

        physicsComputeShader.SetFloat(radiusMulId, starRadiusMul);
        physicsComputeShader.SetFloat(radiusExpId, starRadiusExp);
        physicsComputeShader.SetFloat(luminMulId, luminosityScale);

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

    private void Awake() {
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
