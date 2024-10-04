using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;
using UnityEngine.Rendering;

public class RayTracingMaster : MonoBehaviour
{
    // public
    public ComputeShader RayTracingShader;

    // should render flag communicate with ObjFileBrowser.cs to check if the user has loaded a file
    public bool _shouldRender = false;

    // private
    // Render texture
    private RenderTexture _target;
    private RenderTexture _converged;

    // camera
    private Camera _camera;

    // check render complete
    private bool _isRenderComplete = false;

    // point light
    private Light _pointLight;

    // texture for ray tracing
    //private List<Texture2D> _textures = new List<Texture2D>();

    // BVH variables
    private List<BvhNode> _bvhNodes = new List<BvhNode>();
    private ComputeBuffer _BvhBuffer;

    // about mesh variables
    private static bool _meshObjectsNeedRebuilding = false;
    private static List<RayTracingObject> _rayTracingObjects = new List<RayTracingObject>();
    private static List<MeshObject> _meshObjects = new List<MeshObject>();
    private static List<Vector3> _vertices = new List<Vector3>();
    private static List<int> _indices = new List<int>();
    private ComputeBuffer _MeshObjectBuffer;
    private ComputeBuffer _VerticesBuffer;
    private ComputeBuffer _IndicesBuffer;
    private CommandBuffer _command;

    // struct
    struct MeshObject
    {
        public Matrix4x4 localToWorldMatrix;
        public int indices_offset;
        public int indices_count;
        public Vector4 albedo;
        public Vector4 specular;
        public float smoothness;
        public Vector4 emission;
    }
    struct BvhNode
    {
        public Vector3 min;
        public Vector3 max;
        public int leftChild;
        public int rightChild;
        public int meshObjectIndex;
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
        // ? is a null-conditional operator
        _MeshObjectBuffer?.Release();
        _VerticesBuffer?.Release();
        _IndicesBuffer?.Release();
        _bvhNodes?.Clear();
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

        // set mesh data to compute shader
        SetComputeBuffer("_MeshObjectBuffer", _MeshObjectBuffer);
        SetComputeBuffer("_VerticesBuffer", _VerticesBuffer);
        SetComputeBuffer("_IndicesBuffer", _IndicesBuffer);
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

    //// BVH 10/4
    //private void BuildBVH()
    //{
    //    // init BVH List
    //    _bvhNodes?.Clear();
    //    List<BVHPrimitive> primitives = new List<BVHPrimitive>();

    //    // Create leaf nodes for each mesh object
    //    for (int i = 0; i < _meshObjects.Count; i++)
    //    {
    //        Bounds bounds = CalculateMeshBounds(_meshObjects[i]);
    //        primitives.Add(new BVHPrimitive { bounds = bounds, meshObjectIndex = i });
    //    }

    //    BuildBVHRecursive(primitives, 0, primitives.Count, 0);

    //    CreateComputeBuffer(ref _BvhBuffer, _bvhNodes, 36);
    //}
    //private int BuildBVHRecursive(List<BVHPrimitive> primitives, int start, int end, int depth)
    //{
    //    int nodeIndex = _bvhNodes.Count;
    //    _bvhNodes.Add(new BvhNode());

    //    if (end - start <= 1)
    //    {
    //        // Leaf node
    //        _bvhNodes[nodeIndex] = new BvhNode
    //        {
    //            min = primitives[start].bounds.min,
    //            max = primitives[start].bounds.max,
    //            leftChild = -1,
    //            rightChild = -1,
    //            meshObjectIndex = primitives[start].meshObjectIndex
    //        };
    //    } else
    //    {
    //        // internal node
    //        Bounds nodeBounds = new Bounds(primitives[start].bounds.center, Vector3.zero);
    //        for (int i = start + 1; i < end; i++)
    //        {
    //            nodeBounds.Encapsulate(primitives[i].bounds);
    //        }

    //        int axis = depth % 3;
    //        int mid = (start + end) / 2;

    //        primitives.Sort(start, end - start, new BVHComparer(axis));

    //        int leftChild = BuildBVHRecursive(primitives, start, mid, depth + 1);
    //        int rightChild = BuildBVHRecursive(primitives, mid, end, depth + 1);

    //        _bvhNodes[nodeIndex] = new BvhNode
    //        {
    //            min = nodeBounds.min,
    //            max = nodeBounds.max,
    //            leftChild = leftChild,
    //            rightChild = rightChild,
    //            meshObjectIndex = -1
    //        };
    //    }

