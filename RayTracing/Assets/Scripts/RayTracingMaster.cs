using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static Unity.VisualScripting.Member;

public class RayTracingMaster : MonoBehaviour
{
    #region public variables
    public ComputeShader RayTracingShader;
    #endregion

    #region private variables
    // private
    // shader ID
    int RayTracingKernalID;
    // Render texture
    private RenderTexture _computeShaderResult;
    private RenderTexture _displayTexture;
    private Material _displayMaterial;

    // render
    private GraphicsFence _graphicsFence;

    // camera
    private Camera _camera;

    // check render complete
    private bool _isRendering = false;
    private bool _isAccumulating = false;

    // point light
    private Light _pointLight;

    // texture for ray tracing
    private List<Texture2D> _textures = new List<Texture2D>();
    private Texture2DArray _Texture2DArray;

    // BVH variables
    private BVH.Node[] _bvhNodes;
    private ComputeBuffer _BvhNodesBuffer;
    private ComputeBuffer _BvhTriangleBuffer;

    // mesh variables
    private static bool _meshObjectsNeedRebuilding = false;
    private static List<RayTracingObject> _rayTracingObjects = new List<RayTracingObject>();
    private static List<MeshObject> _meshObjects = new List<MeshObject>();
    private static List<Vector3> _vertices = new List<Vector3>();
    private static List<int> _indices = new List<int>();
    private static List<Vector2> _texCoords = new List<Vector2>();
    private ComputeBuffer _MeshObjectBuffer;
    private ComputeBuffer _VerticesBuffer;
    private ComputeBuffer _IndicesBuffer;
    private ComputeBuffer _TexCoordsBuffer;
    private CommandBuffer _command;

    // mesh data
    private List<Vector3> allVertices = new List<Vector3>();
    private List<int> allTriangles = new List<int>();
    private List<Vector3> allNormals = new List<Vector3>();

    #endregion

    #region struct
    struct MeshObject
    {
        public Matrix4x4 localToWorldMatrix;
        public int indices_offset;
        public int indices_count;
        public Vector4 albedo;
        public Vector4 specular;
        public float smoothness;
        public Vector4 emission;
        public int textureIndex;
        public Vector4 textureST;
    }
    struct BvhNode
    {
        public Vector3 min;
        public Vector3 max;
        public int leftChild;
        public int rightChild;
        public int meshObjectIndex;
    }
    #endregion

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

        Application.targetFrameRate = 60;

