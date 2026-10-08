using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using OpenTK.Mathematics;
namespace ForesTycoon.Models
{
    // CPU asset/pose layer. GPU buffers are shared by all instances in AnimatedModelRenderer.
    internal sealed partial class AnimatedGlbModel
    {
        internal enum AlphaMode { Opaque, Mask, Blend }
        internal sealed class Node {
            internal string Name;
            internal Vector3 Translation, Scale=Vector3.One;
            internal Quaternion Rotation=Quaternion.Identity;
            internal Matrix4? Matrix;
            internal int Parent=-1;
        }
        internal sealed class Mesh {
            internal int Node, Skin=-1, Image=-1;
            internal float[] Vertices; // position3, normal3, uv2, joints4, weights4
            internal uint[] Indices;
            internal Vector4 Color=Vector4.One;
            internal AlphaMode Alpha;
            internal float AlphaCutoff=0.5f;
            internal bool DoubleSided;
            internal Vector3 Center;
            internal Vector3? FlatColor;
        }
        internal sealed class Skin { internal int[] Joints; internal Matrix4[] InverseBind; }
        internal sealed class Clip { internal string Name; internal float Duration; internal Channel[] Channels; }
        internal Node[] Nodes;
        internal Skin[] Skins;
        internal Mesh[] Meshes;
        internal PngImage[] Images;
        internal readonly Dictionary<string,Clip> Clips=new(StringComparer.Ordinal);
        internal int[] Order;
        internal static Vector4 QuaternionVector(Quaternion q)=>new(q.X,q.Y,q.Z,q.W);
        internal Pose CreatePose()=>new(this);
        internal static AnimatedGlbModel Load(string path)
        {
            byte[] bytes=File.ReadAllBytes(path);
            if(bytes.Length<28||BitConverter.ToUInt32(bytes,0)!=0x46546c67||BitConverter.ToUInt32(bytes,4)!=2||BitConverter.ToUInt32(bytes,8)!=bytes.Length)
                throw new InvalidDataException("Invalid GLB.");
            int jsonSize=checked((int)BitConverter.ToUInt32(bytes,12));
            if(jsonSize>bytes.Length-28||BitConverter.ToUInt32(bytes,16)!=0x4e4f534a)throw new InvalidDataException("Invalid JSON chunk.");
            int binaryStart=28+jsonSize,binarySize=checked((int)BitConverter.ToUInt32(bytes,20+jsonSize));
            if(binarySize!=bytes.Length-binaryStart||BitConverter.ToUInt32(bytes,24+jsonSize)!=0x004e4942)throw new InvalidDataException("Invalid BIN chunk.");
            using var document=JsonDocument.Parse(bytes.AsMemory(20,jsonSize));var root=document.RootElement;
            if(root.TryGetProperty("extensionsRequired",out var required))foreach(var extension in required.EnumerateArray())
                if(extension.GetString()!="KHR_materials_pbrSpecularGlossiness")throw new NotSupportedException("Required GLB extension: "+extension.GetString());
            var accessors=root.GetProperty("accessors");var views=root.GetProperty("bufferViews");
            var cache=new Dictionary<int,float[]>();
            int Arity(JsonElement a)=>a.GetProperty("type").GetString() switch {"SCALAR"=>1,"VEC2"=>2,"VEC3"=>3,"VEC4"=>4,"MAT4"=>16,_=>throw new NotSupportedException("Accessor type.")};
            float[] Read(int id) {
                if(cache.TryGetValue(id,out var result))return result;
                var a=accessors[id];if(a.TryGetProperty("sparse",out _))throw new NotSupportedException("Sparse accessor.");
                var view=views[a.GetProperty("bufferView").GetInt32()];int count=a.GetProperty("count").GetInt32(),arity=Arity(a),type=a.GetProperty("componentType").GetInt32();
                int size=type is 5121 or 5120?1:type is 5122 or 5123?2:4;
                int stride=view.TryGetProperty("byteStride",out var v)?v.GetInt32():arity*size;
                int relative=a.TryGetProperty("byteOffset",out v)?v.GetInt32():0,offset=(view.TryGetProperty("byteOffset",out v)?v.GetInt32():0)+relative;
                if(count<1||stride<arity*size||relative<0||(long)relative+(long)(count-1)*stride+arity*size>view.GetProperty("byteLength").GetInt32()||offset<0||(long)offset+view.GetProperty("byteLength").GetInt32()-relative>binarySize)
                    throw new InvalidDataException("Accessor bounds.");
                bool normalized=a.TryGetProperty("normalized",out v)&&v.GetBoolean();
                result=new float[checked(count*arity)];
                for(int i=0;i<count;i++)for(int c=0;c<arity;c++) {
                    int at=binaryStart+offset+i*stride+c*size;
                    float value=type switch {5126=>BitConverter.ToSingle(bytes,at),5125=>BitConverter.ToUInt32(bytes,at),5123=>BitConverter.ToUInt16(bytes,at),5121=>bytes[at],5122=>BitConverter.ToInt16(bytes,at),5120=>(sbyte)bytes[at],_=>throw new NotSupportedException("Component type.")};
                    if(normalized)value=type switch {5121=>value/255,5123=>value/65535,5120=>Math.Max(-1,value/127),5122=>Math.Max(-1,value/32767),_=>value};
                    if(!float.IsFinite(value))throw new InvalidDataException("Non-finite accessor.");result[i*arity+c]=value;
                }
                cache.Add(id,result);return result;
            }
            Matrix4 Matrix(float[] m,int offset=0)=>new(m[offset],m[offset+1],m[offset+2],m[offset+3],m[offset+4],m[offset+5],m[offset+6],m[offset+7],m[offset+8],m[offset+9],m[offset+10],m[offset+11],m[offset+12],m[offset+13],m[offset+14],m[offset+15]);
            Vector4 Vector(JsonElement value)=>new(value[0].GetSingle(),value[1].GetSingle(),value[2].GetSingle(),value.GetArrayLength()>3?value[3].GetSingle():0);
            var model=new AnimatedGlbModel();var nodes=root.GetProperty("nodes");model.Nodes=new Node[nodes.GetArrayLength()];
            for(int i=0;i<model.Nodes.Length;i++) {
                var n=nodes[i];var node=new Node();model.Nodes[i]=node;
                node.Name=n.TryGetProperty("name",out var nodeName)?nodeName.GetString():null;
                if(n.TryGetProperty("translation",out var v))node.Translation=Vector(v).Xyz;
                if(n.TryGetProperty("scale",out v))node.Scale=Vector(v).Xyz;
                if(n.TryGetProperty("rotation",out v)){var q=Vector(v);node.Rotation=new Quaternion(q.X,q.Y,q.Z,q.W).Normalized();}
                if(n.TryGetProperty("matrix",out v)){var m=new float[16];for(int j=0;j<16;j++)m[j]=v[j].GetSingle();node.Matrix=Matrix(m);}
            }
            for(int i=0;i<model.Nodes.Length;i++)if(nodes[i].TryGetProperty("children",out var children))foreach(var child in children.EnumerateArray()) {
                int c=child.GetInt32();if(c<0||c>=model.Nodes.Length||model.Nodes[c].Parent!=-1)throw new InvalidDataException("Node hierarchy.");model.Nodes[c].Parent=i;
            }
            var order=new List<int>();var visited=new byte[model.Nodes.Length];
            void Visit(int i){if(visited[i]==2)return;if(visited[i]==1)throw new InvalidDataException("Node cycle.");visited[i]=1;if(model.Nodes[i].Parent>=0)Visit(model.Nodes[i].Parent);visited[i]=2;order.Add(i);}
            for(int i=0;i<model.Nodes.Length;i++)Visit(i);model.Order=order.ToArray();
            var skins=new List<Skin>();
            if(root.TryGetProperty("skins",out var skinList))foreach(var skin in skinList.EnumerateArray()) {
                var joints=skin.GetProperty("joints");if(joints.GetArrayLength()>64)throw new NotSupportedException("Maximum 64 joints per skin.");
                var data=new Skin {Joints=new int[joints.GetArrayLength()],InverseBind=new Matrix4[joints.GetArrayLength()]};
                float[] bind=skin.TryGetProperty("inverseBindMatrices",out var v)?Read(v.GetInt32()):null;
                for(int j=0;j<data.Joints.Length;j++) {data.Joints[j]=joints[j].GetInt32();if(data.Joints[j]<0||data.Joints[j]>=model.Nodes.Length)throw new InvalidDataException("Joint node.");data.InverseBind[j]=bind==null?Matrix4.Identity:Matrix(bind,j*16);}
                skins.Add(data);
            }
            model.Skins=skins.ToArray();var imageList=new List<PngImage>();
            if(root.TryGetProperty("images",out var images))foreach(var image in images.EnumerateArray()) {
                if(image.GetProperty("mimeType").GetString()!="image/png")throw new NotSupportedException("Embedded PNG required.");
                var view=views[image.GetProperty("bufferView").GetInt32()];int offset=view.TryGetProperty("byteOffset",out var v)?v.GetInt32():0,size=view.GetProperty("byteLength").GetInt32();
                if(offset<0||size<0||(long)offset+size>binarySize)throw new InvalidDataException("Image bounds.");imageList.Add(PngImage.Decode(bytes.AsSpan(binaryStart+offset,size).ToArray()));
            }
            model.Images=imageList.ToArray();var meshList=new List<Mesh>();
            for(int i=0;i<model.Nodes.Length;i++)if(nodes[i].TryGetProperty("mesh",out var meshId))foreach(var primitive in root.GetProperty("meshes")[meshId.GetInt32()].GetProperty("primitives").EnumerateArray()) {
                if(primitive.TryGetProperty("mode",out var mode)&&mode.GetInt32()!=4)throw new NotSupportedException("Triangle meshes required.");
                var a=primitive.GetProperty("attributes");float[] p=Read(a.GetProperty("POSITION").GetInt32()),n=Read(a.GetProperty("NORMAL").GetInt32());
                float[] uv=a.TryGetProperty("TEXCOORD_0",out var v)?Read(v.GetInt32()):new float[p.Length/3*2];
                int skin=nodes[i].TryGetProperty("skin",out v)?v.GetInt32():-1;
                if(skin>=model.Skins.Length)throw new InvalidDataException("Mesh skin.");
                float[] joints=skin>=0?Read(a.GetProperty("JOINTS_0").GetInt32()):null,weights=skin>=0?Read(a.GetProperty("WEIGHTS_0").GetInt32()):null;
                var mesh=new Mesh{Node=i,Skin=skin,Vertices=new float[p.Length/3*16]};
                for(int vertex=0;vertex<p.Length/3;vertex++) {
                    Array.Copy(p,vertex*3,mesh.Vertices,vertex*16,3);Array.Copy(n,vertex*3,mesh.Vertices,vertex*16+3,3);Array.Copy(uv,vertex*2,mesh.Vertices,vertex*16+6,2);
                    if(skin>=0){float sum=0;for(int j=0;j<4;j++){int joint=(int)joints[vertex*4+j];if(joint<0||joint>=model.Skins[skin].Joints.Length||weights[vertex*4+j]<0)throw new InvalidDataException("Vertex joint/weight.");mesh.Vertices[vertex*16+8+j]=joint;sum+=weights[vertex*4+j];}if(sum<=0)throw new InvalidDataException("Zero skin weights.");for(int j=0;j<4;j++)mesh.Vertices[vertex*16+12+j]=weights[vertex*4+j]/sum;}
                }
                Vector3 min=new(float.MaxValue),max=new(float.MinValue);
                for(int vertex=0;vertex<p.Length;vertex+=3) {
                    Vector3 point=new(p[vertex],p[vertex+1],p[vertex+2]);
                    min=Vector3.ComponentMin(min,point);max=Vector3.ComponentMax(max,point);
                }
                mesh.Center=(min+max)*0.5f;
                float[] indices=Read(primitive.GetProperty("indices").GetInt32());mesh.Indices=new uint[indices.Length];
                if(indices.Length%3!=0)throw new InvalidDataException("Triangle indices.");
                for(int j=0;j<indices.Length;j++){if(indices[j]<0||indices[j]>=p.Length/3||indices[j]!=(int)indices[j])throw new InvalidDataException("Index bounds.");mesh.Indices[j]=(uint)indices[j];}
                if(primitive.TryGetProperty("material",out var materialId)) {
                    var material=root.GetProperty("materials")[materialId.GetInt32()];JsonElement diffuse=default;
                    mesh.Alpha=material.TryGetProperty("alphaMode",out v)?v.GetString() switch {
                        "OPAQUE"=>AlphaMode.Opaque,"MASK"=>AlphaMode.Mask,"BLEND"=>AlphaMode.Blend,
                        _=>throw new InvalidDataException("Unknown material alpha mode.")}:AlphaMode.Opaque;
                    if(material.TryGetProperty("alphaCutoff",out v))mesh.AlphaCutoff=v.GetSingle();
                    if(!float.IsFinite(mesh.AlphaCutoff)||mesh.AlphaCutoff<0)throw new InvalidDataException("Invalid alpha cutoff.");
                    mesh.DoubleSided=material.TryGetProperty("doubleSided",out v)&&v.GetBoolean();
                    if(material.TryGetProperty("extensions",out v)&&v.TryGetProperty("KHR_materials_pbrSpecularGlossiness",out var spec))diffuse=spec;
                    else if(material.TryGetProperty("pbrMetallicRoughness",out v))diffuse=v;
                    if(diffuse.ValueKind!=JsonValueKind.Undefined) {
                        if(diffuse.TryGetProperty("diffuseFactor",out v)||diffuse.TryGetProperty("baseColorFactor",out v))mesh.Color=Vector(v);
                        if(diffuse.TryGetProperty("diffuseTexture",out v)||diffuse.TryGetProperty("baseColorTexture",out v))mesh.Image=root.GetProperty("textures")[v.GetProperty("index").GetInt32()].GetProperty("source").GetInt32();
                    }
                    if(mesh.Image < -1 || mesh.Image >= model.Images.Length)throw new InvalidDataException("Material image bounds.");
                }
                meshList.Add(mesh);
            }
            model.Meshes=meshList.ToArray();
            if(root.TryGetProperty("animations",out var animations))foreach(var animation in animations.EnumerateArray()) {
                var channels=new List<Channel>();float duration=0;var samplers=animation.GetProperty("samplers");
                foreach(var channel in animation.GetProperty("channels").EnumerateArray()) {
                    var sampler=samplers[channel.GetProperty("sampler").GetInt32()];var target=channel.GetProperty("target");
                    int node=target.GetProperty("node").GetInt32();
                    AnimationPath pathId=target.GetProperty("path").GetString() switch {
                        "translation"=>AnimationPath.Translation,"rotation"=>AnimationPath.Rotation,
                        "scale"=>AnimationPath.Scale,_=>throw new NotSupportedException("Morph animation not supported.")};
                    if(node<0||node>=model.Nodes.Length||model.Nodes[node].Matrix.HasValue)throw new InvalidDataException("Animated node must use TRS.");
                    string interpolation=sampler.TryGetProperty("interpolation",out var v)?v.GetString():"LINEAR";
                    var c=new Channel{Node=node,Path=pathId,Arity=pathId==AnimationPath.Rotation?4:3,
                        Interpolation=interpolation switch {
                            "LINEAR"=>AnimationInterpolation.Linear,"STEP"=>AnimationInterpolation.Step,
                            "CUBICSPLINE"=>AnimationInterpolation.CubicSpline,_=>throw new NotSupportedException("Animation interpolation.")},
                        Times=Read(sampler.GetProperty("input").GetInt32()),Values=Read(sampler.GetProperty("output").GetInt32())};
                    if(c.Values.Length!=c.Times.Length*c.Arity*(c.Interpolation==AnimationInterpolation.CubicSpline?3:1))throw new InvalidDataException("Animation values.");
                    for(int j=0;j<c.Times.Length;j++)if(c.Times[j]<0||(j>0&&c.Times[j]<=c.Times[j-1]))throw new InvalidDataException("Animation times.");
                    duration=Math.Max(duration,c.Times[^1]);channels.Add(c);
                }
                var clip=new Clip{Name=animation.TryGetProperty("name",out var name)?name.GetString():"clip-"+model.Clips.Count,Duration=duration,Channels=channels.ToArray()};model.Clips.Add(clip.Name,clip);
            }
            return model;
        }
    }
}
