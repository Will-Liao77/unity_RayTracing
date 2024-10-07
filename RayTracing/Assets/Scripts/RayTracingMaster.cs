using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;
using UnityEngine.Rendering;

public class RayTracingMaster : MonoBehaviour
{
    // public variables
    public ComputeShader RayTracingShader;

    // should render flag communicate with ObjFileBrowser.cs to check if the user has loaded a file
    public bool _shouldRender = false;

    // private variables
    // Render texture
    private RenderTexture _target;
    private RenderTexture _converged;

    // camera
    private Camera _camera;

    // check render complete
    private bool _isRenderComplete = false;

    // point light
    private Light _pointLight;

    // BVH variables
    private BVH.Node[] _bvhNodes;
    private ComputeBuffer _BvhNodesBuffer;
    private ComputeBuffer _BvhTriangleBuffer;

    // mesh variables
    private static bool _meshObjectsNeedRebuilding = false;
    private static List<RayTracingObject> _rayTracingObjects = new List<RayTracingObject>();
    private ComputeBuffer _ModelBuffer;
    private CommandBuffer _command;
    MeshInfo[] _meshInfo;

    // Structures
    struct Matrial
    {
        public Vector4 albedo;
        public Vector4 specular;
        public float smoothness;
        public Vector4 emission;
    }
    struct MeshInfo
    {
        public int nodeOffset;
        public int triangleOffset;
        public Matrix4x4 localToWorldMatrix;
        public Matrix4x4 worldToLocalMatrix;
        public Matrial material;
    }