        // RayTracingShaderKernalID
        RayTracingKernalID = RayTracingShader.FindKernel("CSMain");
    }

    private void OnEnable()
    {
        SetUpScene();
    }
    private void OnDisable()
    {
        // Release mesh data buffers
        _MeshObjectBuffer?.Release();
        _VerticesBuffer?.Release();
        _IndicesBuffer?.Release();
        _TexCoordsBuffer?.Release();

        // Release texture data
        _computeShaderResult?.Release();

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
        RayTracingShader.SetInt("_BvhNodeCount", _bvhNodes.Length);
        SetComputeBuffer("_BvhTriangleBuffer", _BvhTriangleBuffer);
        RayTracingShader.SetInt("_BvhTriangleCount", _bvhNodes.Length);

        // set mesh data to compute shader
        SetComputeBuffer("_MeshObjectBuffer", _MeshObjectBuffer);
        SetComputeBuffer("_VerticesBuffer", _VerticesBuffer);
        SetComputeBuffer("_IndicesBuffer", _IndicesBuffer);
        SetComputeBuffer("_TexCoordsBuffer", _TexCoordsBuffer);

        // set texture data to compute shader
        RayTracingShader.SetTexture(0, "_Texture2DArray", _Texture2DArray);
        RayTracingShader.SetInt("_TextureCount", _textures.Count);
        RayTracingShader.SetInt("_MaxTextureWidth", _Texture2DArray.width);
        RayTracingShader.SetInt("_MaxTextureHeight", _Texture2DArray.height);

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
        _meshObjects.Clear();
        _vertices.Clear();
        _indices.Clear();
        _textures.Clear();

        //for debug
        //int totalTriangleCount = 0;

        foreach (RayTracingObject obj in _rayTracingObjects)
        {
            Mesh mesh = obj.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer meshRenderer = obj.GetComponent<MeshRenderer>();
            Matrix4x4 localToWorldMatrix = obj.transform.localToWorldMatrix;
            // mesh data(Vertices, Normals)
            Vector3[] worldVertices = mesh.vertices.Select(v => localToWorldMatrix.MultiplyPoint3x4(v)).ToArray();
            Vector3[] worldNormals = mesh.normals.Select(n => localToWorldMatrix.MultiplyVector(n).normalized).ToArray();
            allVertices.AddRange(worldVertices);
            allNormals.AddRange(worldNormals);

            // Get the object's material
            Material[] materials = meshRenderer.sharedMaterials;

            // Add texCoords data
            if (mesh.uv.Length > 0)
            {
                _texCoords.AddRange(mesh.uv);
            }
            else
            {
                _texCoords.AddRange(Enumerable.Repeat(new Vector2(0, 0), mesh.vertices.Length));
            }

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                // Add vertex data
                int firstVertex = _vertices.Count;

                _vertices.AddRange(mesh.vertices);

                // get and add submesh index data
                int[] submeshIndices = mesh.GetIndices(submesh);

                allTriangles.AddRange(submeshIndices);

                int firstIndex = _indices.Count;
                _indices.AddRange(submeshIndices.Select(index => index + firstVertex));

                // for debug
                // totalTriangleCount += submeshIndices.Length / 3;

                // get the material properties
                Material material = materials[Mathf.Min(submesh, materials.Length - 1)];

                // init setting Texture
                int albedoTexID = -1;
                Vector4 textureST = new Vector4(1, 1, 0, 0);

                // check if the material has a texture assigned
                if (material.HasProperty("_MainTex"))
                {
                    Texture2D albedoTexture = material.GetTexture("_MainTex") as Texture2D;
                    if (albedoTexture != null)
                    {
                        albedoTexID = _textures.IndexOf(albedoTexture);
                        if (albedoTexID == -1)
                        {
                            albedoTexID = _textures.Count;
                            _textures.Add(albedoTexture);
                        }
                        //hasAlbedoTex = true;
                        textureST = new Vector4(material.GetTextureScale("_MainTex").x, material.GetTextureScale("_MainTex").y, material.GetTextureOffset("_MainTex").x, material.GetTextureOffset("_MainTex").y);
                    }
                }

                //Debug.Log("Texture ID: " + albedoTexID);

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
                    textureIndex = albedoTexID,
                    textureST = textureST
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

        CreateComputeBuffer(ref _MeshObjectBuffer, _meshObjects, 144);
        CreateComputeBuffer(ref _VerticesBuffer, _vertices, 12);
        CreateComputeBuffer(ref _IndicesBuffer, _indices, 4);
        CreateComputeBuffer(ref _TexCoordsBuffer, _texCoords, 8);

        // BVH
        BVH bvh = new BVH(allVertices.ToArray(), allTriangles.ToArray(), allNormals.ToArray());

        _bvhNodes = bvh.GetNodes();
        Triangle[] allTris = bvh.GetTriangles();

        CreateComputeBuffer(ref _BvhNodesBuffer, _bvhNodes.ToList(), 32);
        CreateComputeBuffer(ref _BvhTriangleBuffer, allTris.ToList(), 72);

        // Texture
        if (_textures.Count > 0)
        {
            // Find the maximum dimensions among all textures
            int maxWidth = _textures.Max(tex => tex.width);
            int maxHeight = _textures.Max(tex => tex.height);

            // Use a widely supported format
            TextureFormat targetFormat = TextureFormat.RGBA32;

            _Texture2DArray = new Texture2DArray(maxWidth, maxHeight, _textures.Count, targetFormat, true);
            _Texture2DArray.wrapMode = TextureWrapMode.Repeat;

            for (int i = 0; i < _textures.Count; i++)
            {
                Texture2D sourceTex = _textures[i];

                // Ensure the texture is readable
                if (!sourceTex.isReadable)
                {
                    Debug.LogWarning($"Texture {sourceTex.name} is not readable. Please enable 'Read/Write Enabled' in its import settings.");
                    continue; // Skip this texture
                }

                // Create a new texture with the target format and size
                Texture2D compatibleTex = new Texture2D(maxWidth, maxHeight, targetFormat, false);

                // Resize and convert format if necessary
                if (sourceTex.width != maxWidth || sourceTex.height != maxHeight || sourceTex.format != targetFormat)
                {
                    // Read pixels from source texture
                    Color[] sourcePixels = sourceTex.GetPixels();

                    // Resize the array if necessary
                    if (sourceTex.width != maxWidth || sourceTex.height != maxHeight)
                    {
                        sourcePixels = ResizePixelArray(sourcePixels, sourceTex.width, sourceTex.height, maxWidth, maxHeight);
                    }

                    // Set pixels to the new texture
                    compatibleTex.SetPixels(sourcePixels);
                    compatibleTex.Apply();
                }
                else
                {
                    // If no resizing or format conversion is needed, just copy the pixels
                    Graphics.CopyTexture(sourceTex, compatibleTex);
                }

                // Copy to the texture array
                Graphics.CopyTexture(compatibleTex, 0, 0, _Texture2DArray, i, 0);

                // Clean up the temporary texture
                Destroy(compatibleTex);
            }

            _Texture2DArray.Apply(false, true);
        }
        else
        {
            _Texture2DArray = new Texture2DArray(512, 512, 3, TextureFormat.RGBA32, false);
            _Texture2DArray.wrapMode = TextureWrapMode.Repeat;
            _Texture2DArray.SetPixels(new Color[] { Color.white }, 0);
            _Texture2DArray.Apply(false, true);
        }
    }
    private Color[] ResizePixelArray(Color[] sourcePixels, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        Color[] resizedPixels = new Color[targetWidth * targetHeight];

        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                float u = x / (float)targetWidth;
                float v = y / (float)targetHeight;

                int sourceX = Mathf.FloorToInt(u * sourceWidth);
                int sourceY = Mathf.FloorToInt(v * sourceHeight);

                resizedPixels[y * targetWidth + x] = sourcePixels[sourceY * sourceWidth + sourceX];
            }
        }

        return resizedPixels;
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
        if (_computeShaderResult == null || _computeShaderResult.width != Screen.width || _computeShaderResult.height != Screen.height)
        {
            // Release render texture if we already have one
            if (_computeShaderResult != null)
            {
                _computeShaderResult.Release();
            }

            // Get a render target for Ray Tracing
            _computeShaderResult = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _computeShaderResult.enableRandomWrite = true;
            _computeShaderResult.Create();
        }

        _displayTexture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.sRGB);
        _displayTexture.enableRandomWrite = true;
        _displayTexture.Create();
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

    private void CreateOrUpdateRenderTexture(ref RenderTexture rt)
    {
        if (rt == null || rt.width != Screen.width || rt.height != Screen.height)
        {
            if (rt != null)
            {
                rt.Release();
            }
            rt = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            rt.enableRandomWrite = true;
            rt.Create();
        }
    }

    private void PrepareToRender()
    {
        RebuildMeshObjectBuffers();
        SetShaderParameters();
        InitRenderTexture();
    }
    private void ValidateComputeShaderOutput()
    {
        RenderTexture.active = _computeShaderResult;
        Texture2D tex = new Texture2D(_computeShaderResult.width, _computeShaderResult.height, TextureFormat.RGBAFloat, false);
        tex.ReadPixels(new Rect(0, 0, _computeShaderResult.width, _computeShaderResult.height), 0, 0);
        tex.Apply();

        byte[] bytes = tex.EncodeToPNG();
        string path = Application.dataPath + "/ComputeShaderOutput.png";
        System.IO.File.WriteAllBytes(path, bytes);
        Debug.Log("Compute shader output saved to: " + path);

        Color centerColor = tex.GetPixel(tex.width / 2, tex.height / 2);
        Debug.Log($"Center pixel color: {centerColor}");

        Destroy(tex);
    }

    public void Render()
    {
        PrepareToRender();

        RayTracingShader.SetTexture(RayTracingKernalID, "Result", _computeShaderResult);
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);

        _command = new CommandBuffer();
        _command.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);

        // graphicsFence
        _graphicsFence = _command.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.ComputeProcessing);

        RayTracingShader.Dispatch(RayTracingKernalID, threadGroupsX, threadGroupsY, 1);

        // save to png file check result
        //ValidateComputeShaderOutput();

        Graphics.ExecuteCommandBufferAsync(_command, ComputeQueueType.Default);

        _isRendering = true;
    }

    //private void CheckGraphicsFenceSupport()
    //{
    //    if (SystemInfo.supportsGraphicsFence)
    //    {
    //        Debug.Log("Graphics fence is supported.");
    //    }
    //    else
    //    {
    //        Debug.Log("Graphics fence is not supported.");
    //    }
    //}

    private bool _isRendered = false;

    private void Update()
    {
        if (_isRendering)
        {
            if (_graphicsFence.passed)
            {
                Debug.Log("Rendering");
                _isRendering = false;
                _isAccumulating = true;

                // if need to accumulate image use this
                //_command = new CommandBuffer();
                //_command.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
                //_graphicsFence = _command.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.ComputeProcessing);

                // use in accumulation shader
                //RenderTexture temp = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                //Graphics.Blit(_computeShaderResult, temp);

                _isRendered = true;

                // for accumulation
                //Graphics.ExecuteCommandBufferAsync(_command, ComputeQueueType.Default);
                //RenderTexture.ReleaseTemporary(temp);
            }
        }
        //else if (_isAccumulating)
        //{
        //    if (_graphicsFence.passed)
        //    {
        //        Debug.Log("Accumulating");
        //    }
        //}
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (_isRendered)
        {
            Graphics.Blit(_computeShaderResult, destination);
        }
        else
        {
            Graphics.Blit(source, destination);
        }
    }
}