using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using EzySlice;

namespace HairStudio
{
    [RequireComponent(typeof(HairSimulation))]
    public class HairRenderer : MonoBehaviour
    {
        private const int LOD_COUNT = 30;

        private ComputeBuffer segmentDefBuffer;
        private HairSimulation simulation;
        private readonly int scalpWorldPositionPropertyID = Shader.PropertyToID("_ScalpWorldPosition");
        private readonly int scalpWorldInverseRotationPropertyID = Shader.PropertyToID("_InverseRotation");
        private readonly int segmentDefinitionsPropertyID = Shader.PropertyToID("_SegmentDefinitions");
        private readonly int segmentsPropertyID = Shader.PropertyToID("_SegmentsForShading");
        private readonly int HairThicknessPropertyID = Shader.PropertyToID("_ThicknessRoot");
        private Material localMaterial;

        private Dictionary<int, List<MeshRenderer>> filtersByLOD = new Dictionary<int, List<MeshRenderer>>();
        private float distanceForMinDetailSqr, distanceForMaxDetailSqr;
        private float materialThickness;

        public Material material;

        [Tooltip("For performance purpose, the material is cloned and any change in the original material won't affect this renderer. Check this to force update every frame.")]
        public bool updateMaterial = false;

        [Tooltip("Tesselation allows to add intermediate vertices in the hair strands.\n 1 will not produce any intermediate vertex.\n Higher values will produce smoother curves at the cost of performances.")]
        [Range(0, 10)] public int tesselation = 0;

        [Tooltip("The distance for maximum detail. At that distance and closer, all strands are drawn with the minimum thickness.")]
        public float distanceForMaxDetail = 0.2f;
        [Tooltip("The distance for minimum detail. At that distance and farer, only a part of the strands are drawn and the thickness is increased.")]
        public float distanceForMinDetail = 10;

        private void Awake() {
            if (material == null) {
                Debug.LogError("The hair renderer does not have a material. The renderer is deactivated.", this);
                enabled = false;
                return;
            }
            simulation = GetComponent<HairSimulation>();
            for (int i = 0; i < LOD_COUNT; i++) {
                filtersByLOD[i] = new List<MeshRenderer>();
            }

            distanceForMinDetailSqr = Mathf.Pow(distanceForMinDetail, 2);
            distanceForMaxDetailSqr = Mathf.Pow(distanceForMaxDetail, 2);

        }

        private void OnValidate() {
            distanceForMinDetailSqr = Mathf.Pow(distanceForMinDetail, 2);
            distanceForMaxDetailSqr = Mathf.Pow(distanceForMaxDetail, 2);
            localMaterial = null;
            if (!material.shader.name.Contains("HairStudio")) {
                Debug.LogError("The material " + material.name + " does not use the HairStudio shader. Material is discarded.", this);
            }
        }

        private void OnEnable() {
#if UNITY_2019_1_OR_NEWER
            RenderPipelineManager.beginCameraRendering += RenderPipelineManager_beginCameraRendering;
            RenderPipelineManager.beginCameraRendering += RenderPipelineManager_beginCameraRendering;
#endif
            Camera.onPreCull -= Camera_onPreCull;
            Camera.onPreCull += Camera_onPreCull;
        }

        private void OnDisable() {
#if UNITY_2019_1_OR_NEWER
            RenderPipelineManager.beginCameraRendering -= RenderPipelineManager_beginCameraRendering;
#endif
            Camera.onPreCull -= Camera_onPreCull;
        }

#if UNITY_2019_1_OR_NEWER
        private void RenderPipelineManager_beginCameraRendering(ScriptableRenderContext src, Camera cam) {
            SetLevelOfDetail(cam);
        }
#endif

        private void Camera_onPreCull(Camera cam) {
            SetLevelOfDetail(cam);
        }

        public    List<Vector3> verts = new List<Vector3>();
        public List<Vector3> normals = new List<Vector3>();
        public List<Vector2> uvs = new List<Vector2>();
        public List<int> indices = new List<int>(); 

