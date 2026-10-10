using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using OpenTK.Mathematics;
namespace ForesTycoon
{
    // Restricted GLB 2.0 loader for the selected, flattened, vertex-colour vehicle asset.
    // No external URIs, scripts, skins, or extension execution.
    internal sealed class GlbTruckModel : ITruckModel
    {
        internal sealed class Part
        {
            internal string Name,Category;
            internal Vector3 Pivot;
            internal Vertex[] Vertices;
            internal VertexBuffer Buffer;
        }
        internal readonly List<Part> Parts=new();
        private readonly List<Part> drawParts = new();
        private int cargoCount;
        private bool disposed;
        internal int DrawGroupCount => drawParts.Count;
        private void BuildDrawGroups()
        {
            var groups = new Dictionary<(string Category, Vector3 Pivot), List<Part>>();
            foreach (var part in Parts)
            {
                if (part.Category == "cargo") { drawParts.Add(part); cargoCount++; continue; }
                var key = (part.Category, part.Category == "wheel" ? part.Pivot : Vector3.Zero);
                if (!groups.TryGetValue(key, out var group)) groups.Add(key, group = new List<Part>());
                group.Add(part);
            }
            foreach (var group in groups.Values)
            {
                int count = 0;
                foreach (var part in group) count += part.Vertices.Length;
                var vertices = new Vertex[count]; int offset = 0;
                foreach (var part in group) { Array.Copy(part.Vertices, 0, vertices, offset, part.Vertices.Length); offset += part.Vertices.Length; }
                var first = group[0];
                drawParts.Add(new Part { Name = first.Name, Category = first.Category, Pivot = first.Pivot, Vertices = vertices });
            }
        }
        public float Radius { get; private set; }
        public float Width { get; private set; }
        public float Wheelbase { get; private set; }
        public float AxleMidpoint { get; private set; }
        internal static GlbTruckModel Load(string path)
        {
            if (new FileInfo(path).Length > AnimatedGlbModel.MaxAssetBytes) throw new NotSupportedException("GLB exceeds the asset budget.");
            byte[] data=File.ReadAllBytes(path);
            try { return LoadCore(data); }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException
                or ArgumentException or IndexOutOfRangeException or OverflowException)
            { throw new InvalidDataException("Malformed vehicle GLB structure or values.", error); }
        }
        private static GlbTruckModel LoadCore(byte[] data)
        {
            if(data.Length<28 || BitConverter.ToUInt32(data,0)!=0x46546c67 || BitConverter.ToUInt32(data,4)!=2
                || BitConverter.ToUInt32(data,8)!=data.Length) throw new InvalidDataException("Invalid GLB header.");
            int jsonLength=checked((int)BitConverter.ToUInt32(data,12));
            if(BitConverter.ToUInt32(data,16)!=0x4e4f534a || jsonLength>data.Length-28)throw new InvalidDataException("Invalid GLB JSON.");
            using var doc=JsonDocument.Parse(data.AsMemory(20,jsonLength));
            int chunk=20+jsonLength, binaryStart=chunk+8;
            int binaryLength=checked((int)BitConverter.ToUInt32(data,chunk));
            if(BitConverter.ToUInt32(data,chunk+4)!=0x004e4942 || binaryLength!=data.Length-binaryStart)
                throw new InvalidDataException("Invalid GLB binary.");
            var root=doc.RootElement;
            AnimatedGlbModel.ValidateDocument(root, binaryLength);
            var accessors=root.GetProperty("accessors");var views=root.GetProperty("bufferViews");
            (int Offset,int Count,int Stride,int Type) Access(int id,int components)
            {
                var a=accessors[id];var v=views[a.GetProperty("bufferView").GetInt32()];
                int type=a.GetProperty("componentType").GetInt32();
                int bytes=type==5123?2:type==5121?1:4;
                int count=a.GetProperty("count").GetInt32();
                int offset=(v.TryGetProperty("byteOffset",out var o)?o.GetInt32():0)+(a.TryGetProperty("byteOffset",out o)?o.GetInt32():0);
                int arity=a.GetProperty("type").GetString() switch{"SCALAR"=>1,"VEC2"=>2,"VEC3"=>3,"VEC4"=>4,_=>throw new InvalidDataException("Unsupported accessor type.")};
                if(arity<components)throw new InvalidDataException("Invalid accessor arity.");
                int stride=v.TryGetProperty("byteStride",out var st)?st.GetInt32():bytes*arity;
                if(count<0 || offset<0 || stride<bytes*components || (long)offset+(long)Math.Max(0,count-1)*stride+bytes*components>binaryLength)
                    throw new InvalidDataException("GLB accessor outside buffer.");
                return (binaryStart+offset,count,stride,type);
            }
            Vector3 Read3(int id,int index)
            {
                var a=Access(id,3);if(a.Type!=5126 || index<0 || index>=a.Count)throw new InvalidDataException("Invalid vertex accessor.");
                int o=a.Offset+index*a.Stride;
                return new Vector3(BitConverter.ToSingle(data,o),BitConverter.ToSingle(data,o+4),BitConverter.ToSingle(data,o+8));
            }
            var model=new GlbTruckModel();
            long decodedPayload = 0;
            foreach(var node in root.GetProperty("nodes").EnumerateArray())
            {
                if (node.TryGetProperty("skin", out _) || node.TryGetProperty("matrix", out _) || node.TryGetProperty("translation", out _)
                    || node.TryGetProperty("rotation", out _) || node.TryGetProperty("scale", out _))
                    throw new NotSupportedException("Vehicle importer requires flattened unskinned nodes.");
                var extras=node.GetProperty("extras");var pivot=extras.GetProperty("pivot");
                if (pivot.GetArrayLength() != 3) throw new InvalidDataException("Vehicle pivot.");
                Vector3 partPivot = new(pivot[0].GetSingle(), pivot[1].GetSingle(), pivot[2].GetSingle());
                if (!Finite(partPivot)) throw new InvalidDataException("Non-finite vehicle pivot.");
                foreach(var primitive in root.GetProperty("meshes")[node.GetProperty("mesh").GetInt32()].GetProperty("primitives").EnumerateArray())
                {
                    if(primitive.GetProperty("mode").GetInt32()!=4)throw new InvalidDataException("Vehicle must use triangles.");
                    var attributes=primitive.GetProperty("attributes");
                    int positions=attributes.GetProperty("POSITION").GetInt32(),normals=attributes.GetProperty("NORMAL").GetInt32(),colours=attributes.GetProperty("COLOR_0").GetInt32();
                    var indices=Access(primitive.GetProperty("indices").GetInt32(),1);
                    if(indices.Type!=5125 || indices.Count%3!=0)throw new InvalidDataException("Invalid triangle indices.");
                    decodedPayload += (long)indices.Count * Vertex.Stride * 2; // Part data plus grouped draw copies.
                    if (decodedPayload > AnimatedGlbModel.MaxDecodedAssetBytes) throw new NotSupportedException("Vehicle exceeds the decoded asset budget.");
                    var vertices=new Vertex[indices.Count];var c=Access(colours,4);
                    if(c.Type!=5126 || c.Count != Access(positions, 3).Count)throw new InvalidDataException("Invalid colour accessor.");
                    for(int i=0;i<vertices.Length;i++){
                        int index=checked((int)BitConverter.ToUInt32(data,indices.Offset+i*indices.Stride));
                        Vector3 p=Read3(positions,index),n=Read3(normals,index),rgb=Read3(colours,index);
                        if(!Finite(p)||!Finite(n)||!Finite(rgb)||!float.IsFinite(n.LengthSquared)||n.LengthSquared<0.001f)throw new InvalidDataException("Invalid vehicle geometry.");
                        byte Channel(float value)=>(byte)Math.Clamp((int)(MathF.Pow(Math.Clamp(value,0,1),1/2.2f)*255+0.5f),0,255);
                        // Glass is deliberately opaque tinted geometry in this diorama asset.
                        uint color=(uint)(Channel(rgb.X)|(Channel(rgb.Y)<<8)|(Channel(rgb.Z)<<16)|unchecked((int)0xff000000));
                        if (extras.GetProperty("category").GetString() == "cargo") {
                            Vector3 wood = MathF.Abs(n.X) > 0.65f ? new Vector3(0.68f,0.48f,0.27f) : new Vector3(0.32f,0.19f,0.10f);
                            color = (uint)((byte)(wood.X*255) | ((byte)(wood.Y*255)<<8) | ((byte)(wood.Z*255)<<16) | unchecked((int)0xff000000));
                        }
                        vertices[i]=new Vertex(p,n.Normalized(),color);
                    }
                    model.Parts.Add(new Part{Name=node.GetProperty("name").GetString(),Category=extras.GetProperty("category").GetString(),
                        Pivot=partPivot,Vertices=vertices});
                }
            }
            float minY=float.MaxValue,maxY=float.MinValue;
            foreach(var part in model.Parts)foreach(var vertex in part.Vertices){
                model.Radius=Math.Max(model.Radius,vertex.Position.Length);
                minY=Math.Min(minY,vertex.Position.Y);maxY=Math.Max(maxY,vertex.Position.Y);
            }
            model.Width=maxY-minY;
            if(!float.IsFinite(model.Width)||model.Width<=0)throw new InvalidDataException("Invalid vehicle width.");
            float front=0,rear=0;int frontCount=0,rearCount=0;
            foreach(var part in model.Parts)if(part.Category=="wheel"){
                if(part.Name.Contains("Front")){front+=part.Pivot.X;frontCount++;}
                else{rear+=part.Pivot.X;rearCount++;}
            }
            if(frontCount==0||rearCount==0)throw new InvalidDataException("Vehicle has no axle geometry.");
            front/=frontCount;rear/=rearCount;
            model.Wheelbase=front-rear;model.AxleMidpoint=(front+rear)*0.5f;
            model.BuildDrawGroups();
            return model;
        }
        private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
        public void Draw(Matrix4 transform,float cargoFill,float wheelAngle,float curvature=0,float scale=1,Matrix4? suspension=null,float outlineWidth=0,float articulation=0,float trailerPitch=0)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            int visibleCargo=(int)MathF.Ceiling(Math.Clamp(cargoFill,0,1)*cargoCount),cargoIndex=0;
            foreach(var part in drawParts){
                if(part.Category=="cargo" && cargoIndex++>=visibleCargo)continue;
                if(part.Buffer==null){
                    var candidate = new VertexBuffer(PrimitiveTopology.Triangles);
                    try { candidate.SetData(part.Vertices,false); part.Buffer = candidate; }
                    catch { candidate.Dispose(); throw; }
                }
                RenderDevice.PushModel();
                try{
                    float steer=part.Category=="wheel" && part.Name.Contains("Front")?
                        VehicleVisualMotion.Steering(curvature,Wheelbase*scale,part.Pivot.Y*scale):0;
                    Matrix4 local=part.Category=="wheel"?
                        Matrix4.CreateTranslation(-part.Pivot)*Matrix4.CreateRotationY(-wheelAngle)*Matrix4.CreateRotationZ(steer)*Matrix4.CreateTranslation(part.Pivot):
                        suspension ?? Matrix4.Identity;
                    RenderDevice.SetModel(local*transform);
                    var visuals = RenderDevice.Visuals;
                    SurfaceKind previousKind = visuals?.Kind ?? SurfaceKind.Vehicle;
                    if (visuals != null) visuals.Kind = part.Category == "cargo" ? SurfaceKind.LogCargo : part.Category == "wheel" ? SurfaceKind.Rubber : SurfaceKind.Vehicle;
                    try {
                        part.Buffer.DrawArray();
                        if (outlineWidth > 0 && visuals?.Active == true && !visuals.ShadowPass)
                        {
                            using (RenderDevice.CreateStateScope().Cull(RenderCullFace.Front))
                            {
                                visuals.Use(outlineWidth); part.Buffer.DrawArray(false);
                            }
                        }
                    } finally { if (visuals != null) visuals.Kind = previousKind; }
                }finally{RenderDevice.PopModel();}
            }
        }
        public void Dispose(){if(disposed)return;disposed=true;foreach(var part in drawParts)part.Buffer?.Dispose();}
    }
}
