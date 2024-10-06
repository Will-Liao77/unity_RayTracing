using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using Unity.Mathematics;
using UnityEngine;
using System.Text;

// reference from https://github.com/SebLague/Ray-Tracing/blob/main/Assets/Scripts/BVH.cs#L213
public class BVH : MonoBehaviour
{
    public readonly NodeList _allNodes;
    public readonly Triangle[] _allTris;

    public BuildStatus status;

    public Triangle[] GetTriangles() => _allTris;

    readonly BVHTriangle[] _allTriangles;

    public BVH(Vector3[] verts, int[] indices, Vector3[] normals)
    {
        // start BVH construction
        var sw = System.Diagnostics.Stopwatch.StartNew();
        status = new();

        // construct BVH
        _allNodes = new();
        _allTriangles = new BVHTriangle[indices.Length / 3];
        BoundingBox bounds = new BoundingBox();

        for (int i = 0; i < indices.Length; i += 3)
        {
            float3 a = verts[indices[i]];
            float3 b = verts[indices[i + 1]];
            float3 c = verts[indices[i + 2]];
            float3 centre = (a + b + c) / 3;

            float3 max = math.max(math.max(a, b), c);
            float3 min = math.min(math.min(a, b), c);
            _allTriangles[i / 3] = new BVHTriangle(centre, min, max, i / 3);
            bounds.GrowToInclude(min, max);
        }

        _allNodes.Add(new Node(bounds));
        Split(0, verts, 0, _allTriangles.Length);

        _allTris = new Triangle[indices.Length];

        // debug
        //Debug.Log(_allTriangles.Length); // sibenik 75284 triangles

        for (int i = 0; i < _allTriangles.Length; i++)
        {
            //Debug.Log("indices: " + indices[i] + "i: " + i);
            BVHTriangle buildTri = _allTriangles[i];
            Vector3 a = verts[indices[buildTri._index]];
            Vector3 b = verts[indices[buildTri._index + 1]];
            Vector3 c = verts[indices[buildTri._index + 2]];
            Vector3 normalA = normals[indices[buildTri._index]];
            Vector3 normalB = normals[indices[buildTri._index + 1]];
            Vector3 normalC = normals[indices[buildTri._index + 2]];
            _allTris[i] = new Triangle(a, b, c, normalA, normalB, normalC);
        }

        sw.Stop();
        status._TimeMs = (int)sw.ElapsedMilliseconds;
    }

    void Split(int parentIndex, Vector3[] verts, int triGlobalStart, int triNum, int depth = 0)
    {
        const int MaxDepth = 32;
        Node parent = _allNodes._nodes[parentIndex];
        Vector3 size = parent.CalcuateBoundSize();
        float parentCost = NodeCost(size, triNum);

        (int splitAxis, float splitPos, float cost) = ChooseSplit(parent, triGlobalStart, triNum);

        if (cost < parentCost && depth < MaxDepth)
        {
            BoundingBox boundsLeft = new();
            BoundingBox boundsRight = new();
            int numOnLeft = 0;

            for (int i = triGlobalStart; i < triGlobalStart + triNum; i++)
            {
                BVHTriangle tri = _allTriangles[i];
                if (tri._centre[splitAxis] < splitPos)
                {
                    boundsLeft.GrowToInclude(tri._min, tri._max);
                    BVHTriangle swap = _allTriangles[triGlobalStart + numOnLeft];
                    _allTriangles[triGlobalStart + numOnLeft] = tri;
                    _allTriangles[i] = swap;
                    numOnLeft++;
                }
                else
                {
                    boundsRight.GrowToInclude(tri._min, tri._max);
                }
            }

            int numOnRight = triNum - numOnLeft;
            int triStartLeft = triGlobalStart + 0;
            int triStartRight = triGlobalStart + numOnLeft;

            // split parent into two children
            int childLeftIndex = _allNodes.Add(new Node(boundsLeft, triStartLeft, numOnLeft));
            int childRightIndex = _allNodes.Add(new Node(boundsRight, triStartRight, numOnRight));

            // update parent
            parent._startIndex = childLeftIndex;
            _allNodes._nodes[parentIndex] = parent;
            status.RecordNode(depth, false);

            // recursive split children
            Split(childLeftIndex, verts, triGlobalStart, numOnLeft, depth + 1);
            Split(childRightIndex, verts, triGlobalStart + numOnLeft, numOnRight, depth + 1);
        }
        else
        {
            // parent is a leaf, assign all triangle to it
            parent._startIndex = triGlobalStart;
            parent._triangleCount = triNum;
            _allNodes._nodes[parentIndex] = parent;
            status.RecordNode(depth, true, triNum);
        }
    }

    (int axis, float pos, float cost) ChooseSplit(Node node, int start, int count)
    {
        if (count <= 1) return (0, 0, float.PositiveInfinity);

        float bestSplitPos = 0;
        int bestSplitAxis = 0;
        const int numSplitsTests = 5;

        float bestCost = float.MaxValue;

        // estimate best split position
        for (int axis = 0; axis < 3; axis++)
        {
            for (int i = 0; i < numSplitsTests; i++)
            {
                float splitT = (i + 1) / (numSplitsTests + 1f);
                float splitPos = Mathf.Lerp(node._boundsMin[axis], node._boundsMax[axis], splitT);
                float cost = EvaluateSplit(axis, splitPos, start, count);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestSplitPos = splitPos;
                    bestSplitAxis = axis;
                }
            }
        }

