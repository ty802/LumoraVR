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
        public Vector3 Position = Vector3.Zero;
        public Vector3 Normal = Vector3.Up;
        
        public BlendshapeVertex()
        {
        }
    }
    public class MeshPart
    {
        public Vertex[] Vertices = [];
        public BlendshapeVertex[][] BlendshapeVertices = [];
        public Vector3I[] Indices = [];
        public LODGroup[] LODs = [];
        
        public bool UseVertexColors;
        public bool UseUV2;
        public bool Skinned;
    }
    public class LODGroup
    {
        public float Distance;
        public Vector3I[] Indices;
    }

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
            /*
            if (part.Vertices
                .Select(i => i.Bones)
                .SelectMany(i => new[]{i.X, i.Y, i.Z, i.W})
                .Any(i => i != -1 && i >= BoneCount)) 
                return false;
                */
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
            var part = new MeshPart();
            mesh.MeshParts[i] = part;
            
            var format = arrayMesh.SurfaceGetFormat(i);
            var array = arrayMesh.SurfaceGetArrays(i);
            var blendshapeArray = arrayMesh.SurfaceGetBlendShapeArrays(i);

            var positionArray = array[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            
            var verts = positionArray.Length;
            var vertArray = new Vertex[verts];
            
            for (var x = 0; x < verts; x++) vertArray[x].Position = positionArray[x];

            var hasNormal = (format & Mesh.ArrayFormat.FormatNormal) > 0;
            
            if (hasNormal)
            {
                var normalArray = array[(int)Mesh.ArrayType.Normal].AsVector3Array();
                for (var x = 0; x < verts; x++) vertArray[x].Normal = normalArray[x];
            }
            else
                for (var x = 0; x < verts; x++)
                    vertArray[x].Normal = Vector3.Up;

            if ((format & Mesh.ArrayFormat.FormatTexUV) > 0)
            {
                var uvArray = array[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                for (var x = 0; x < verts; x++) vertArray[x].UV = uvArray[x];;
            }
            else
                for (var x = 0; x < verts; x++)
                    vertArray[x].UV = Vector2.Zero;
            
            if ((format & Mesh.ArrayFormat.FormatColor) > 0)
            {
                part.UseVertexColors = true;
                var colors = array[(int)Mesh.ArrayType.Color].AsColorArray();
                for (var x = 0; x < verts; x++) vertArray[x].Color = colors[x];
            }
            if ((format & Mesh.ArrayFormat.FormatTexUV2) > 0)
            {
                part.UseUV2 = true;
                var uv2s = array[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
                for (var x = 0; x < verts; x++) vertArray[x].UV2 = uv2s[x];
            }
            if ((format & (Mesh.ArrayFormat.FormatBones | Mesh.ArrayFormat.FormatWeights)) > 0)
            {
                part.Skinned = true;
                var bones = array[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights = array[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                
                for (var x = 0; x < verts; x++)
                {
                    vertArray[x].Bones = new Vector4I(bones[4*x], bones[(4*x)+1], bones[(4*x)+2], bones[(4*x)+3]);
                    vertArray[x].Weights = new Vector4(weights[4*x], weights[(4*x)+1], weights[(4*x)+2], weights[(4*x)+3]);
                }
            }

            if ((format & Mesh.ArrayFormat.FormatIndex) > 0)
            {
                var indexArray = array[(int)Mesh.ArrayType.Index].AsInt32Array();
                var count = indexArray.Length / 3;
                part.Indices = new Vector3I[count];
                for (var x = 0; x < count; x++) part.Indices[x] = new Vector3I(indexArray[3*x], indexArray[(3*x)+1], indexArray[(3*x)+2]);
            }
            else
            {
                var indexCount = verts / 3;
                for (var x = 0; x < indexCount; x++) part.Indices[x] = new Vector3I(3*x, (3*x)+1, (3*x)+2);
            }

            part.BlendshapeVertices = new BlendshapeVertex[mesh.Blendshapes.Length][];
            
            for (var x = 0; x < blendshapeArray.Count; x++)
            {
                var item = blendshapeArray[x];
                var blendVertArray = new BlendshapeVertex[verts];

                var vertexPositions = item[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                for (var y = 0; y < verts; y++) blendVertArray[y].Position = vertexPositions[y];
                
                if (hasNormal)
                {
                    var normals = item[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    for (var y = 0; y < verts; y++) blendVertArray[y].Normal = normals[y];
                }
                else
                    for (var y = 0; y < verts; y++)
                        blendVertArray[y].Normal = Vector3.Up;

                part.BlendshapeVertices[x] = blendVertArray;
            }
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
            if (part.UseVertexColors) meshArray[(int)Mesh.ArrayType.Color] = part.Vertices.Select(i => i.Color).ToArray();
            meshArray[(int)Mesh.ArrayType.TexUV] = part.Vertices.Select(i => i.UV).ToArray();
            if (part.UseUV2) meshArray[(int)Mesh.ArrayType.TexUV2] = part.Vertices.Select(i => i.UV2).ToArray();
            if (part.Skinned) meshArray[(int)Mesh.ArrayType.Bones] = part.Vertices.Select(i => i.Bones).SelectMany(i => new []{ i.X, i.Y, i.Z, i.W }).ToArray();
            if (part.Skinned) meshArray[(int)Mesh.ArrayType.Weights] = part.Vertices.Select(i => i.Weights).SelectMany(i => new []{ i.X, i.Y, i.Z, i.W }).ToArray();
            meshArray[(int)Mesh.ArrayType.Index] = part.Indices.SelectMany(i => new[]{ i.X, i.Y, i.Z }).ToArray();

            var hasLods = part.LODs is not null && part.LODs.Length > 0;
            var lodDict = new Dictionary();
            
            if (hasLods)
            {
                foreach (var lodGroup in part.LODs) 
                    lodDict[lodGroup.Distance] = lodGroup.Indices.SelectMany(i => new[]{ i.X, i.Y, i.Z }).ToArray();
            }

            var flags = Mesh.ArrayFormat.FormatVertex | Mesh.ArrayFormat.FormatNormal | Mesh.ArrayFormat.FormatTexUV;

            if (part.UseVertexColors) flags |= Mesh.ArrayFormat.FormatColor;
            if (part.UseUV2) flags |= Mesh.ArrayFormat.FormatTexUV2;
            if (part.Skinned) flags |= Mesh.ArrayFormat.FormatBones | Mesh.ArrayFormat.FormatWeights;

            var blendshapeArray = new Array<Array>();

            foreach (var blendshape in part.BlendshapeVertices)
            {
                var blend = new Array();
                
                blend.Resize((int)Mesh.ArrayType.Max);
                
                blend[(int)Mesh.ArrayType.Vertex] = blendshape.Select(i => i.Position).ToArray();
                blend[(int)Mesh.ArrayType.Normal] = blendshape.Select(i => i.Normal).ToArray();
                
                blendshapeArray.Add(blend);
            }
            
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, meshArray, blendshapeArray, hasLods ? lodDict : null, flags);
        }
        
        mesh.RegenNormalMaps(); //thanks
        
        return mesh;
    }
}