        private void Start() {
            if (!simulation.strands.Any()) {
                Debug.LogWarning("The simulation does not contain any strand to render.", this);
                return;
            }

            

            var strandsByLOD = (float)simulation.strands.Count / LOD_COUNT;
            var currentLOD = 0;
            var strandInLOD = 0;

            int strandIndex = 0;
            foreach (var strand in simulation.strands) {
                if (++strandInLOD > strandsByLOD) {
                    // time to switch to next LOD
                    filtersByLOD[currentLOD].Add(BuildFilter(verts, normals, uvs, indices));
                    
                    verts.Clear();
                    normals.Clear();
                    uvs.Clear();
                    indices.Clear();

                    currentLOD++;
                    strandInLOD = 0;
                }
                if (verts.Count > 65000) {
                    filtersByLOD[currentLOD].Add(BuildFilter(verts, normals, uvs, indices));

                    verts.Clear();
                    normals.Clear();
                    uvs.Clear();
                    indices.Clear();
                }
                float strandRate = 0;
                float strandRateStep = 1.0f / (strand.segmentCount - 1);
                float strandIndex01 = (float)strandIndex / simulation.strands.Count;
                for (int segIndex = strand.firstSegmentIndex; segIndex < strand.firstSegmentIndex + strand.segmentCount - 1; segIndex++) {
                    // last segment is ignored
                    /////////////
                    // 0--1  4 //
                    // | /  /| //
                    // |/  / | //
                    // 2  3--5 //
                    /////////////
                    float segmentRate = 0;
                    float segmentRateStep = 1.0f / (tesselation + 1);
                    for (int tess = 0; tess <= tesselation; tess++) {
                        for (int j = 0; j < 6; j++) {
                            int localSegIndex = segIndex;
                            float localStrandRate = strandRate + strandRateStep * segmentRate;
                            float localSegmentRate = segmentRate;
                            float isRootSide = 1;
                            int side = j == 0 || j == 1 || j == 4 ? 1 : -1;
                            if (j == 1 || j == 4 || j == 5) {
                                // this vertex is on the tip side.
                                isRootSide = 0;
                                if (tess == tesselation) {
                                    // the vertex belong to the next segment
                                    localSegIndex = segIndex + 1;
                                    localStrandRate = strandRate + strandRateStep;
                                    localSegmentRate = 0;
                                } else {
                                    localSegIndex = segIndex;
                                    localStrandRate = strandRate + strandRateStep * (segmentRate + segmentRateStep);
                                    localSegmentRate = segmentRate + segmentRateStep;
                                }
                            }
                            verts.Add(new Vector3(strandIndex01, localSegmentRate, isRootSide));
                            normals.Add(new Vector3(localSegIndex, localStrandRate, side));
                            uvs.Add(Vector2.zero);
                            indices.Add(verts.Count - 1);
                        }
                        segmentRate += segmentRateStep;
                    }
                    strandRate += strandRateStep;
                }
                strandIndex++;
            }
            filtersByLOD[currentLOD].Add(BuildFilter(verts, normals, uvs, indices));
           // this.gameObject.GetComponent<MeshFilter>().sharedMesh = BuildFilter(verts, normals, uvs, indices).gameObject.GetComponent<MeshFilter>().mesh;


            var defs = new List<SegmentDef>();
            int counter = 0;
            foreach (var strand in simulation.strands) {
                var roughnessRate = UnityEngine.Random.value;
                var eccentricityRate = UnityEngine.Random.value;
                for(int i = strand.firstSegmentIndex; i < strand.firstSegmentIndex + strand.segmentCount - 1; i++) {
                    if(i < 0 || i > simulation.segments.Count - 1) {
                        Debug.LogError("index out of bound " + i + " count = " + simulation.segments.Count + " strand " + counter);
                        Debug.LogError("strand.firstSegmentIndex " + strand.firstSegmentIndex);
                        Debug.LogError("strand.segmentCount" + strand.segmentCount);
                    }
                    var seg = simulation.segments[i];
                    var sd = new SegmentDef();
                    sd.initialLocalPos = seg.initialLocalPos;
                    sd.roughnessRate = roughnessRate;
                    sd.eccentricityRate = eccentricityRate;
                    defs.Add(sd);
                }
                counter++;
            }

            segmentDefBuffer = new ComputeBuffer(defs.Count, sizeof(float) * 3 + sizeof(float) + sizeof(float));
            segmentDefBuffer.SetData(defs.ToArray());
        }

        private void SetLevelOfDetail(Camera cam) {
            if (localMaterial == null) return;
            var sqrDistance = (cam.transform.position - transform.position).sqrMagnitude;
            var distanceRateForLOD = Mathf.InverseLerp(distanceForMaxDetailSqr, distanceForMinDetailSqr, sqrDistance);
            var LOD = Mathf.Lerp(1, LOD_COUNT - 1, 1 - distanceRateForLOD);

            localMaterial.SetFloat(HairThicknessPropertyID, materialThickness * (LOD_COUNT - LOD));

            LOD = Mathf.FloorToInt(LOD);
            int i = 0;
            foreach (var filterLOD in filtersByLOD.Values) {
                var enabled = i++ <= LOD;
                foreach (var filter in filterLOD) {
                    filter.enabled = enabled;
                    
                }
            }
        }

        private void LateUpdate() {
            if (localMaterial == null || updateMaterial) {
                UpdateMaterial();
            }
            localMaterial.SetMatrix(scalpWorldInverseRotationPropertyID, Matrix4x4.TRS(Vector3.zero, Quaternion.Inverse(transform.rotation), Vector3.one));
            localMaterial.SetVector(scalpWorldPositionPropertyID, transform.position);
        }

        public void UpdateMaterial() {
            if (material == null) {
                localMaterial = null;
                return;
            } else if(localMaterial == null) {
                localMaterial = new Material(material);
            } else {
                localMaterial.CopyPropertiesFromMaterial(material);
            }
            materialThickness = material.GetFloat(HairThicknessPropertyID);
            localMaterial.SetBuffer(segmentDefinitionsPropertyID, segmentDefBuffer);
            localMaterial.SetBuffer(segmentsPropertyID, simulation.segmentForShadingBuffer);

            foreach(var go in filtersByLOD.Values.SelectMany(l => l)) {
                var mr = go.GetComponent<MeshRenderer>();
                mr.material = localMaterial;
               
            }
        }

      

      
        public MeshRenderer BuildFilter(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices) {
            var mesh = new Mesh();
            mesh.vertices = verts.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = indices.ToArray();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one);

            var go = UOUtility.Create("Hair mesh", gameObject, typeof(MeshRenderer));
            go.hideFlags = HideFlags.HideAndDontSave;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

           
           



            return go.GetComponent<MeshRenderer>();
        }



       
        



        private struct SegmentDef
        {
            public Vector3 initialLocalPos;
            public float roughnessRate;
            public float eccentricityRate;
        }

        private void OnDestroy() {
            segmentDefBuffer?.Release();
        }
    }
}