    //    return nodeIndex;
    //}
    //private Bounds CalculateMeshBounds(MeshObject meshObject)
    //{
    //    Bounds bounds = new Bounds();

    //    for (int i = 0; i < meshObject.indices_count; i++)
    //    {
    //        int index = _indices[meshObject.indices_offset + i];
    //        Vector3 vertex = meshObject.localToWorldMatrix.MultiplyPoint(_vertices[_indices[meshObject.indices_offset + i]]);
    //        if (i == 0)
    //        {
    //            bounds = new Bounds(vertex, Vector3.zero);
    //        }
    //        else
    //        {
    //            bounds.Encapsulate(vertex);
    //        }
    //    }

    //    return bounds;
    //}

    // Rebuild the mesh object buffers
    private void RebuildMeshObjectBuffers()
    {
        if (!_meshObjectsNeedRebuilding)
        {
            return;
        }

        _meshObjectsNeedRebuilding = false;

        // Clear any existing buffers
        //_textures.Clear();
        _meshObjects.Clear();
        _vertices.Clear();
        _indices.Clear();

        //for debug
        //int totalTriangleCount = 0;

        foreach (RayTracingObject obj in _rayTracingObjects)
        {
            Mesh mesh = obj.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer meshRenderer = obj.GetComponent<MeshRenderer>();

            // Get the object's material
            Material[] materials = meshRenderer.sharedMaterials;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                // Add vertex data
                int firstVertex = _vertices.Count;
                _vertices.AddRange(mesh.vertices);

                // get and add submesh index data
                int[] submeshIndices = mesh.GetIndices(submesh);
                int firstIndex = _indices.Count;
                _indices.AddRange(submeshIndices.Select(index => index + firstVertex));

                // for debug
                // totalTriangleCount += submeshIndices.Length / 3;

                // get the material properties
                Material material = materials[Mathf.Min(submesh, materials.Length - 1)];

                // init setting Texture
                //int albedoTexID = -1;
                //int normalTexID = -1;
                //bool hasAlbedoTex = false;
                //bool hasNormalTex = false;

                //// check if the material has a texture assigned
                //if (material.HasProperty("_MainTex"))
                //{
                //    Texture2D albedoTexture = material.GetTexture("_MainTex") as Texture2D;
                //    if (albedoTexture != null)
                //    {
                //        albedoTexID = _textures.Count;
                //        _textures.Add(albedoTexture);
                //        hasAlbedoTex = true;
                //    }
                //}

                //if (material.HasProperty("_BumpMap"))
                //{
                //    Texture2D normalTexture = material.GetTexture("_BumpMap") as Texture2D;
                //    if (normalTexture != null)
                //    {
                //        normalTexID = _textures.Count;
                //        _textures.Add(normalTexture);
                //        hasNormalTex = true;
                //    }
                //}

                Vector3 albedo = material.GetVector("_Color");
                Vector3 specular = material.GetVector("_SpecColor");
                float smoothness = material.GetFloat("_Glossiness");
                Vector3 emission = material.GetVector("_EmissionColor");

                // Add the object to the list
                _meshObjects.Add(new MeshObject()
                {
                    localToWorldMatrix = obj.transform.localToWorldMatrix,
                    indices_offset = firstIndex,
                    indices_count = submeshIndices.Length,
                    albedo = albedo,
                    specular = specular,
                    smoothness = smoothness,
                    emission = emission,
                });
            }
        }

        if (_meshObjects.Count == 0)
        {
            Debug.LogWarning("No objects to trace.");
            return;
        }

        // for debug
        //Debug.Log("Triangle count: " + totalTriangleCount);

        CreateComputeBuffer(ref _MeshObjectBuffer, _meshObjects, 124);
        CreateComputeBuffer(ref _VerticesBuffer, _vertices, 12);
        CreateComputeBuffer(ref _IndicesBuffer, _indices, 4);

        // BVH 10/4
        //BuildBVH();
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

    // Helper classes for BVH construction 10/4
    private struct BVHPrimitive
    {
        public Bounds bounds;
        public int meshObjectIndex;
    }

    private class BVHComparer : IComparer<BVHPrimitive>
    {
        private int _axis;

        public BVHComparer(int axis)
        {
            _axis = axis;
        }

        public int Compare(BVHPrimitive x, BVHPrimitive y)
        {
            return x.bounds.center[_axis].CompareTo(y.bounds.center[_axis]);
        }
    }
}