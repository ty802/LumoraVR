using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

namespace Aquamarine.Source.Assets;

public class MeshFile
{
    public struct Vertex
    {
        public Vector3 Position = Vector3.Zero;
        public Vector3 Normal = Vector3.Up;
        public Color Color = Colors.White;
        public Vector2 UV = Vector2.Zero;
        public Vector2 UV2 = Vector2.Zero;
        public Vector4I Bones = -Vector4I.One;
        public Vector4 Weights = Vector4.Zero;
        
        public Vertex()
        {
        }
    }
    public struct BlendshapeVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
    }
    public class MeshPart
    {
        public Vertex[] Vertices = [];
        public BlendshapeVertex[][] BlendshapeVertices = [];
        public Vector3I[] Indices = [];
        public LODGroup[] LODs = [];
    }
    public class LODGroup
    {
        public float Distance;
        public Vector3I[] Indices;
    }
    
    public bool UseVertexColors;
    public bool UseUV2;
    public bool Skinned;
    public int BoneCount = -1;

    public string[] Blendshapes = [];
    public MeshPart[] MeshParts = [];

    public bool Valid()
    {
        if (MeshParts.Length == 0) return false;
        foreach (var part in MeshParts)
        {
            if (part.BlendshapeVertices.Length < Blendshapes.Length) return false;
            if (part.BlendshapeVertices.Any(i => i.Length < part.Vertices.Length)) return false;
            if (part.Indices
                .Concat(part.LODs.SelectMany(i => i.Indices))
                .SelectMany(i => new[] { i.X, i.Y, i.Z })
                .Any(i => i < 0 || i >= part.Vertices.Length)) 
                return false;
            if (part.Vertices
                .Select(i => i.Bones)
                .SelectMany(i => new[]{i.X, i.Y, i.Z, i.W})
                .Any(i => i != -1 && i >= BoneCount)) 
                return false;
        }
        return true;
    }
    public static MeshFile FromArrayMesh(ArrayMesh arrayMesh)
    {
        var mesh = new MeshFile();
        
        mesh.Blendshapes = Enumerable.Range(0, arrayMesh.GetBlendShapeCount()).Select(i => arrayMesh.GetBlendShapeName(i).ToString()).ToArray();

        var partCount = arrayMesh.GetSurfaceCount();
        mesh.MeshParts = new MeshPart[partCount];
        for (var i = 0; i < partCount; i++)
        {
            
        }

        return mesh;
    }
    public ArrayMesh Instantiate()
    {
        var mesh = new ArrayMesh();

        foreach (var blend in Blendshapes) mesh.AddBlendShape(blend);

        foreach (var part in MeshParts)
        {
            var meshArray = new Array();
            meshArray.Resize((int)Mesh.ArrayType.Max);

            meshArray[(int)Mesh.ArrayType.Vertex] = part.Vertices.Select(i => i.Position).ToArray();
            meshArray[(int)Mesh.ArrayType.Normal] = part.Vertices.Select(i => i.Normal).ToArray();
            if (UseVertexColors) meshArray[(int)Mesh.ArrayType.Color] = part.Vertices.Select(i => i.Color).ToArray();
            meshArray[(int)Mesh.ArrayType.TexUV] = part.Vertices.Select(i => i.UV).ToArray();
            if (UseUV2) meshArray[(int)Mesh.ArrayType.TexUV2] = part.Vertices.Select(i => i.UV2).ToArray();
            if (Skinned) meshArray[(int)Mesh.ArrayType.Bones] = part.Vertices.Select(i => i.Bones).SelectMany(i => new []{ i.X, i.Y, i.Z, i.W }).ToArray();
            if (Skinned) meshArray[(int)Mesh.ArrayType.Weights] = part.Vertices.Select(i => i.Weights).SelectMany(i => new []{ i.X, i.Y, i.Z, i.W }).ToArray();
            meshArray[(int)Mesh.ArrayType.Index] = part.Indices.SelectMany(i => new[]{ i.X, i.Y, i.Z }).ToArray();

            var lodDict = new Dictionary();
            foreach (var lodGroup in part.LODs) 
                lodDict[lodGroup.Distance] = lodGroup.Indices.SelectMany(i => new[]{ i.X, i.Y, i.Z }).ToArray();

            var flags = Mesh.ArrayFormat.FormatVertex | Mesh.ArrayFormat.FormatNormal | Mesh.ArrayFormat.FormatTexUV;

            if (UseVertexColors) flags |= Mesh.ArrayFormat.FormatColor;
            if (UseUV2) flags |= Mesh.ArrayFormat.FormatTexUV2;
            if (Skinned) flags |= Mesh.ArrayFormat.FormatBones | Mesh.ArrayFormat.FormatWeights;

            var blendshapeArray = new Array<Array>();

            foreach (var blendshape in part.BlendshapeVertices)
            {
                var blend = new Array();
                
                blend.Resize((int)Mesh.ArrayType.Max);
                
                blend[(int)Mesh.ArrayType.Vertex] = blendshape.Select(i => i.Position).ToArray();
                blend[(int)Mesh.ArrayType.Normal] = blendshape.Select(i => i.Normal).ToArray();
                
                blendshapeArray.Add(blend);
            }
            
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, meshArray, blendshapeArray, lodDict, flags);
        }
        
        mesh.RegenNormalMaps(); //thanks
        
        return mesh;
    }
}

