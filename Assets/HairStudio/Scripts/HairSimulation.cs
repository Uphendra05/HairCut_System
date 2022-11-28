using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HairStudio
{
    public class HairSimulation : MonoBehaviour
    {
        private readonly int gravityPropertyID = Shader.PropertyToID("_Gravity");
        private readonly int dragPropertyID = Shader.PropertyToID("_Drag");
        private readonly int radiusPropertyID = Shader.PropertyToID("_Radius");
        private readonly int localStiffnessPropertyID = Shader.PropertyToID("_LocalStiffness");
        private readonly int globalStiffnessStartPropertyID = Shader.PropertyToID("_GlobalStiffnessStart");
        private readonly int globalStiffnessEndPropertyID = Shader.PropertyToID("_GlobalStiffnessEnd");
        private readonly int lengthIterationCountPropertyID = Shader.PropertyToID("_LengthIterationCount");
        private readonly int stiffnessIterationCountPropertyID = Shader.PropertyToID("_StiffnessIterationCount");
        private readonly int voxelSizePropertyID = Shader.PropertyToID("_VoxelSize");
        private readonly int gridResolutionPropertyID = Shader.PropertyToID("_GridResolution");
        private readonly int frictionPropertyID = Shader.PropertyToID("_Friction");
        private readonly int repulsionPropertyID = Shader.PropertyToID("_Repulsion");
        private readonly int strandCountPropertyID = Shader.PropertyToID("_StrandCount");
        private readonly int scalpRotationPropertyID = Shader.PropertyToID("_ScalpRotation");
        private readonly int scalpTransformPropertyID = Shader.PropertyToID("_ScalpTransform");
        private readonly int strandsPropertyID = Shader.PropertyToID("_Strands");
        private readonly int segmentsPropertyID = Shader.PropertyToID("_Segments");
        private readonly int segmentsForShadingPropertyID = Shader.PropertyToID("_SegmentsForShading");
        private readonly int forcePropertyID = Shader.PropertyToID("_Force");
        private readonly int deltaTimePropertyID = Shader.PropertyToID("_DeltaTime");
        private readonly int centerPropertyID = Shader.PropertyToID("_Center");
        private readonly int velocityGridPropertyID = Shader.PropertyToID("_VelocityGrid");
        private readonly int densityGridPropertyID = Shader.PropertyToID("_DensityGrid");
        private readonly int colliderInfosPropertyID = Shader.PropertyToID("_ColliderInfos");
        private readonly int extrapolatePropertyID = Shader.PropertyToID("_Extrapolate");
        private readonly int extrapolationTimePropertyID = Shader.PropertyToID("_ExtrapolatationTime");
        private readonly int useDFTLPropertyID = Shader.PropertyToID("_UseDFTL");
        private readonly int lockedPropertyID = Shader.PropertyToID("_Locked");
        private readonly int colliderCountPropertyID = Shader.PropertyToID("_ColliderCount");

        private int physicStepKernelID, renderingStepKernelID, resetCohesionMapKernelID, buildCohesionMapKernelID;
        private readonly List<SegmentDTO> segmentDTOs = new List<SegmentDTO>();
        private readonly List<StrandDTO> strandDTOs = new List<StrandDTO>();
        private readonly Dictionary<SphereCollider, ColliderInfo> colliderInfos = new Dictionary<SphereCollider, ColliderInfo>();
        private float hairRange;
        private bool isPhysicDirty = true, physicRanThisTurn = false, locked = false;
        private int kernelThreadCount;


        [NonSerialized] public ComputeBuffer strandBuffer;
        [NonSerialized] public ComputeBuffer segmentBuffer;
        [NonSerialized] public ComputeBuffer segmentForShadingBuffer;
        [NonSerialized] public ComputeBuffer velocityGridBuffer;
        [NonSerialized] public ComputeBuffer densityGridBuffer;
        [NonSerialized] public ComputeBuffer colliderBuffer;

        [NonSerialized] public List<Strand> strands = new List<Strand>();
        [NonSerialized] public List<StrandSegment> segments = new List<StrandSegment>();

        [HideInInspector] public ComputeShader computeShader;

        [Header("Physics")]
        [Tooltip("The minimum distance between strands and colliders.")]
        [Range(0.001f, 0.05f)] public float collisionDistance = 0.01f;

        [Tooltip("The weight of the strands.")]
        [Range(0, 0.01f)] public float weight = 0.001f;

        [Tooltip("The drag force (damping) that will slow the strands, representing the friction of the air with the medium. Greater values can be used to simulate hair in water.")]
        [Range(0, 10)] public float drag = 0.1f;

        [Tooltip("The stiffness of the hair strand, from a segment to the next. Great values may make the simulation unstable. Try to increase the number of iterations in that case.")]
        [Range(0, 0.8f)] public float localStiffness = 0.1f;

        [Tooltip("Global stiffness brings the strands back in its position relatively to the scalp. This is the distance from the root at which the global stiffness starts to decrease.")]
        [Range(0, 1)] public float globalStiffnessStart = 0f;
        [Tooltip("Global stiffness brings the strands back in its position relatively to the scalp. This is the distance from the root at which there is no more global stiffness.")]
        [Range(0, 2)] public float globalStiffnessEnd = 0.3f;

        [Header("Simulation")]
        [Tooltip("The number of iterations made to preserve strand segment correct length. 1 or 2 iterations are generally enough.")]
        [Range(1, 20)] public int lengthIterations = 2;

        [Tooltip("The number of iterations made to apply local stiffness. The more segments per strand, the more iterations are required to get stable simulation at the cost of performances.")]
        [Range(1, 20)] public int stiffnessIterations = 5;

        [ReadOnlyProperty] public Vector3 externalForce;

        [Header("Hair/hair")]
        [Delayed, HideInInspector] public float voxelSize = 0.05f;
        [Delayed, HideInInspector] public int gridResolution = 32;

        [Tooltip("The hair/hair friction, used to make the strands move together for more realism.")]
        [Range(0, 0.5f)] public float friction = 0.05f;

        [Tooltip("The hair/hair repulsion, used to temporarily add artifical volume (electrocution, fear, hair dryer...)")]
        [Range(0, 400)] public float repulsion = 0f;

        [Tooltip("Allows the hair position to be anticipated when several frames are rendered between two physics steps. It will prevent scalp penetration at the cost of some jitter. Use this if you have high accelerations.")]
        public bool extrapolate = false;

        [Tooltip("A different method that will enforce the hair length at the cost of stiffness stability. Note that changing the number of length iterations does not have any effect with this method.")]
        public bool inextensibleHair = false;

        [Header("Debug")]
        [Tooltip("The current sphere colliders actually in the range of the hair.")]
        [ReadOnlyProperty, SerializeField] private int currentColliderCount;

        private void Awake() {
            SetShaderConstants();
        }

        private void OnValidate() {
            globalStiffnessStart = Mathf.Clamp(globalStiffnessStart, 0, globalStiffnessEnd);
            globalStiffnessEnd = Mathf.Clamp(globalStiffnessEnd, globalStiffnessStart, 2);

            SetShaderConstants();
        }

        private void Start() {
            if (!strands.Any()) {
                Debug.LogWarning("The simulation does not contain any strand to simulate. Simulation is deactivated.", this);
                enabled = false;
                return;
            }
            if (!segments.Any()) throw new Exception("The simulation contains strands, but no segments, which is abnormal.");

            // we compute the max hair range
            // note that the last segment may have a smallest length, so the range is not exact.
            var scaleFactor = transform.lossyScale.x + transform.lossyScale.x + transform.lossyScale.z;
            scaleFactor /= 3;
            hairRange = strands.Max(strand => {
                var firstSegment = segments[strand.firstSegmentIndex];
                var length = firstSegment.length * strand.segmentCount / scaleFactor;
                var distanceToScalp = firstSegment.initialLocalPos.magnitude;
                return length + distanceToScalp;
            });
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.radius = hairRange;
            sc.isTrigger = true;
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            // strand buffer
            strandDTOs.AddRange(strands.Select(strand => new StrandDTO(strand)));
            strandBuffer = new ComputeBuffer(strands.Count, StrandDTO.SIZE);
            strandBuffer.SetData(strandDTOs);

            // segment buffer
            segmentDTOs.AddRange(segments.Select(seg => new SegmentDTO(seg)));
            segmentBuffer = new ComputeBuffer(segmentDTOs.Count, SegmentDTO.SIZE);
            segmentBuffer.SetData(segmentDTOs);

            // segment for shading buffer
            segmentForShadingBuffer = new ComputeBuffer(segmentDTOs.Count,
                sizeof(float) * 3 +
                sizeof(float) * 3 +
                sizeof(float) * 3);

            SetShaderConstants();

            // velocity grid buffer
            velocityGridBuffer = new ComputeBuffer(gridResolution * gridResolution * gridResolution, sizeof(int) * 4);

            //density grid buffer
            densityGridBuffer = new ComputeBuffer(gridResolution * gridResolution * gridResolution, sizeof(int));

            // collider buffer
            // initialized to 1 because an empty buffer is not possible
            colliderBuffer = new ComputeBuffer(1, ColliderDTO.SIZE);

            // kernel
            physicStepKernelID = computeShader.FindKernel("PhysicStep");
            renderingStepKernelID = computeShader.FindKernel("RenderingStep");
            resetCohesionMapKernelID = computeShader.FindKernel("ResetCohesionMap");
            buildCohesionMapKernelID = computeShader.FindKernel("BuildCohesionMap"); 
            kernelThreadCount = (int)Mathf.Ceil((float)strands.Count / 64);
        }

        private void SetShaderConstants() {
            if (computeShader == null) return;
            computeShader.SetFloat(gravityPropertyID, Physics.gravity.y * weight);
            computeShader.SetFloat(dragPropertyID, drag);
            computeShader.SetFloat(radiusPropertyID, collisionDistance);
            computeShader.SetFloat(localStiffnessPropertyID, localStiffness);
            computeShader.SetFloat(globalStiffnessStartPropertyID, globalStiffnessStart);
            computeShader.SetFloat(globalStiffnessEndPropertyID, globalStiffnessEnd);
            computeShader.SetInt(lengthIterationCountPropertyID, lengthIterations);
            computeShader.SetInt(stiffnessIterationCountPropertyID, stiffnessIterations);
            computeShader.SetFloat(voxelSizePropertyID, voxelSize);
            computeShader.SetInt(gridResolutionPropertyID, gridResolution);
            computeShader.SetFloat(frictionPropertyID, friction);
            computeShader.SetFloat(repulsionPropertyID, repulsion);
            computeShader.SetInt(strandCountPropertyID, strands.Count);
            computeShader.SetBool(extrapolatePropertyID, extrapolate);
            computeShader.SetBool(useDFTLPropertyID, inextensibleHair);
            computeShader.SetBool(lockedPropertyID, locked);
        }

        private void LateUpdate() {
            physicRanThisTurn = false;
            if (isPhysicDirty) {
                PhysicPass();
            }
            if (!physicRanThisTurn) {
                SetShaderConstants();

                computeShader.SetFloat(extrapolationTimePropertyID, (Time.time - Time.fixedTime) / Time.fixedDeltaTime);
                
                computeShader.SetMatrix(scalpTransformPropertyID, transform.localToWorldMatrix);
                computeShader.SetVector(scalpRotationPropertyID, QuaternionUtility.ToVector4(transform.rotation));

                computeShader.SetBuffer(renderingStepKernelID, strandsPropertyID, strandBuffer);
                computeShader.SetBuffer(renderingStepKernelID, segmentsPropertyID, segmentBuffer);
                computeShader.SetBuffer(renderingStepKernelID, segmentsForShadingPropertyID, segmentForShadingBuffer);

                // colliders
                int colliderCount;
                if (colliderInfos.Values.Any()) {
                    colliderBuffer.SetData(colliderInfos.Values.Select(ci => new ColliderDTO(ci)).ToList());
                    colliderCount = colliderInfos.Values.Count;
                } else {
                    colliderBuffer.SetData(new List<ColliderDTO>() { new ColliderDTO() });
                    colliderCount = 0;
                }
                computeShader.SetBuffer(renderingStepKernelID, colliderInfosPropertyID, colliderBuffer);
                computeShader.SetInt(colliderCountPropertyID, colliderCount);

                computeShader.Dispatch(renderingStepKernelID, kernelThreadCount, 1, 1);
            }
        }

        private void FixedUpdate() {
            if (isPhysicDirty) {
                PhysicPass();
            }
            isPhysicDirty = true;
        }

        private void PhysicPass() {
            isPhysicDirty = false;
            physicRanThisTurn = true;
            SetShaderConstants();
            
            // setting changing values
            computeShader.SetVector(forcePropertyID, externalForce);
            externalForce = Vector3.zero;
            computeShader.SetMatrix(scalpTransformPropertyID, transform.localToWorldMatrix);
            computeShader.SetVector(scalpRotationPropertyID, QuaternionUtility.ToVector4(transform.rotation));
            computeShader.SetFloat(deltaTimePropertyID, Time.fixedDeltaTime);
            computeShader.SetVector(centerPropertyID, transform.position);

            // reset cohesion map
            computeShader.SetBuffer(resetCohesionMapKernelID, velocityGridPropertyID, velocityGridBuffer);
            computeShader.SetBuffer(resetCohesionMapKernelID, densityGridPropertyID, densityGridBuffer);
            computeShader.Dispatch(resetCohesionMapKernelID, (int)Mathf.Ceil(Mathf.Pow(gridResolution, 3) / 64), 1, 1);


            // build cohesion map
            computeShader.SetBuffer(buildCohesionMapKernelID, strandsPropertyID, strandBuffer);
            computeShader.SetBuffer(buildCohesionMapKernelID, segmentsPropertyID, segmentBuffer);
            computeShader.SetBuffer(buildCohesionMapKernelID, velocityGridPropertyID, velocityGridBuffer);
            computeShader.SetBuffer(buildCohesionMapKernelID, densityGridPropertyID, densityGridBuffer);
            computeShader.Dispatch(buildCohesionMapKernelID, kernelThreadCount, 1, 1);

            // simulation
            computeShader.SetBuffer(physicStepKernelID, strandsPropertyID, strandBuffer);
            computeShader.SetBuffer(physicStepKernelID, segmentsPropertyID, segmentBuffer);
            computeShader.SetBuffer(physicStepKernelID, segmentsForShadingPropertyID, segmentForShadingBuffer);

            computeShader.SetBuffer(physicStepKernelID, velocityGridPropertyID, velocityGridBuffer);
            computeShader.SetBuffer(physicStepKernelID, densityGridPropertyID, densityGridBuffer);

            // colliders
            if (colliderInfos.Values.Any()) {
                colliderBuffer.SetData(colliderInfos.Values.Select(ci => new ColliderDTO(ci)).ToList());
            } else {
                colliderBuffer.SetData(new List<ColliderDTO>() { new ColliderDTO() });
            }
            computeShader.SetBuffer(physicStepKernelID, colliderInfosPropertyID, colliderBuffer);

            computeShader.Dispatch(physicStepKernelID, kernelThreadCount, 1, 1);

            locked = false;
        }

        private void OnDestroy() {
            strandBuffer?.Release();
            segmentBuffer?.Release();
            segmentForShadingBuffer?.Release();
            velocityGridBuffer?.Release();
            densityGridBuffer?.Release();
            colliderBuffer?.Release();
        }

        private void OnTriggerEnter(Collider other) {
            var sc = other as SphereCollider;
            if(sc != null && !sc.isTrigger) {
                colliderInfos[sc] = new ColliderInfo() {
                    collider = sc,
                    radius = sc.radius * Mathf.Max(
                        sc.transform.lossyScale.x,
                        sc.transform.lossyScale.y,
                        sc.transform.lossyScale.z),
                };
                UpdateColliderCount();
            }
        }

        private void OnTriggerExit(Collider other) {
            var sc = other as SphereCollider;
            if (sc != null && !sc.isTrigger) {
                if (!colliderInfos.Remove(sc)) {
                    Debug.LogWarning($"The sphere collider named {other.name} exited the hair range but but has never entered, which should not happen.", this);
                }
                colliderInfos.Remove(sc);
                UpdateColliderCount();
            }
        }

        private void UpdateColliderCount() {
            colliderBuffer?.Release();
            colliderBuffer = new ComputeBuffer(Mathf.Max(1, colliderInfos.Keys.Count), ColliderDTO.SIZE);
            currentColliderCount = colliderInfos.Keys.Count;
        }

        /// <summary>
        /// Asks the simulation to place the hair on its original position on the next physic step. Usefull when you have to teleport the scalp.
        /// </summary>
        public void ResetHair() {
            locked = true;
        }
    }
}