    private void Start()
    {
        if (RayTracingShader == null)
        {
            Debug.LogError("no computer shader load");
        }

        _command = new CommandBuffer();
        _command.name = "Ray Tracing";

        //Debug.Log(_shouldRender);
    }
    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        SetUpScene();
    }
    private void OnDisable()
    {
        // Release BVH
        _BvhNodesBuffer?.Release();
    }

    // set parameters to compute shader
    private void SetShaderParameters()
    {
        // set the camera's matrices to compute shader
        RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        RayTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);
        RayTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));

        // set the light's position to compute shader
        Vector3 l = _pointLight.transform.position;
        RayTracingShader.SetVector("_PointLightPos", new Vector3(l.x, l.y, l.z));
        RayTracingShader.SetVector("_PointLightProperties", new Vector4(_pointLight.color.r, _pointLight.color.g, _pointLight.color.b, _pointLight.intensity));

        // set BVH data to compute shader
        SetComputeBuffer("_BvhNodesBuffer", _BvhNodesBuffer);
        SetComputeBuffer("_BvhTriangleBuffer", _BvhTriangleBuffer);

        // set mesh data to compute shader
        SetComputeBuffer("_MeshInfoBuffer", _ModelBuffer);
    }

    public static void RegisterObject(RayTracingObject obj)
    {
        _rayTracingObjects.Add(obj);
        _meshObjectsNeedRebuilding = true;
    }
    public static void UnregisterObject(RayTracingObject obj)
    {
        _rayTracingObjects.Remove(obj);
        _meshObjectsNeedRebuilding = true;
    }

    // setup scene
    private void SetUpScene()
    {
        // instatiate pointlight
        GameObject pointLight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        pointLight.name = "Point Light";
        pointLight.transform.position = new Vector3(-7.4f, -10.13f, 0.2f);
        pointLight.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
        pointLight.AddComponent<Light>();
        pointLight.GetComponent<Light>().intensity = 2.0f;
        pointLight.GetComponent<Light>().color = Color.white;
        pointLight.GetComponent<Light>().range = 11.34f;
        _pointLight = pointLight.GetComponent<Light>();
    }

    private MeshDataLists CreateAllMeshData()
    {
        MeshDataLists allData = new MeshDataLists();
        Dictionary<Mesh, (int nodeOffset, int triOffset)> meshLookup = new Dictionary<Mesh, (int nodeOffset, int triOffset)>();

        foreach (RayTracingObject obj in _rayTracingObjects)
        {
            Mesh mesh = obj.GetComponent<MeshFilter>().sharedMesh;

            // cheange local to world matrix(if not changed, the result will be error)
            Matrix4x4 localToWorldMatrix = obj.transform.localToWorldMatrix;

            if (!meshLookup.ContainsKey(mesh))
            {
                meshLookup.Add(mesh, (allData.nodes.Count, allData.triangles.Count));

                // cheange local to world matrix
                Vector3[] worldVertices = mesh.vertices.Select(v => localToWorldMatrix.MultiplyPoint3x4(v)).ToArray();
                Vector3[] worldNormals = mesh.normals.Select(n => localToWorldMatrix.MultiplyVector(n).normalized).ToArray();

                BVH bvh = new BVH(worldVertices, mesh.triangles, worldNormals);
                allData.triangles.AddRange(bvh.GetTriangles());
                allData.nodes.AddRange(bvh.GetNodes());
            }

            MeshRenderer meshRenderer = obj.GetComponent<MeshRenderer>();
            Material material = meshRenderer.sharedMaterial;
            allData.meshInfo.Add(new MeshInfo()
            {
                nodeOffset = meshLookup[mesh].nodeOffset,
                triangleOffset = meshLookup[mesh].triOffset,
                localToWorldMatrix = obj.transform.localToWorldMatrix,
                worldToLocalMatrix = obj.transform.worldToLocalMatrix,
                material = new Matrial
                {
                    albedo = material.GetVector("_Color"),
                    specular = material.GetVector("_SpecColor"),
                    smoothness = material.GetFloat("_Glossiness"),
                    emission = material.GetVector("_EmissionColor")
                }
            });
        }

        return allData;
    }

    // Rebuild the mesh object buffers
    private void RebuildMeshObjectBuffers()
    {
        if (!_meshObjectsNeedRebuilding)
        {
            return;
        }

        _meshObjectsNeedRebuilding = false;

        //for debug
        //int totalTriangleCount = 0;
        //Debug.Log("Triangle count: " + totalTriangleCount);

        MeshDataLists allData = CreateAllMeshData();

        // set mesh info
        _meshInfo = allData.meshInfo.ToArray();

        if (allData.meshInfo.Count == 0)
        {
            Debug.LogWarning("No Objects to trace.");
            return;
        }

        _bvhNodes = allData.nodes.ToArray();

        CreateComputeBuffer(ref _BvhNodesBuffer, allData.nodes, 32);
        CreateComputeBuffer(ref _BvhTriangleBuffer, allData.triangles, 72);
        CreateComputeBuffer(ref _ModelBuffer, _meshInfo.ToList(), 188);

        RayTracingShader.SetInt("_BvhNodeCount", allData.nodes.Count);
        RayTracingShader.SetInt("_BvhTriangleCount", allData.triangles.Count);
    }

    private static void CreateComputeBuffer<T>(ref ComputeBuffer buffer, List<T> data, int stride) where T : struct
    {
        if (buffer != null)
        {
            if (data.Count == 0 || buffer.count != data.Count || buffer.stride != stride)
            {
                buffer.Release();
                buffer = null;
            }
        }

        if (data.Count != 0)
        {
            if (buffer == null)
            {
                buffer = new ComputeBuffer(data.Count, stride);
            }
            buffer.SetData(data);
        }
    }

    private void SetComputeBuffer(string name, ComputeBuffer buffer)
    {
        if (buffer != null)
        {
            RayTracingShader.SetBuffer(0, name, buffer);
        }
    }

    private void InitRenderTexture()
    {
        if (_target == null || _target.width != Screen.width || _target.height != Screen.height)
        {
            // Release render texture if we already have one
            if (_target != null)
            {
                _target.Release();
            }

            // Get a render target for Ray Tracing
            _target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _target.enableRandomWrite = true;
            _target.Create();

            // convered for waht?
            _converged = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _converged.enableRandomWrite = true;
            _converged.Create();
        }
    }

    // vizualize BVH
    //void OnDrawGizmos()
    //{
    //    if (_bvhNodes == null) return;

    //    Gizmos.color = Color.green;

    //    foreach (var node in _bvhNodes)
    //    {
    //        Vector3 center = (node._boundsMin + node._boundsMax) / 2;
    //        Vector3 size = node._boundsMax - node._boundsMin;

    //        Gizmos.DrawWireCube(center, size);
    //    }
    //}

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (_shouldRender && !_isRenderComplete)
        {
            RebuildMeshObjectBuffers();
            SetShaderParameters();
            InitRenderTexture();

            // command buffer setting
            _command.Clear();
            _command.SetComputeTextureParam(RayTracingShader, 0, "Result", _target);
            _command.DispatchCompute(RayTracingShader, 0, Mathf.CeilToInt(_target.width / 8.0f), Mathf.CeilToInt(_target.height / 8.0f), 1);
            _command.Blit(_target, destination);
            _command.Blit(destination, _converged);
            Graphics.ExecuteCommandBuffer(_command);

            _isRenderComplete = true;
        }
        else if (_isRenderComplete)
        {
            Graphics.Blit(destination, _converged);
        }
        else
        {
            Graphics.Blit(source, destination);
        }
    }

    // class
    class MeshDataLists
    {
        public List<Triangle> triangles = new List<Triangle>();
        public List<BVH.Node> nodes = new List<BVH.Node>();
        public List<MeshInfo> meshInfo = new List<MeshInfo>();
    }
}