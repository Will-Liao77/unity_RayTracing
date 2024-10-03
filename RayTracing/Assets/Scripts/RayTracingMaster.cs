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

    // private
    private RenderTexture _target;
    private RenderTexture _converged;
    private Camera _camera;
    // light for ray tracing
    private Light _pointLight;
    // texture for ray tracing
    //private List<Texture2D> _textures = new List<Texture2D>();
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
    private void Start()
    {
        if (RayTracingShader == null)
        {
            Debug.LogError("no computer shader load");
        }

        _command = new CommandBuffer();
        _command.name = "Ray Tracing";
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

    //private void Render(RenderTexture destination)
    //{
    //    // Make sure we have a current render target
    //    InitRenderTexture();

    //    // Set the target and dispatch the compute shader
    //    RayTracingShader.SetTexture(0, "Result", _target);
    //    // 8 is the number of threads per group
    //    RayTracingShader.Dispatch(0, Mathf.CeilToInt(_target.width / 8.0f), Mathf.CeilToInt(_target.height / 8.0f), 1);

    //    // Blit the result texture to the screen
    //    Graphics.Blit(_target, destination);
    //}

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        RebuildMeshObjectBuffers();
        SetShaderParameters();
        InitRenderTexture();
        //Render(destination);

        // command buffer setting
        _command.Clear();
        _command.SetComputeTextureParam(RayTracingShader, 0, "Result", _target);
        _command.DispatchCompute(RayTracingShader, 0, Mathf.CeilToInt(_target.width / 8.0f), Mathf.CeilToInt(_target.height / 8.0f), 1);
        _command.Blit(_target, destination);
        _command.Blit(destination, _converged);
        Graphics.ExecuteCommandBuffer(_command);
    }
}