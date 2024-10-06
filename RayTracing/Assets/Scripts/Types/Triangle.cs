using UnityEngine;

public readonly struct Triangle
{
    public readonly Vector3 _PosA;
    public readonly Vector3 _PosB;
    public readonly Vector3 _PosC;

    public readonly Vector3 _NormalA;
    public readonly Vector3 _NormalB;
    public readonly Vector3 _NormalC;

    public Triangle(Vector3 posA, Vector3 posB, Vector3 posC, Vector3 normalA, Vector3 normalB, Vector3 normalC)
    {
        this._PosA = posA;
        this._PosB = posB;
        this._PosC = posC;

        this._NormalA = normalA;
        this._NormalB = normalB;
        this._NormalC = normalC;
    }
}