        return (bestSplitAxis, bestSplitPos, bestCost);
    }

    float EvaluateSplit(int splitAxis, float splitPos, int start, int count)
    {
        BoundingBox boundsLeft = new();
        BoundingBox boundsRight = new();
        int numLeft = 0;
        int numRight = 0;

        for (int i = 0; i < start + count; i++)
        {
            BVHTriangle _tri = _allTriangles[i];
            if (_tri._centre[splitAxis] < splitPos)
            {
                boundsLeft.GrowToInclude(_tri._min, _tri._max);
                numLeft++;
            }
            else
            {
                boundsRight.GrowToInclude(_tri._min, _tri._max);
                numRight++;
            }
        }

        float costA = NodeCost(boundsLeft._size, numLeft);
        float costB = NodeCost(boundsRight._size, numRight);
        return costA + costB;
    }

    static float NodeCost(Vector3 size, int numTriangles)
    {
        float halfArea = size.x * size.y + size.x * size.z + size.y * size.z;
        return halfArea * numTriangles;
    }
    

    public struct Node
    {
        public float3 _boundsMin;
        public float3 _boundsMax;
        public int _startIndex;
        public int _triangleCount;

        public Node(BoundingBox bounds) : this()
        {
            _boundsMin = bounds._min;
            _boundsMax = bounds._max;
            _startIndex = -1;
            _triangleCount = -1;
        }

        public Node(BoundingBox bounds, int startIndex, int triCount)
        {
            _boundsMin = bounds._min;
            _boundsMax = bounds._max;
            _startIndex = startIndex;
            _triangleCount = triCount;
        }

        public Vector3 CalcuateBoundSize() => _boundsMax - _boundsMin;
        public Vector3 CalculateBoundCentre() => (_boundsMin + _boundsMax) / 2;
    }
    public struct BoundingBox
    {
        public float3 _min;
        public float3 _max;
        public float3 _centre => (_min + _max) / 2;
        public float3 _size => _max - _min;
        bool _hasPoint;

        public void GrowToInclude(float3 min, float3 max)
        {
            if (!_hasPoint)
            {
                _hasPoint = true;
                _min = min;
                _max = max;
            }
            else
            {
                _min.x = min.x < _min.x ? min.x : _min.x;
                _min.y = min.y < _min.y ? min.y : _min.y;
                _min.z = min.z < _min.z ? min.z : _min.z;
                _max.x = max.x > _max.x ? max.x : _max.x;
                _max.y = max.y > _max.y ? max.y : _max.y;
                _max.z = max.z > _max.z ? max.z : _max.z;
            }
        }
    }

    public readonly struct BVHTriangle
    {
        public readonly float3 _centre;
        public readonly float3 _min;
        public readonly float3 _max;
        public readonly int _index;

        public BVHTriangle(float3 centre, float3 min, float3 max, int index)
        {
            _centre = centre;
            _min = min;
            _max = max;
            _index = index;
        }
    }

    public Node[] GetNodes() => _allNodes._nodes.AsSpan(0, _allNodes.NodeCount).ToArray();

    public class NodeList
    {
        public Node[] _nodes = new Node[256];
        int _index;

        public int Add(Node node)
        {
            if (_index >= _nodes.Length)
            {
                Array.Resize(ref _nodes, _nodes.Length * 2);
            }

            int nodeIndex = _index;
            _nodes[_index++] = node;
            return nodeIndex;
        }

        public int NodeCount => _index;
    }

    [System.Serializable]
    public class BuildStatus
    {
        public int _TimeMs;
        public int _TriangleCount;
        public int _TotalNodeCount;
        public int _LeafNodeCount;

        public int _LeafDepthMax;
        public int _LeafDepthMin = int.MaxValue;
        public int _LeafDepthSum;

        public int _LeafMaxTriCount;
        public int _LeafMinTriCount = int.MaxValue;

        public void RecordNode(int depth, bool isLeaf, int triCount = 0)
        {
            _TotalNodeCount++;

            if (isLeaf)
            {
                _LeafNodeCount++;
                _LeafDepthSum += depth;
                _LeafDepthMax = Mathf.Max(_LeafDepthMax, depth);
                _LeafDepthMin = Mathf.Min(_LeafDepthMin, depth);
                _TriangleCount += triCount;

                _LeafMaxTriCount = Mathf.Max(_LeafMaxTriCount, triCount);
                _LeafMinTriCount = Mathf.Min(_LeafMinTriCount, triCount);

            }
        }
        public override string ToString()
        {
            var sb = new StringBuilder();
            
            sb.AppendLine($"Total Time: {_TimeMs}ms");
            sb.AppendLine($"Total Triangles: {_TriangleCount}");
            sb.AppendLine($"Total Nodes: {_TotalNodeCount}");
            sb.AppendLine($"Leaf Nodes: {_LeafNodeCount}");
            sb.AppendLine($"Leaf Depth:");
            sb.AppendLine($"Leaf Depth Max: {_LeafDepthMax}");
            sb.AppendLine($"Leaf Depth Min: {_LeafDepthMin}");
            sb.AppendLine($"Leaf Depth Avg: {_LeafDepthSum / _LeafNodeCount}");
            sb.AppendLine($"Leaf Tris:");
            sb.AppendLine($"Leaf Max Triangles: {_LeafMaxTriCount}");
            sb.AppendLine($"Leaf Min Triangles: {_LeafMinTriCount}");
            sb.AppendLine($"Leaf Avg Triangles: {_TriangleCount / _LeafNodeCount}");

            return sb.ToString();
        }
    }
}